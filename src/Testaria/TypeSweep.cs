using System.Reflection;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace Testaria;

/// <summary>
/// Every type in a mod that can be made without being told anything, for
/// checks that need a subject but no knowledge of it.
/// <para/>
/// <see cref="Assert.SettersAcceptTheirOwnGetters"/> and
/// <see cref="Assert.SettersSettleAfterOneWrite"/> ask a question of an object
/// without needing to understand it. That makes them worth asking of
/// everything rather than of the handful of types somebody thought to name, and
/// this is what turns "everything" into a list a <c>[CaseSource]</c> can
/// enumerate, so each type arrives as its own result rather than as one loop
/// whose first failure hides the rest.
/// <para/>
/// A default constructor is the whole of the qualification. A type needing
/// arguments cannot be built without knowing what they mean, which is exactly
/// the knowledge these checks are meant to do without.
/// </summary>
public static class TypeSweep
{
	/// <summary>
	/// The names of the types in a mod that a sweep can construct, sorted.
	/// <para/>
	/// Empty rather than throwing when the mod is absent: a
	/// <c>[CaseSource]</c> runs during discovery, where a skip would be
	/// reported as a broken source rather than as an absent mod. No cases is
	/// reported as a skip by the runner, which is the honest outcome.
	/// <para/>
	/// Full names, because two types in different namespaces may share a short
	/// one and a case has to name exactly one of them.
	/// </summary>
	public static IEnumerable<string> ConstructibleTypes(string modName)
		=> ModLoader.TryGetMod(modName, out Mod mod)
			? mod.Code.GetTypes().Where(Constructible).Select(t => t.FullName!).Order()
			: [];

	/// <summary>
	/// One of those types, made.
	/// <para/>
	/// A construction that throws is reported as a skip rather than a failure.
	/// These checks are about properties, and a constructor that will not run
	/// is a different subject: calling it a failure here would blame the wrong
	/// thing and bury whatever the properties would have said.
	/// </summary>
	public static object Construct(string modName, string typeName)
	{
		if (!ModLoader.TryGetMod(modName, out Mod mod))
			Assert.Skip($"{modName} is not installed, so there is nothing to sweep.");

		Type? type = mod.Code.GetType(typeName);

		if (type is null)
			Assert.Skip($"{modName} no longer has a type called {typeName}.");

		try {
			return Activator.CreateInstance(type)!;
		}
		catch (Exception bad) {
			Exception real = bad is TargetInvocationException { InnerException: { } inner } ? inner : bad;

			Assert.Skip($"{typeName} could not be constructed: "
				+ $"{real.GetType().Name}: {real.Message}");
			throw;
		}
	}

	/// <summary>
	/// The separator between a mod's name and a type's, in the names
	/// <see cref="EveryConstructibleType"/> produces. A slash, because a mod
	/// name cannot contain one and a type name cannot either.
	/// </summary>
	public const char Separator = '/';

	/// <summary>
	/// Every constructible type in every loaded mod, as <c>Mod/Type.Full.Name</c>.
	/// <para/>
	/// The reason this exists rather than a list per mod: the two property
	/// checks need no knowledge of their subject, so the set of subjects can be
	/// every type in every mod that happens to be installed, and a suite
	/// pointed at them needs no reference to any of them. Enable whichever mods
	/// are of interest and the sweep covers exactly those.
	/// <para/>
	/// The framework and any suite are left out: sweeping the thing doing the
	/// sweeping says nothing, and a suite's own types are not the subject. Both
	/// are recognised by name, which is crude and is the only signal available
	/// from here.
	/// </summary>
	public static IEnumerable<string> EveryConstructibleType()
		=> ModLoader.Mods
			.Where(mod => !IsHarness(mod.Name))
			.SelectMany(mod => ConstructibleTypes(mod.Name)
				.Select(type => $"{mod.Name}{Separator}{type}"))
			.Order();

	/// <summary>
	/// One of those, made, given the name <see cref="EveryConstructibleType"/>
	/// produced.
	/// </summary>
	public static object ConstructQualified(string qualified)
	{
		int split = qualified.IndexOf(Separator);

		if (split <= 0)
			Assert.Fail($"'{qualified}' does not name a mod and a type separated by '{Separator}'.");

		return Construct(qualified[..split], qualified[(split + 1)..]);
	}

	/// <summary>
	/// Whether a mod is this framework, a suite built on it, or tModLoader's
	/// own built-in one, none of which is a subject worth sweeping.
	/// </summary>
	private static bool IsHarness(string modName)
		=> modName == "ModLoader"
			|| modName == nameof(Testaria)
			|| modName.EndsWith("Test", StringComparison.Ordinal)
			|| modName.EndsWith("Tests", StringComparison.Ordinal);

	/// <summary>
	/// Whether this is content the loader registers, which a sweep cannot
	/// validly make for itself.
	/// <para/>
	/// Anything <c>ILoadable</c> is built and bound by tModLoader: it is given
	/// its mod, its name, and for <c>ModType&lt;TEntity&gt;</c> an entity whose
	/// fields its properties are named views onto, <c>IsStickingToTarget</c>
	/// reading <c>Projectile.ai[0]</c> and so on. An instance made by
	/// reflection has none of that, so reading such a property throws through
	/// no fault of the mod, and the instances the loader did make are live
	/// content a sweep must never write to.
	/// <para/>
	/// Measured twice while narrowing this. Excluding only the entity-bound
	/// kinds left 51 failures, every one a property tModLoader itself declares;
	/// restricting each check to the subject's own assembly took that to 14,
	/// every one a property the mod declares but the loader fills in. Both
	/// numbers were noise of the kind that teaches a reader to skim a report.
	/// <para/>
	/// <c>ModConfig</c> is the exception, and not an arbitrary one. A config is
	/// a bag of settings that tModLoader itself constructs and serialises
	/// freely, rather than behaviour bound to a registered thing, and those
	/// settings are read and written by a generated interface, which is exactly
	/// the round trip these checks are about.
	/// </summary>
	private static bool IsLoaderBound(Type type)
		=> typeof(ILoadable).IsAssignableFrom(type) && !typeof(ModConfig).IsAssignableFrom(type);

	/// <summary>
	/// Whether a sweep can make one of these without being told anything.
	/// <para/>
	/// Public, concrete, not generic, and with a public constructor taking no
	/// arguments. Enums, interfaces and delegates are excluded by the same
	/// tests: none of them is a thing with properties to write back.
	/// </summary>
	private static bool Constructible(Type type)
		=> type.IsPublic
			&& type.IsClass
			&& !type.IsAbstract
			&& !type.IsGenericTypeDefinition
			&& type.FullName is not null
			&& type.GetConstructor(Type.EmptyTypes) is not null
			&& !IsLoaderBound(type);
}
