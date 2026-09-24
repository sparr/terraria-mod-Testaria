namespace Testaria.Core.Tests;

public class TickRateGovernorTests
{
	[Fact]
	public void Before_any_tick_the_loop_is_allowed_to_start()
		=> Assert.True(new TickRateGovernor().IsBehindSchedule());

	[Fact]
	public void A_run_under_its_target_is_behind_and_may_catch_up()
	{
		var clock = new ManualClock();
		var governor = new TickRateGovernor(clock);
		governor.Retarget(100);

		governor.OnTick();
		clock.Advance(TimeSpan.FromSeconds(1));

		// One tick in a second, against a target of a hundred.
		Assert.True(governor.IsBehindSchedule());
	}

	[Fact]
	public void A_run_at_its_target_is_not_behind()
	{
		var clock = new ManualClock();
		var governor = new TickRateGovernor(clock);
		governor.Retarget(100);

		governor.OnTick();
		clock.Advance(TimeSpan.FromSeconds(1));

		for (int i = 0; i < 200; i++)
			governor.OnTick();

		Assert.False(governor.IsBehindSchedule());
	}

	[Fact]
	public void The_measured_rate_is_ticks_over_elapsed_time()
	{
		var clock = new ManualClock();
		var governor = new TickRateGovernor(clock);

		governor.OnTick();
		clock.Advance(TimeSpan.FromSeconds(2));

		for (int i = 0; i < 199; i++)
			governor.OnTick();

		Assert.InRange(governor.ActualTicksPerSecond, 99.9, 100.1);
	}

	[Fact]
	public void A_long_gap_earns_a_bounded_burst_rather_than_an_unlimited_one()
	{
		var clock = new ManualClock();
		var governor = new TickRateGovernor(clock);
		governor.Retarget(100);

		governor.OnTick();

		// Ten seconds with no ticks, as happens while the world is frozen.
		// Uncapped, that would be a thousand ticks of credit.
		clock.Advance(TimeSpan.FromSeconds(10));

		int burst = 0;
		while (governor.IsBehindSchedule() && burst < 1000) {
			governor.OnTick();
			burst++;
		}

		// The cap is a tenth of a second of backlog, so at a hundred a second
		// the burst is about ten ticks, and nowhere near the thousand owed.
		Assert.InRange(burst, 1, 50);
	}

	[Fact]
	public void Retargeting_forgets_the_schedule_so_there_is_no_catch_up_burst()
	{
		var clock = new ManualClock();
		var governor = new TickRateGovernor(clock);
		governor.Retarget(100);

		governor.OnTick();
		clock.Advance(TimeSpan.FromSeconds(10));

		// Ten seconds behind at a hundred a second would be a thousand ticks
		// of debt; retargeting must not carry it.
		governor.Retarget(100);

		Assert.Equal(0, governor.TicksTaken);
		Assert.Equal(0, governor.ElapsedSeconds);
	}

	[Fact]
	public void A_target_rate_must_be_positive()
		=> Assert.Throws<ArgumentOutOfRangeException>(() => new TickRateGovernor().Retarget(0));

	/// <summary>
	/// A clock that only moves when told to. Hand written rather than pulled
	/// from a package, so the core's tests keep needing nothing but xUnit.
	/// </summary>
	private sealed class ManualClock : TimeProvider
	{
		private long timestamp;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override long GetTimestamp() => timestamp;

		public void Advance(TimeSpan by) => timestamp += by.Ticks;
	}
}
