using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Testaria.Analyzers.Tests;

/// <summary>
/// Compiles a snippet against a stub of the Terraria surface and runs the
/// analyzer over it.
/// <para/>
/// Stubs rather than the real assemblies on purpose: these tests must run on a
/// machine with no game installed, which is the same reason the core carries
/// no tModLoader reference. What the analyzer matches on is fully qualified
/// names and shapes, so a stub with the right names is an honest subject.
/// </summary>
internal static class AnalyzerHarness
{
	/// <summary>
	/// The slice of Terraria the rule cares about, with the shapes that matter:
	/// a const on an *ID type, a nested Sets type beside it, and the
	/// collections that answer empty rather than throwing.
	/// </summary>
	public const string TerrariaStubs = """
		namespace Terraria
		{
			public static class Lang
			{
				public static string GetItemNameValue(int id) => "";
			}

			public class Item
			{
				public string Name = "";
				public int damage;
			}
		}

		namespace Terraria.Localization
		{
			public static class Language
			{
				public static string GetTextValue(string key) => "";
			}
		}

		namespace Terraria.ID
		{
			public static class ContentSamples
			{
				public static System.Collections.Generic.Dictionary<int, Terraria.Item> ItemsByType = new();
			}

			public static class ItemID
			{
				public const int Count = 5456;

				public static class Sets
				{
					public static bool[] Deprecated = new bool[Count];
				}
			}
		}

		namespace Terraria.ModLoader
		{
			public interface ILoadable
			{
			}

			public static class ModContent
			{
				public static int ItemType<T>() => 0;
			}

			public static class ModLoader
			{
				public static object[] Mods = System.Array.Empty<object>();
				public static string version = "1.4.5";
			}
		}
		""";

	/// <summary>Runs the analyzer over a snippet, which is compiled beside the stubs.</summary>
	/// <param name="source">The code under analysis.</param>
	/// <param name="loaderStateAnalysis">
	/// Value for the <c>TestariaLoaderStateAnalysis</c> build property, or
	/// null to leave it unset as an ordinary project would.
	/// </param>
	public static ImmutableArray<Diagnostic> Analyze(string source, string? loaderStateAnalysis = null)
	{
		CSharpCompilation compilation = CSharpCompilation.Create(
			"UnderTest",
			[CSharpSyntaxTree.ParseText(TerrariaStubs), CSharpSyntaxTree.ParseText(source)],
			References,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

		// A snippet that does not compile would produce no operations and
		// therefore no diagnostics, so every one of these tests would pass
		// whatever the analyzer did. Fail loudly instead.
		ImmutableArray<Diagnostic> errors = [..compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];

		if (errors.Length > 0)
			throw new InvalidOperationException("the snippet under test does not compile: " + string.Join("; ", errors));

		CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
			[new LoaderStateAnalyzer()],
			new AnalyzerOptions([], new Options(loaderStateAnalysis)));

		return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
	}

	/// <summary>Runs the analyzer and reports the ids it raised, in source order.</summary>
	public static string[] Ids(string source, string? loaderStateAnalysis = null)
		=> [..Analyze(source, loaderStateAnalysis)
			.OrderBy(d => d.Location.SourceSpan.Start)
			.Select(d => d.Id)];

	// Everything this test host runs on, which covers the framework types the
	// stubs use. The compilation needs no Terraria assembly, only Testaria's
	// own, for the attributes the rules read.
	private static ImmutableArray<MetadataReference> References { get; } = Build();

	private static ImmutableArray<MetadataReference> Build()
	{
		HashSet<string> paths = [];

		if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform) {
			foreach (string path in platform.Split(Path.PathSeparator)) {
				if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
					paths.Add(path);
			}
		}

		// Named rather than left to the platform list, because the rules
		// resolve Testaria's own attributes by metadata name and a test that
		// silently lost this reference would look like the analyzer ignoring
		// a declaration.
		paths.Add(typeof(RequiresLoadedGameAttribute).Assembly.Location);

		return [..paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
	}

	/// <summary>
	/// The one MSBuild property the rule reads, since nothing simpler exposes
	/// a global analyzer option to a hand-driven compilation.
	/// </summary>
	private sealed class Options(string? loaderStateAnalysis) : AnalyzerConfigOptionsProvider
	{
		public override AnalyzerConfigOptions GlobalOptions { get; } = new Global(loaderStateAnalysis);

		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

		private sealed class Global(string? loaderStateAnalysis) : AnalyzerConfigOptions
		{
			public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
			{
				value = key == "build_property.TestariaLoaderStateAnalysis" ? loaderStateAnalysis : null;

				return value is not null;
			}
		}
	}
}
