namespace Testaria;

/// <summary>How a <see cref="TestRunner"/> should behave.</summary>
public sealed record TestRunnerOptions
{
	/// <summary>Name recorded on the run, conventionally the mod under test.</summary>
	public string RunName { get; init; } = "Testaria";

	/// <summary>
	/// The highest tier this environment can actually honour.
	/// <para/>
	/// Tests above it are reported as skipped rather than run. This is the
	/// Tier 0 boundary made operational: a Tier 2 test attempted in a bare
	/// test host would not fail cleanly, it would run against
	/// default-initialized statics and might well pass.
	/// </summary>
	public TestTier MaxTier { get; init; } = TestTier.Unit;

	/// <summary>Tick budget for tests that declare none. One minute at 60 Hz.</summary>
	public int DefaultTimeoutTicks { get; init; } = 3600;

	/// <summary>
	/// The arena to lease boxes from. Null means no boxes are available, and
	/// any test asking for one is reported as an error rather than run
	/// somewhere undefined.
	/// </summary>
	public Arena? Arena { get; init; }

	/// <summary>
	/// Narrows the run. Filtered-out tests are omitted rather than reported,
	/// and the count held back is surfaced so a narrowed run never reads like
	/// a complete one.
	/// </summary>
	public TestFilter Filter { get; init; } = TestFilter.All;

	/// <summary>
	/// Whether the host can actually provide a freshly generated world for
	/// tests marked <c>[FreshWorld]</c>.
	/// <para/>
	/// False by default, and a test declaring it is then reported as skipped
	/// rather than run. Running it in whatever world happens to be loaded
	/// would prove nothing while reporting a pass, which is the exact failure
	/// this framework exists to prevent.
	/// </summary>
	public bool SupportsFreshWorld { get; init; }

	/// <summary>
	/// Whether to give every test marked <c>[MutatesGlobalState]</c> a world of
	/// its own, as though it had declared <c>[FreshWorld]</c>.
	/// <para/>
	/// False by default, which is the cheap and slightly dishonest answer: such
	/// a test restores what it changed and is written to be safe in a shared
	/// world, but its subject is global and restoration cannot be guaranteed for
	/// a subject that is already misbehaving. One switch opts the whole run in,
	/// rather than each suite deciding, because the cost is a world per test and
	/// that is a decision about the run rather than about any one test.
	/// <para/>
	/// Opting in also needs <see cref="SupportsFreshWorld"/>, for the same
	/// reason a <c>[FreshWorld]</c> test does: a world per test is something the
	/// host provides, not something the runner can conjure. Asking for isolation
	/// the host cannot give is reported as a skip rather than quietly ignored.
	/// </summary>
	public bool IsolateMutatingTests { get; init; }

	/// <summary>
	/// Keep the box of a failing test for inspection instead of recycling it,
	/// so an author can fly out and look at the wreckage.
	/// </summary>
	public bool KeepFailedBoxes { get; init; } = true;

	/// <summary>
	/// How to construct an instance for non-static test methods. Defaults to
	/// the parameterless constructor.
	/// </summary>
	public Func<Type, object?>? Activate { get; init; }

	/// <summary>
	/// How to build the context handed to a test that asks for one. Required
	/// only for tests that take an <see cref="ITestContext"/>.
	/// </summary>
	public Func<BoxLease?, ITestContext>? CreateContext { get; init; }

	/// <summary>
	/// Pacing and freeze state for the run, or null when the host cannot vary
	/// either. The runner applies each test's <c>[RealTime]</c> and
	/// <c>[StartPaused]</c> to it, and resets it between tests so one test's
	/// pause cannot strand the next.
	/// </summary>
	public RunPacing? Pacing { get; init; }

	/// <summary>
	/// The game's random generators, or null when the host controls none.
	/// <para/>
	/// Null means tests run against whatever roll the game happens to be on,
	/// and their results carry no seed, so a report never claims a
	/// reproducibility it cannot deliver.
	/// </summary>
	public IRandomControl? Random { get; init; }

	/// <summary>
	/// Shifts every test's seed at once, so a suite can be rerun against
	/// different rolls without editing a line of it.
	/// <para/>
	/// Zero by default, which makes the default run of a suite identical
	/// everywhere. A suite that only passes at one run seed is a suite with a
	/// real dependency on luck, and changing this is how that gets found.
	/// </summary>
	public int RunSeed { get; init; }

	/// <summary>Clock used for wall clock durations. Injectable so runs are reproducible under test.</summary>
	public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
