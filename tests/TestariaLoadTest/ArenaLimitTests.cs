using System.Collections;
using Terraria;
using Terraria.ID;
using Testaria;

namespace TestariaLoadTest;

/// <summary>
/// The edges rather than the volume: what the arena and the entity pools do
/// when they are asked for more than they have.
/// <para/>
/// A load test that only ever succeeds has not found anything. These are the
/// cases where the plan says the framework should complain loudly rather than
/// stall, corrupt, or quietly hand out something other than what was asked
/// for, and each of them is here to check that it does.
/// </summary>
public class ArenaLimitTests
{
	/// <summary>
	/// Spawns NPCs until Terraria's fixed pool runs out.
	/// <para/>
	/// The pool is 200 slots shared by the whole world, and a test asking for
	/// more is asking for something the game cannot give. What matters is that
	/// the framework says so rather than handing back entities that are not
	/// there, and that ownership survives the edge so teardown still cleans up.
	/// </summary>
	[GameTest(Band = Band.Cavern, Width = 96, Height = 64, Timeout = 1800)]
	public IEnumerator Spawning_past_the_pool_gives_back_inactive_entities(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		int alive = 0;
		int refused = 0;

		for (int i = 0; i < 260; i++) {
			NPC npc = box.SpawnNPC(NPCID.BlueSlime, 8 + (i % 80), 8 + (i % 40));

			if (npc.active)
				alive++;
			else
				refused++;
		}

		yield return Wait.Ticks(2);

		// The pool is 200 and the world has a few of its own, so somewhere
		// short of 260 the answers must start coming back inactive rather than
		// wrapping round and handing out somebody else's slot.
		Assert.True(refused > 0, $"260 spawns in a 200 slot pool, and none were refused: {alive} came back active");
		Assert.True(alive > 100, $"only {alive} of 260 spawns succeeded, which is fewer than the pool should have held");
	}

	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator The_box_after_the_pool_ran_out_is_clean(ITestContext ctx)
	{
		yield return Wait.Ticks(2);

		int inside = 0;

		for (int i = 0; i < Main.npc.Length; i++) {
			if (Main.npc[i].active && BoxSpace.Contains(ctx.Interior, new WorldPoint(Main.npc[i].Center.X, Main.npc[i].Center.Y)))
				inside++;
		}

		// The point of the previous test: however badly it ended, the arena
		// hands on a clean box.
		Assert.Equal(0, inside);
	}
}
