namespace Testaria.Core.Tests;

public class RunPacingTests
{
	[Fact]
	public void A_new_run_is_realtime_and_not_frozen()
	{
		var pacing = new RunPacing();

		Assert.Equal(PacingMode.Realtime, pacing.Mode);
		Assert.False(pacing.IsFrozen);
		Assert.Null(pacing.PendingSteps);
	}

	[Fact]
	public void Pausing_freezes_the_world_and_resuming_thaws_it()
	{
		var pacing = new RunPacing();

		pacing.Pause();
		Assert.True(pacing.IsFrozen);

		pacing.Resume();
		Assert.False(pacing.IsFrozen);
	}

	[Fact]
	public void A_granted_step_thaws_for_exactly_that_many_ticks()
	{
		var pacing = new RunPacing();
		pacing.Pause();

		pacing.GrantSteps(3);

		for (int i = 0; i < 3; i++) {
			Assert.False(pacing.IsFrozen);
			pacing.OnWorldTick();
		}

		Assert.True(pacing.IsFrozen);
	}

	[Fact]
	public void Stepping_while_running_pauses_first()
	{
		var pacing = new RunPacing();

		pacing.GrantSteps(2);

		Assert.Equal(2, pacing.PendingSteps);
		pacing.OnWorldTick();
		pacing.OnWorldTick();
		Assert.True(pacing.IsFrozen);
	}

	[Fact]
	public void Grants_accumulate_rather_than_replace()
	{
		var pacing = new RunPacing();
		pacing.Pause();

		pacing.GrantSteps(2);
		pacing.GrantSteps(3);

		Assert.Equal(5, pacing.PendingSteps);
	}

	[Fact]
	public void An_unpaused_world_never_runs_out_of_ticks()
	{
		var pacing = new RunPacing();

		for (int i = 0; i < 1000; i++)
			pacing.OnWorldTick();

		Assert.False(pacing.IsFrozen);
		Assert.Null(pacing.PendingSteps);
	}

	[Fact]
	public void A_step_must_advance_at_least_one_tick()
		=> Assert.Throws<ArgumentOutOfRangeException>(() => new RunPacing().GrantSteps(0));

	[Fact]
	public void A_bounded_rate_must_be_positive()
		=> Assert.Throws<ArgumentOutOfRangeException>(() => new RunPacing().SetMode(PacingMode.Bounded, 0));

	[Fact]
	public void A_realtime_test_overrides_the_runs_fast_forward()
	{
		var pacing = new RunPacing();
		pacing.SetMode(PacingMode.Unbounded);

		Assert.Equal(PacingMode.Unbounded, pacing.EffectiveMode);

		pacing.CurrentTestWantsRealtime = true;

		Assert.Equal(PacingMode.Realtime, pacing.EffectiveMode);
		// The run's own choice survives, so the next test gets it back.
		Assert.Equal(PacingMode.Unbounded, pacing.Mode);
	}

	[Fact]
	public void Resetting_between_tests_thaws_and_clears_the_opt_out()
	{
		var pacing = new RunPacing();
		pacing.SetMode(PacingMode.Unbounded);
		pacing.CurrentTestWantsRealtime = true;
		pacing.Pause();

		pacing.ResetForNextTest();

		Assert.False(pacing.IsFrozen);
		Assert.False(pacing.CurrentTestWantsRealtime);
		// The run's pacing is not a per-test thing and must survive.
		Assert.Equal(PacingMode.Unbounded, pacing.Mode);
	}
}
