using System.Collections;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// Tests against someone else's mod, as a calibration of the framework.
/// <para/>
/// ExampleMod is the right first subject not because it is small, at 563 files
/// it is not, but because it is broad and shallow: every piece of it is a
/// minimal, deliberately documented demonstration. And the tModLoader team
/// maintains it, so a test that cannot be made to work here is a framework bug
/// rather than a quirk of somebody's mod, which is exactly what a calibration
/// standard has to guarantee.
/// <para/>
/// Content is resolved by name rather than by a compile-time reference, which
/// is how cross-mod code is normally written, and which lets this mod load and
/// report honestly when ExampleMod is absent instead of failing to load at all.
/// </summary>
public class ExampleModTests
{
	private const string Subject = "ExampleMod";

	private static Mod RequireExampleMod()
	{
		if (!ModLoader.TryGetMod(Subject, out Mod mod))
			Assert.Skip($"{Subject} is not installed, so there is nothing to calibrate against.");

		return mod;
	}

	[LoadedTest]
	public void The_subject_mod_is_loaded()
	{
		Mod mod = RequireExampleMod();

		Assert.Equal(Subject, mod.Name);
	}

	[LoadedTest]
	public void Its_item_is_registered_above_the_vanilla_range()
	{
		RequireExampleMod();

		Assert.True(
			ModContent.TryFind(Subject, "ExampleItem", out ModItem item),
			"ExampleItem should resolve by name once the mod is loaded");

		// Modded ids are assigned above the vanilla count at load, so anything
		// at or below it would mean the lookup found vanilla content.
		Assert.True(item.Type >= ItemID.Count, $"expected a modded id above {ItemID.Count}, got {item.Type}");
	}

	[LoadedTest]
	public void Its_item_has_a_localized_display_name()
	{
		RequireExampleMod();
		Assert.True(ModContent.TryFind(Subject, "ExampleItem", out ModItem item));

		// A Tier 1 assertion by construction: localization is only populated
		// once a load pass has run.
		Assert.NotEmpty(item.DisplayName.Value);
	}

	[LoadedTest]
	public void Its_critter_is_registered()
	{
		RequireExampleMod();

		Assert.True(
			ModContent.TryFind(Subject, "ExampleCritterNPC", out ModNPC critter),
			"ExampleCritterNPC should resolve by name");
		Assert.True(critter.Type >= NPCID.Count, $"expected a modded id above {NPCID.Count}, got {critter.Type}");
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator Its_critter_can_be_spawned_and_lives(ITestContext ctx)
	{
		RequireExampleMod();

		if (!ModContent.TryFind(Subject, "ExampleCritterNPC", out ModNPC critter))
			Assert.Fail("ExampleCritterNPC should resolve by name");

		var box = (TestContext)ctx;
		NPC spawned = box.SpawnNPC(critter.Type, 8, 8);

		Assert.True(spawned.active, "the critter should be active immediately after spawning");
		Assert.Equal(critter.Type, spawned.type);

		// Let it run its AI for a second. A critter has no reason to die in an
		// empty box, so still being alive is the assertion.
		yield return Wait.Seconds(1);

		Assert.True(spawned.active, "the critter should still be alive after a second of its own AI");
	}
}
