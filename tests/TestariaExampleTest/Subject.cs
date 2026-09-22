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
}
