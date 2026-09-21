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

	/// <summary>Clock used for wall clock durations. Injectable so runs are reproducible under test.</summary>
	public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
