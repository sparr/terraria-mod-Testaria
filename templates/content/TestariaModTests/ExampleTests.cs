using System.Collections;
using Terraria.ModLoader;
using Testaria;

namespace TestariaModTests;

/// <summary>
/// Placeholder tests, meant to be replaced.
/// <para/>
/// They exist to go green once before you write anything of your own. That
/// proves your tModLoader path, scratch save directory and world provisioning
/// all work, which is the hardest part of getting started; test authoring is
/// the easy part.
/// </summary>
public class ExampleTests
{
	/// <summary>
	/// Tier 1. Needs a completed load pass, but no world.
	/// <para/>
	/// Content registration, recipes, ID sets, config serialization and
	/// localization coverage all belong here.
	/// </summary>
	[LoadedTest]
	public void The_mod_under_test_is_loaded()
	{
		Assert.True(
			ModLoader.TryGetMod("SUBJECT_MOD", out Mod mod),
			"SUBJECT_MOD should be loaded. Check modReferences in build.txt.");

		Assert.Equal("SUBJECT_MOD", mod.Name);
	}

	/// <summary>
	/// Tier 2. Needs a world and a running tick loop, and is given a box of
	/// its own to work in.
	/// <para/>
	/// Everything is relative to the box, so a test never needs to know where
	/// in the world it was placed and cannot reach outside it.
	/// </summary>
	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator The_box_is_somewhere_sensible(ITestContext ctx)
	{
		Assert.Equal(Band.Cavern, ctx.Bands);
		Assert.False(ctx.Interior.IsEmpty, "a Tier 2 test should be given a box");
		Assert.True(ctx.Bounds.Contains(ctx.Interior), "the interior sits inside the gutter");

		yield break;
	}

	/// <summary>
	/// Tier 2, showing how a gameplay assertion spans ticks. Yielding a wait
	/// is how a test says where it is willing to be suspended; the body
	/// resumes where the tick loop allows and nowhere else.
	/// </summary>
	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator Time_passes_between_yields(ITestContext ctx)
	{
		int before = ctx.ElapsedTicks;

		yield return Wait.Seconds(0.5);

		Assert.True(ctx.ElapsedTicks > before, "ticks should have advanced");
	}

	// To place tiles or spawn entities, cast the context to Testaria.TestContext:
	//
	//     var box = (TestContext)ctx;
	//     box.PlaceTile(4, 4, TileID.Stone);
	//     NPC zombie = box.SpawnNPC(NPCID.Zombie, 8, 8);
	//
	// That type lives in the Testaria mod assembly rather than in
	// Testaria.Core, because it touches Terraria types. Compiling against it
	// needs a reference to the built Testaria.dll, for example:
	//
	//     <Reference Include="Testaria">
	//       <HintPath>/path/to/ModSources/Testaria/bin/Debug/net10.0/Testaria.dll</HintPath>
	//       <Private>false</Private>
	//     </Reference>
	//
	// The geometry and timing above need only the NuGet package, which is why
	// the generated project builds as-is.
}
