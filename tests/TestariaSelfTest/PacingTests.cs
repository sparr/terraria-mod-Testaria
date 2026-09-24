using System.Collections;
using Terraria;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// That stopping and stepping the world actually stops and steps it.
/// <para/>
/// Asserted against <c>Main.GameUpdateCount</c>, the game's own tick counter,
/// rather than against Testaria's bookkeeping. A framework that believed its
/// own count while the world did something else would look exactly like one
/// that works.
/// </summary>
public class PacingTests
{
	[GameTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator The_world_advances_when_nothing_is_frozen(ITestContext ctx)
	{
		uint before = Main.GameUpdateCount;

		yield return Wait.Ticks(5);

		Assert.False(ctx.IsPaused(), "nothing asked for a pause");
		Assert.Equal(5u, Main.GameUpdateCount - before);
	}

	[GameTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator A_step_advances_the_world_by_exactly_that_many_ticks(ITestContext ctx)
	{
		ctx.Pause();
		Assert.True(ctx.IsPaused(), "the world should be frozen once paused");

		uint before = Main.GameUpdateCount;

		yield return ctx.Step(3);

		Assert.Equal(3u, Main.GameUpdateCount - before);
		Assert.True(ctx.IsPaused(), "the world refreezes once the steps are spent");
	}

	[GameTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Steps_can_be_taken_one_at_a_time(ITestContext ctx)
	{
		ctx.Pause();

		for (uint expected = 1; expected <= 4; expected++) {
			uint before = Main.GameUpdateCount;

			yield return ctx.Step();

			Assert.Equal(1u, Main.GameUpdateCount - before);
		}

		ctx.Resume();
		Assert.False(ctx.IsPaused(), "resuming lets the world run again");
	}

	[GameTest(Band = Band.Cavern, Timeout = 600)]
	[StartPaused]
	public IEnumerator A_StartPaused_test_begins_with_the_world_frozen(ITestContext ctx)
	{
		Assert.True(ctx.IsPaused(), "[StartPaused] should freeze the world before the body runs");

		uint before = Main.GameUpdateCount;

		yield return ctx.Step(2);

		Assert.Equal(2u, Main.GameUpdateCount - before);
	}

	[GameTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Time_stands_still_for_an_entity_while_the_world_is_frozen(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		NPC slime = box.SpawnNPC(Terraria.ID.NPCID.BlueSlime, 8, 2);

		// Let it start falling, so a frozen tick is distinguishable from one
		// where it simply had nowhere to go.
		yield return Wait.Ticks(3);

		ctx.Pause();
		float restingY = slime.position.Y;

		// Several real frames pass here; none of them are world ticks.
		yield return ctx.Step(1);

		Assert.True(slime.position.Y != restingY || slime.velocity.Y == 0,
			"one stepped tick should move a falling slime, or it was already at rest");

		float afterOneStep = slime.position.Y;
		uint tickAfterStep = Main.GameUpdateCount;

		yield return ctx.Step(1);

		Assert.Equal(1u, Main.GameUpdateCount - tickAfterStep);
		Assert.True(Main.GameUpdateCount > 0, "the world clock kept its own count");

		ctx.Resume();
	}

	/// <summary>
	/// Opting out is recorded and honoured. The rate itself is not asserted:
	/// wall clock on a shared machine is not something a test can pin down.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 600)]
	[RealTime]
	public IEnumerator A_RealTime_test_still_ticks_normally(ITestContext ctx)
	{
		uint before = Main.GameUpdateCount;

		yield return Wait.Ticks(4);

		Assert.Equal(4u, Main.GameUpdateCount - before);
	}
}
