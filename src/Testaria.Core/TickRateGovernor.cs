namespace Testaria;

/// <summary>
/// Decides whether the game loop may skip its pacing sleep, so that a run
/// settles at a chosen number of ticks per second.
/// <para/>
/// A schedule rather than a per-tick delay. It compares ticks actually taken
/// against ticks the target rate would have allowed by now, and lets the loop
/// run flat out only while it is behind. That keeps the average honest across
/// a run whose ticks vary in cost, where sleeping a computed amount each tick
/// would drift.
/// <para/>
/// The clock is injectable so the arithmetic can be tested without waiting.
/// </summary>
public sealed class TickRateGovernor(TimeProvider? timeProvider = null)
{
	private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
	private long startedAt;
	private long nextTickDue;
	private long ticksTaken;
	private bool started;

	/// <summary>Ticks per second this governor is aiming for.</summary>
	public double TargetTicksPerSecond { get; private set; } = 60;

	/// <summary>Ticks counted since the last reset. Diagnostic.</summary>
	public long TicksTaken => ticksTaken;

	/// <summary>Seconds of wall clock since the last reset, or zero before the first tick.</summary>
	public double ElapsedSeconds => started ? clock.GetElapsedTime(startedAt).TotalSeconds : 0;

	/// <summary>The rate actually achieved so far, or zero before it can be known.</summary>
	public double ActualTicksPerSecond => ElapsedSeconds > 0 ? ticksTaken / ElapsedSeconds : 0;

	/// <summary>
	/// How far behind schedule the governor will let itself get before it
	/// gives up on the missing ticks. A hundred milliseconds is several of the
	/// server loop's sleep quanta, so a burst can bridge one, while a freeze
	/// of any length costs at most this much catch-up.
	/// </summary>
	private const double MaxBacklogSeconds = 0.1;

	private long PeriodTimestamps => (long)(clock.TimestampFrequency / TargetTicksPerSecond);

	private long MaxBacklogTimestamps => (long)(clock.TimestampFrequency * MaxBacklogSeconds);

	/// <summary>Aims at a new rate and forgets the schedule so far.</summary>
	public void Retarget(double ticksPerSecond)
	{
		if (!(ticksPerSecond > 0))
			throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), ticksPerSecond,
				"A target rate must be a positive number of ticks per second.");

		TargetTicksPerSecond = ticksPerSecond;
		Reset();
	}

	/// <summary>Forgets the schedule, so a pause or a mode change costs no catch-up burst.</summary>
	public void Reset()
	{
		started = false;
		ticksTaken = 0;
	}

	/// <summary>Counts a tick against the schedule and books when the next one is due.</summary>
	public void OnTick()
	{
		long now = clock.GetTimestamp();

		if (!started) {
			started = true;
			startedAt = now;
			nextTickDue = now;
		}

		ticksTaken++;
		nextTickDue += PeriodTimestamps;

		// Never bank more than one period of credit. Ticks do not happen while
		// a test is being set up or while the world is frozen, and a schedule
		// that remembered every one of those would then run flat out to catch
		// up, overshooting the rate that was asked for.
		// Allow a bounded backlog, and no more.
		//
		// Some catch-up is necessary rather than sloppy: the loop's sleep is
		// all or nothing at about 16ms, so any rate above 60 has to be reached
		// by skipping several sleeps in a row and then taking one. Capping the
		// backlog at a single period would forbid that and peg every bounded
		// rate at 60. Leaving it uncapped banks credit through every pause and
		// every test setup, and then spends it flat out, overshooting the rate
		// that was asked for. A backlog of a few sleep quanta does both jobs.
		long floor = now - MaxBacklogTimestamps;

		if (nextTickDue < floor)
			nextTickDue = floor;
	}

	/// <summary>
	/// How long until the next tick is due, or zero or less when one is due
	/// now.
	/// <para/>
	/// The useful form for a caller that can choose how long to wait. Sleeping
	/// this long keeps one tick per loop iteration, which is what preserves
	/// every hook's normal cadence; skipping the sleep outright would instead
	/// hand the rate over to whatever the machine manages.
	/// </summary>
	public TimeSpan TimeUntilDue()
	{
		if (!started)
			return TimeSpan.Zero;

		long remaining = nextTickDue - clock.GetTimestamp();

		return remaining <= 0
			? TimeSpan.Zero
			: TimeSpan.FromSeconds((double)remaining / clock.TimestampFrequency);
	}

	/// <summary>
	/// True while the run is behind the rate it is aiming for, and so may skip
	/// the loop's sleep to catch up.
	/// </summary>
	public bool IsBehindSchedule()
	{
		// Before the first tick there is no schedule to be behind, but the
		// loop should still be allowed to start promptly.
		if (!started)
			return true;

		return clock.GetTimestamp() >= nextTickDue;
	}
}
