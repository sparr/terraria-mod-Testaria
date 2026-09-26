using System.Reflection;
using Terraria.ModLoader;

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
			&& type.GetConstructor(Type.EmptyTypes) is not null;
}
