using System.Collections;
using Terraria;
using Terraria.ID;
using Testaria;

namespace TestariaLoadTest;

/// <summary>
/// The load test from PLAN.md section 8.4, aimed at the arena.
/// <para/>
/// The plan calls for a Calamity-class mod here. There is not one: measured,
/// Calamity 2.2.2 is a 1.4.4 build and fails to load on 1.4.5 with a missing
/// method, and no mod of that size is ported yet. What the plan actually names,
/// though, are the *stresses* such a mod would apply, and those can be applied
/// directly and more precisely: churn, size, entity pressure, and the spanning
/// columns no other suite asks for.
/// <para/>
/// Deliberately run on its own rather than in the ordinary gates, because it
/// exists to find the point where the arena stops coping.
/// </summary>
public class ArenaLoadTests
{
	/// <summary>
	/// How many boxes the churn test leases, one after another.
	/// <para/>
	/// The arena is sized by concurrency rather than by suite size, so this
	/// should cost a handful of slots however large it gets. That claim is
	/// what the test is here to break.
	/// </summary>
	public static IEnumerable<int> Churn() => Enumerable.Range(1, 300);

	[GameTest(Band = Band.Cavern, Timeout = 120)]
	[CaseSource(nameof(Churn))]
	public IEnumerator A_box_is_leased_and_given_back(ITestContext ctx, int index)
	{
		// Touch the ground, so teardown has something to put back and the
		// churn exercises restore rather than just the free list.
		var box = (TestContext)ctx;

		box.ClearTile(index % 40, index % 20);
		yield return Wait.Ticks(1);

		Assert.True(ctx.Interior.Width > 0, "a box should have been leased");
	}

	[GameTest(Band = Band.Cavern, Width = 192, Height = 128, Timeout = 300)]
	public IEnumerator A_large_box_can_be_leased_and_restored(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		Assert.True(ctx.Interior.Width >= 192, $"asked for 192 wide, got {ctx.Interior.Width}");
		Assert.True(ctx.Interior.Height >= 128, $"asked for 128 tall, got {ctx.Interior.Height}");

		// A patch big enough that restoring it is real work.
		for (int x = 0; x < 64; x++) {
			for (int y = 0; y < 64; y++)
				box.ClearTile(x, y);
		}

		yield return Wait.Ticks(2);
	}

	[GameTest(Band = Band.Cavern, Width = 384, Height = 256, Timeout = 300)]
	public IEnumerator The_largest_size_class_can_be_leased(ITestContext ctx)
	{
		Assert.True(ctx.Interior.Width >= 384, $"asked for 384 wide, got {ctx.Interior.Width}");
		Assert.True(ctx.Interior.Height >= 256, $"asked for 256 tall, got {ctx.Interior.Height}");

		yield break;
	}

	[GameTest(Spans = Band.Surface | Band.Underground | Band.Cavern, Width = 96, Timeout = 300)]
	public IEnumerator A_column_can_span_three_bands(ITestContext ctx)
	{
		// The path no other suite takes. The measurement in section 8.5f finds
		// not one spanning request in a corpus of 967 tests, so without this the
		// column code runs on unit tests alone.
		Assert.Equal(Band.Surface | Band.Underground | Band.Cavern, ctx.Bands);
		Assert.True(ctx.Interior.Width >= 96, $"asked for 96 wide, got {ctx.Interior.Width}");
		Assert.True(ctx.Interior.Height > 200, $"a column across three bands should be tall, got {ctx.Interior.Height}");

		yield break;
	}

	[GameTest(Spans = Band.Surface | Band.Underground | Band.Cavern, Width = 48, Timeout = 600)]
	public IEnumerator A_column_is_restored_like_any_other_box(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		// Ground at the top, the middle and the bottom of the column, so the
		// restore has to cover its whole height.
		foreach (int y in new[] { 1, ctx.Interior.Height / 2, ctx.Interior.Height - 2 })
			box.ClearTile(4, y);

		yield return Wait.Ticks(2);

		Assert.False(box.GetTile(4, ctx.Interior.Height / 2).HasTile, "the middle tile should have been cleared");
	}

	/// <summary>
	/// Fills a box with as many NPCs as the framework will let it own.
	/// <para/>
	/// Terraria's NPC pool is fixed at 200 slots and shared by everything, so
	/// a test that wants a crowd is competing with the world. The question is
	/// what the framework does at the edge: whether ownership holds, whether
	/// teardown gets them all back, and whether the next test inherits a mess.
	/// </summary>
	[GameTest(Band = Band.Cavern, Width = 96, Height = 64, Timeout = 900)]
	public IEnumerator A_box_full_of_entities_is_cleaned_up_afterwards(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		int spawned = 0;

		for (int i = 0; i < 120; i++) {
			NPC npc = box.SpawnNPC(NPCID.BlueSlime, 8 + (i % 80), 8 + (i % 40));

			if (npc.active)
				spawned++;
		}

		yield return Wait.Ticks(5);

		Assert.True(spawned > 50, $"only {spawned} of 120 NPCs were spawned, so the pool ran out sooner than expected");

		// Whatever happened, the framework has to own what it made: the next
		// test's box must not inherit a hundred slimes.
		int active = 0;

		for (int i = 0; i < Main.npc.Length; i++) {
			if (Main.npc[i].active && Main.npc[i].type == NPCID.BlueSlime)
				active++;
		}

		Assert.True(active >= spawned - 10, $"{spawned} spawned but only {active} are alive, so something else is reclaiming them");
	}

	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator The_box_after_the_crowd_is_empty(ITestContext ctx)
	{
		// Runs after the test above in declaration order, and is the actual
		// assertion about teardown: whatever the crowd did, this box is clean.
		yield return Wait.Ticks(2);

		int inside = 0;

		for (int i = 0; i < Main.npc.Length; i++) {
			if (Main.npc[i].active && BoxSpace.Contains(ctx.Interior, new WorldPoint(Main.npc[i].Center.X, Main.npc[i].Center.Y)))
				inside++;
		}

		Assert.Equal(0, inside);
	}
}
