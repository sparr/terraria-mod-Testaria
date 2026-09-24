using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Testaria.Analyzers;

/// <summary>
/// Enforces the Tier 0 boundary: loader-dependent Terraria surface is an error
/// in an assembly that has no load pass underneath it.
/// <para/>
/// The measurements this rests on are in PLAN.md section 2.2. The short of it
/// is that the dangerous surface does not throw outside the game, it answers:
/// <c>ContentSamples</c> collections are empty, <c>ModLoader.Mods</c> is
/// empty, <c>Lang</c> returns <c>""</c>, and the <c>*ID.Sets</c> arrays are
/// vanilla-sized because nothing resized them for mods. A test asserting on
/// any of that goes green while proving nothing, and a green suite that proves
/// nothing is worse than no suite at all. <c>Main</c> is deliberately absent
/// from the list: it throws a <c>TypeInitializationException</c>, so it is
/// already self-correcting and does not need a diagnostic.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoaderStateAnalyzer : DiagnosticAnalyzer
{
	/// <summary>Diagnostic id for loader state touched where no load pass has run.</summary>
	public const string LoaderStateId = "TSTA001";

	/// <summary>Diagnostic id for an undeclared call into a member that needs the game.</summary>
	public const string UndeclaredDependencyId = "TSTA002";

	private const string Category = "Testaria.Tiers";
	private const string HelpUri = "https://github.com/sparr/terraria-mod-Testaria/blob/main/README.md#the-tier-0-boundary";

	/// <summary>Reported where loader-dependent surface is used outside the game.</summary>
	public static readonly DiagnosticDescriptor LoaderState = new(
		LoaderStateId,
		"Loader state is not available here",
		"'{0}' only means anything after a load pass, and this assembly runs without one",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
			"Outside a loaded game this surface does not throw, it answers: empty collections, empty "
			+ "strings, and vanilla-sized arrays. A test asserting on that passes while proving nothing. "
			+ "Move it to a [LoadedTest] or [GameTest] in a test mod, or mark the member "
			+ "[RequiresLoadedGame] so the dependency is declared and travels to its callers.",
		helpLinkUri: HelpUri);

	/// <summary>Reported where a caller reaches loader state through a member that declared it.</summary>
	public static readonly DiagnosticDescriptor UndeclaredDependency = new(
		UndeclaredDependencyId,
		"Undeclared dependency on a loaded game",
		"'{0}' is marked [RequiresLoadedGame] and '{1}' does not declare the same",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
			"A declared dependency on the game is only worth having if it travels. Marking the caller "
			+ "[RequiresLoadedGame] too carries it up to the test that has to answer for it; leaving it "
			+ "undeclared hides the boundary one call away from where it matters.",
		helpLinkUri: HelpUri);

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
		= ImmutableArray.Create(LoaderState, UndeclaredDependency);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.RegisterCompilationStartAction(OnCompilationStart);
	}

	private static void OnCompilationStart(CompilationStartAnalysisContext context)
	{
		if (IsDisabled(context.Options))
			return;

		Compilation compilation = context.Compilation;

		// A mod assembly is by definition loaded before its code runs, so the
		// boundary does not apply to it. Autoloadable content types implement
		// ILoadable, so declaring one is the compilation saying what it is.
		// This matters less than it looks: an analyzer arrives through NuGet,
		// and a mod build cannot consume NuGet (PLAN.md section 1.1.1). It is
		// here for the project that imports the analyzer by hand.
		if (DeclaresLoadableType(compilation))
			return;

		var surface = LoaderSurface.For(compilation);
		INamedTypeSymbol? requires = compilation.GetTypeByMetadataName("Testaria.RequiresLoadedGameAttribute");
		INamedTypeSymbol? testMarker = compilation.GetTypeByMetadataName("Testaria.TestariaTestAttribute");

		if (surface.IsEmpty && requires is null)
			return;

		// The assembly as a whole can declare the dependency, which is how a
		// test mod's own project would opt out wholesale if it ever imported
		// this analyzer.
		if (HasAttribute(compilation.Assembly, requires))
			return;

		context.RegisterOperationAction(
			operationContext => Inspect(operationContext, surface, requires, testMarker),
			OperationKind.Invocation,
			OperationKind.PropertyReference,
			OperationKind.FieldReference,
			OperationKind.MethodReference,
			OperationKind.EventReference,
			OperationKind.ObjectCreation);
	}

	private static void Inspect(
		OperationAnalysisContext context,
		LoaderSurface surface,
		INamedTypeSymbol? requires,
		INamedTypeSymbol? testMarker)
	{
		ISymbol? target = TargetOf(context.Operation);

		if (target is null)
			return;

		// A member that has declared the dependency, or a test that runs in a
		// tier where the game is present, is answering for it already.
		if (Declares(context.ContainingSymbol, requires, testMarker))
			return;

		if (surface.Describe(target) is string described) {
			context.ReportDiagnostic(Diagnostic.Create(LoaderState, context.Operation.Syntax.GetLocation(), described));
			return;
		}

		if (requires is not null && HasRequiresAttribute(target, requires)) {
			context.ReportDiagnostic(Diagnostic.Create(
				UndeclaredDependency,
				context.Operation.Syntax.GetLocation(),
				target.Name,
				context.ContainingSymbol?.Name ?? "this code"));
		}
	}

	private static ISymbol? TargetOf(IOperation operation) => operation switch {
		IInvocationOperation invocation => invocation.TargetMethod,
		IPropertyReferenceOperation property => property.Property,
		IFieldReferenceOperation field => field.Field,
		IMethodReferenceOperation method => method.Method,
		IEventReferenceOperation @event => @event.Event,
		IObjectCreationOperation creation => creation.Constructor,
		_ => null,
	};

	private static bool IsDisabled(AnalyzerOptions options)
		=> options.AnalyzerConfigOptionsProvider.GlobalOptions
			.TryGetValue("build_property.TestariaLoaderStateAnalysis", out string? value)
			&& (string.Equals(value, "false", System.StringComparison.OrdinalIgnoreCase)
				|| string.Equals(value, "disable", System.StringComparison.OrdinalIgnoreCase));

	private static bool DeclaresLoadableType(Compilation compilation)
	{
		INamedTypeSymbol? loadable = compilation.GetTypeByMetadataName("Terraria.ModLoader.ILoadable");

		if (loadable is null)
			return false;

		var pending = new Stack<INamespaceOrTypeSymbol>();
		pending.Push(compilation.Assembly.GlobalNamespace);

		while (pending.Count > 0) {
			foreach (ISymbol member in pending.Pop().GetMembers()) {
				if (member is not INamespaceOrTypeSymbol child)
					continue;

				if (child is INamedTypeSymbol type && type.AllInterfaces.Contains(loadable, SymbolEqualityComparer.Default))
					return true;

				pending.Push(child);
			}
		}

		return false;
	}

	private static bool Declares(ISymbol? symbol, INamedTypeSymbol? requires, INamedTypeSymbol? testMarker)
	{
		for (ISymbol? current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol) {
			foreach (AttributeData attribute in current.GetAttributes()) {
				if (requires is not null && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, requires))
					return true;

				if (testMarker is not null && InheritsFrom(attribute.AttributeClass, testMarker))
					return true;
			}
		}

		return false;
	}

	private static bool HasRequiresAttribute(ISymbol symbol, INamedTypeSymbol requires)
	{
		if (HasAttribute(symbol, requires))
			return true;

		// A whole type, or a whole assembly, can carry the declaration, which
		// is how a helper class full of game-dependent members says so once.
		return symbol.ContainingType is { } type && HasAttribute(type, requires)
			|| symbol.ContainingAssembly is { } assembly && HasAttribute(assembly, requires);
	}

	private static bool HasAttribute(ISymbol? symbol, INamedTypeSymbol? attribute)
	{
		if (symbol is null || attribute is null)
			return false;

		foreach (AttributeData data in symbol.GetAttributes()) {
			if (SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute))
				return true;
		}

		return false;
	}

	private static bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
	{
		for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType) {
			if (SymbolEqualityComparer.Default.Equals(current, baseType))
				return true;
		}

		return false;
	}

	/// <summary>
	/// The surface that answers wrongly rather than failing, resolved against
	/// one compilation.
	/// </summary>
	private readonly struct LoaderSurface
	{
		// Members of ModLoader that report on a load pass. The rest of the
		// type is version metadata and is harmless, so naming members rather
		// than the whole type keeps the rule honest.
		private static readonly ImmutableHashSet<string> ModLoaderMembers
			= ImmutableHashSet.Create("Mods", "GetMod", "TryGetMod", "HasMod");

		private readonly ImmutableArray<INamedTypeSymbol> whole;
		private readonly INamedTypeSymbol? modLoader;

		private LoaderSurface(ImmutableArray<INamedTypeSymbol> whole, INamedTypeSymbol? modLoader)
		{
			this.whole = whole;
			this.modLoader = modLoader;
		}

		/// <summary>True when the compilation references none of this surface.</summary>
		public bool IsEmpty => whole.IsDefaultOrEmpty && modLoader is null;

		/// <summary>Resolves the surface against a compilation, skipping what it does not reference.</summary>
		public static LoaderSurface For(Compilation compilation)
		{
			var whole = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

			foreach (string name in new[] {
				"Terraria.ID.ContentSamples",
				"Terraria.ModLoader.ModContent",
				"Terraria.Lang",
				"Terraria.Localization.Language",
			}) {
				if (compilation.GetTypeByMetadataName(name) is INamedTypeSymbol type)
					whole.Add(type);
			}

			return new LoaderSurface(whole.ToImmutable(), compilation.GetTypeByMetadataName("Terraria.ModLoader.ModLoader"));
		}

		/// <summary>Names the offence, or null when the symbol is safe without a game.</summary>
		public string? Describe(ISymbol symbol)
		{
			INamedTypeSymbol? container = symbol.ContainingType;

			if (container is null)
				return null;

			// The ID sets: parallel arrays indexed by content id, resized at
			// load. Outside the game they are vanilla-sized and answer anyway,
			// which is the quietest failure of the lot. The consts on the
			// enclosing *ID types are inlined at compile time and stay true,
			// so only the nested Sets type is at issue.
			if (container.Name == "Sets"
				&& container.ContainingType is INamedTypeSymbol outer
				&& outer.ContainingNamespace?.ToDisplayString() == "Terraria.ID") {
				return $"{outer.Name}.Sets.{symbol.Name}";
			}

			foreach (INamedTypeSymbol type in whole) {
				if (SymbolEqualityComparer.Default.Equals(container, type))
					return $"{type.Name}.{symbol.Name}";
			}

			if (modLoader is not null
				&& SymbolEqualityComparer.Default.Equals(container, modLoader)
				&& ModLoaderMembers.Contains(symbol.Name)) {
				return $"ModLoader.{symbol.Name}";
			}

			return null;
		}
	}
}
