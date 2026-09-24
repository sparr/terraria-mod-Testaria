namespace Testaria;

/// <summary>How fast a run is allowed to simulate.</summary>
public enum PacingMode
{
	/// <summary>
	/// The game's own 60 ticks per second. What a player would see, and the
	/// only mode in which wall clock and simulated time agree.
	/// </summary>
	Realtime,

	/// <summary>
	/// Up to <see cref="RunPacing.TargetTicksPerSecond"/> ticks per second.
	/// Faster than realtime but still a rate you chose, so a run takes about
	/// the same wall clock on a slow machine as on a fast one until the slow
	/// one runs out of CPU.
	/// </summary>
	Bounded,

	/// <summary>
	/// As fast as the machine manages. The quickest option and the least
	/// reproducible one, since the rate is then a property of the hardware.
	/// </summary>
	Unbounded,
}

/// <summary>
/// The pacing and freeze state of a run, shared between the runner and
/// whatever drives the game loop.
/// <para/>
/// Game-free on purpose. The core decides <em>whether</em> the world should
/// advance and how fast; the mod layer is what actually skips a sleep or a
/// world update. Keeping the decision here is what lets it be unit tested
/// without a game.
/// <para/>
/// Two separate ideas live here and are deliberately not merged. Pacing is how
/// fast ticks arrive, and is a property of the whole run. Freezing is whether
/// ticks arrive at all, and is a property of the test currently running.
/// </summary>
public sealed class RunPacing
{
	/// <summary>Budget meaning "no limit", as opposed to a count of ticks owed.</summary>
	private const int Unlimited = -1;

	private int worldTickBudget = Unlimited;

	/// <summary>How fast the run may simulate when nothing is frozen.</summary>
	public PacingMode Mode { get; private set; } = PacingMode.Realtime;

	/// <summary>
	/// The ceiling for <see cref="PacingMode.Bounded"/>, in ticks per second.
	/// Meaningless in the other modes.
	/// </summary>
	public double TargetTicksPerSecond { get; private set; } = 60;

	/// <summary>
	/// Set while the running test opted out of fast forward with
	/// <c>[RealTime]</c>. Its own property rather than a mode change, so the
	/// run's chosen pacing is not lost and restored around every such test.
	/// </summary>
	public bool CurrentTestWantsRealtime { get; set; }

	/// <summary>The pacing actually in force right now, opt-outs included.</summary>
	public PacingMode EffectiveMode => CurrentTestWantsRealtime ? PacingMode.Realtime : Mode;

	/// <summary>True when the world must not advance on this tick.</summary>
	public bool IsFrozen => worldTickBudget == 0;

	/// <summary>Ticks the world still owes a paused test, or null when unpaused.</summary>
	public int? PendingSteps => worldTickBudget == Unlimited ? null : worldTickBudget;

	/// <summary>Chooses how fast the run may go from here.</summary>
	/// <param name="mode">The pacing to adopt.</param>
	/// <param name="targetTicksPerSecond">The ceiling, used only by <see cref="PacingMode.Bounded"/>.</param>
	public void SetMode(PacingMode mode, double targetTicksPerSecond = 60)
	{
		if (mode == PacingMode.Bounded && !(targetTicksPerSecond > 0))
			throw new ArgumentOutOfRangeException(nameof(targetTicksPerSecond), targetTicksPerSecond,
				"A bounded rate must be a positive number of ticks per second.");

		Mode = mode;
		TargetTicksPerSecond = targetTicksPerSecond;
	}

	/// <summary>
	/// Stops the world advancing, from the next tick onwards.
	/// <para/>
	/// The tick a test pauses on still completes: the test is running inside
	/// it, and abandoning it half way would leave the world in a state no
	/// ordinary tick ever produces.
	/// </summary>
	public void Pause() => worldTickBudget = 0;

	/// <summary>Lets the world advance freely again.</summary>
	public void Resume() => worldTickBudget = Unlimited;

	/// <summary>
	/// Grants the world exactly <paramref name="ticks"/> more ticks, then
	/// freezes again.
	/// <para/>
	/// Additive while paused, so two grants in one tick are both honoured.
	/// Granting while unpaused pauses first, which is what "step from here"
	/// means and saves a test pairing every step with a pause.
	/// </summary>
	public void GrantSteps(int ticks)
	{
		if (ticks < 1)
			throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "A step must advance the world at least one tick.");

		worldTickBudget = worldTickBudget == Unlimited ? ticks : worldTickBudget + ticks;
	}

	/// <summary>
	/// Records that the world advanced one tick, spending a step if one was
	/// owed. Called by whatever actually let the tick through.
	/// </summary>
	public void OnWorldTick()
	{
		if (worldTickBudget > 0)
			worldTickBudget--;
	}

	/// <summary>
	/// Resets to the run's own pacing with nothing frozen. Called between
	/// tests so one test's pause cannot strand the next one.
	/// </summary>
	public void ResetForNextTest()
	{
		worldTickBudget = Unlimited;
		CurrentTestWantsRealtime = false;
	}
}
