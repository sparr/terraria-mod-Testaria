using System.Collections;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// NPCs, from registration through to actually existing in a world.
/// <para/>
/// The Tier 2 tests here are the ones that justify the whole framework: they
/// assert about a thing running in the game, over time, which no amount of
/// out-of-game testing can reach.
/// </summary>
public class NpcTests
{
	[LoadedTest]
	public void Every_npc_has_a_content_sample()
	{
		foreach (ModNPC npc in Subject.Content<ModNPC>()) {
			Assert.True(
				ContentSamples.NpcsByNetId.ContainsKey(npc.Type),
				$"NPC '{npc.Name}' (id {npc.Type}) has no content sample");
		}
	}

	[LoadedTest]
	public void Every_npc_has_positive_maximum_life()
	{
		// An NPC with no life dies the instant it is hit by anything,
		// including its own spawn effects.
		foreach (ModNPC npc in Subject.Content<ModNPC>()) {
			NPC sample = ContentSamples.NpcsByNetId[npc.Type];

			Assert.True(sample.lifeMax > 0, $"NPC '{npc.Name}' has {sample.lifeMax} maximum life");
		}
	}

	[LoadedTest]
	public void Every_npc_has_a_non_zero_size()
	{
		foreach (ModNPC npc in Subject.Content<ModNPC>()) {
			NPC sample = ContentSamples.NpcsByNetId[npc.Type];

			Assert.True(sample.width > 0 && sample.height > 0, $"NPC '{npc.Name}' is {sample.width}x{sample.height}");
		}
	}

	[GameTest(Band = Band.Cavern, Width = 96, Height = 64, Timeout = 600)]
	public IEnumerator Every_npc_can_be_spawned(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		// Spawning only. Whether an NPC then survives is its own business:
		// worm segments despawn at once without a head, which is correct
		// behaviour and not something a blanket test should call a failure.
		// Found by this test failing on ExampleWormBody.
		//
		// One test rather than one per NPC, because the framework has no
		// parameterised tests yet.
		foreach (ModNPC modNpc in Subject.Content<ModNPC>()) {
			NPC spawned = box.SpawnNPC(modNpc.Type, 8, 8);

			Assert.True(spawned.active, $"NPC '{modNpc.Name}' was not active immediately after spawning");
			Assert.Equal(modNpc.Type, spawned.type);

			yield return Wait.Ticks(1);
		}
	}

	[GameTest(Band = Band.Cavern, Width = 96, Height = 64, Timeout = 600)]
	public IEnumerator A_spawned_npc_starts_at_full_life(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		Assert.True(ModContent.TryFind(Subject.Name, "ExampleCritterNPC", out ModNPC critter));

		NPC spawned = box.SpawnNPC(critter.Type, 8, 8);
		yield return Wait.Ticks(2);

		Assert.Equal(spawned.lifeMax, spawned.life);
	}

	[GameTest(Band = Band.Cavern, Width = 96, Height = 64, Timeout = 600)]
	public IEnumerator A_spawned_npc_lands_where_it_was_asked_to(ITestContext ctx)
	{
		// Box-relative positioning is the framework's promise; this checks the
		// promise is kept against a real entity rather than only in unit tests.
		var box = (TestContext)ctx;

		Assert.True(ModContent.TryFind(Subject.Name, "ExampleCritterNPC", out ModNPC critter));

		NPC spawned = box.SpawnNPC(critter.Type, 8, 8);
		yield return Wait.Ticks(1);

		int tileX = TileCoordinates.ToTile(spawned.Center.X);
		int tileY = TileCoordinates.ToTile(spawned.Center.Y);

		Assert.True(ctx.Interior.Contains(tileX, tileY), $"NPC landed at {tileX},{tileY}, outside the box {ctx.Interior}");
	}
}
