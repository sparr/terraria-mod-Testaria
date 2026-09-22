using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// The mod under test, resolved by name.
/// <para/>
/// Nothing here references ExampleMod at compile time. Cross-mod code is
/// normally written this way, and it lets this mod load and report honestly
/// when its subject is absent rather than failing to load at all.
/// </summary>
internal static class Subject
{
	public const string Name = "ExampleMod";

	/// <summary>
	/// The subject mod, or a skip if it is not installed.
	/// <para/>
	/// Skipped rather than failed: the absence of an optional mod says nothing
	/// about whether this framework or that mod works.
	/// </summary>
	public static Mod Require()
	{
		if (!ModLoader.TryGetMod(Name, out Mod mod))
			Assert.Skip($"{Name} is not installed, so there is nothing to test against.");

		return mod;
	}

	/// <summary>Every piece of content of a given kind that the subject registers.</summary>
	public static IReadOnlyList<T> Content<T>() where T : ILoadable
		=> [.. Require().GetContent<T>()];

	/// <summary>
	/// The internal names of a kind of content, or nothing if the subject is
	/// absent.
	/// <para/>
	/// For use as a [CaseSource], which runs during discovery. Skipping there
	/// would report a broken source rather than an absent mod, so this returns
	/// empty instead and lets the framework report a skip for having no cases.
	/// <para/>
	/// Names rather than the objects themselves, because the name is what a
	/// case label should read as and what a filter should be able to match.
	/// </summary>
	public static IEnumerable<string> NamesOf<T>() where T : ILoadable, IModType
		=> ModLoader.TryGetMod(Name, out Mod mod) ? mod.GetContent<T>().Select(c => c.Name).Order() : [];
}
