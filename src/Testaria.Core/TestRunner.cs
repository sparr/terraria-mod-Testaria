using System.Collections;
using System.Reflection;

namespace Testaria;

/// <summary>Where a <see cref="TestRunner"/> has got to.</summary>
public enum RunnerState
{
	/// <summary>Not started.</summary>
	Idle,

	/// <summary>Working through the test list.</summary>
	Running,

	/// <summary>Done. <see cref="TestRunner.Result"/> is available.</summary>
	Finished,
}

/// <summary>
/// Drives discovered tests to completion, one tick per <see cref="Step"/>.
/// <para/>
/// A stepped state machine rather than a blocking loop, because the host is
/// Terraria's tick loop and the runner does not get to own it. The game calls
/// <see cref="Step"/> once per update; Tier 0 and Tier 1 bodies finish inside
/// a single call, while Tier 2 coroutines span as many as they need.
/// <para/>
/// Not thread safe, by the same reasoning as <see cref="Arena"/>.
/// </summary>
public sealed class TestRunner
{
	private readonly List<TestCase> queue;
	private readonly List<TestResult> results = [];
	private readonly TestRunnerOptions options;
	private int next;

	private TestCase? current;
	private TestCoroutine? coroutine;
	private BoxLease? lease;
	private ITestContext? context;
	private long startedAt;

	/// <summary>Creates a runner over a discovery result, folding in its errors.</summary>
	public TestRunner(DiscoveryResult discovery, TestRunnerOptions? options = null)
		: this(discovery?.Tests ?? throw new ArgumentNullException(nameof(discovery)), options)
	{
		// Discovery errors are reported, not dropped. A malformed test that
		// vanishes leaves the suite green for the wrong reason.
		foreach (TestDiscoveryError error in discovery.Errors)
			results.Add(error.ToResult());
	}

	/// <summary>Creates a runner over an explicit list of tests.</summary>
	public TestRunner(IReadOnlyList<TestCase> tests, TestRunnerOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(tests);

		this.options = options ?? new TestRunnerOptions();

		queue = [.. tests.Where(this.options.Filter.Matches)];
		FilteredOut = tests.Count - queue.Count;
	}

	/// <summary>Where the runner has got to.</summary>
	public RunnerState State { get; private set; } = RunnerState.Idle;

	/// <summary>
	/// How many discovered tests the filter held back. Reported rather than
	/// silently dropped, so a narrowed run is never mistaken for a full one.
	/// </summary>
	public int FilteredOut { get; }

	/// <summary>The test currently executing, if any.</summary>
	public TestCase? Current => current;

	/// <summary>Results so far; complete once <see cref="State"/> is finished.</summary>
	public TestRunResult Result => TestRunResult.FromResults(options.RunName, results);

	/// <summary>
	/// Advances by one tick.
	/// </summary>
	/// <returns>True while there is more to do.</returns>
	public bool Step()
	{
		if (State == RunnerState.Finished)
			return false;

		State = RunnerState.Running;
		options.Arena?.Tick();

		if (current is null && !StartNext()) {
			State = RunnerState.Finished;
			return false;
		}

		if (context is ITickingContext ticking)
			ticking.Tick();

		if (coroutine is not null && !coroutine.Step())
			Finish();

		return State != RunnerState.Finished;
	}

	/// <summary>
	/// Runs every test to completion. Convenience for Tier 0 and Tier 1, where
	/// there is no real tick loop to borrow.
	/// </summary>
	public TestRunResult RunToCompletion(int maxTicks = 1_000_000)
	{
		for (int i = 0; i < maxTicks && State != RunnerState.Finished; i++)
			Step();

		return Result;
	}

	private bool StartNext()
	{
		while (next < queue.Count) {
			TestCase test = queue[next++];

			if (test.SkipReason is string reason) {
				results.Add(TestResult.Skip(test.ClassName, test.Name, reason));
				continue;
			}

			if (test.Tier > options.MaxTier) {
				results.Add(TestResult.Skip(test.ClassName, test.Name,
					$"Needs tier {test.Tier} but this environment supports up to {options.MaxTier}."));
				continue;
			}

			if (test.FreshWorld && !options.SupportsFreshWorld) {
				// Reported, never quietly run. A [FreshWorld] test turned loose
				// in whatever world happens to be loaded would pass while
				// proving nothing.
				results.Add(TestResult.Skip(test.ClassName, test.Name,
					"Declares [FreshWorld], which this runner cannot provide. Running it in the current world would report a pass without testing what it asked for."));
				continue;
			}

			if (Begin(test))
				return true;
		}

		return false;
	}

	private bool Begin(TestCase test)
	{
		current = test;
		startedAt = options.TimeProvider.GetTimestamp();

		if (test.Box is BoxRequest request) {
			if (options.Arena is null) {
				Complete(TestOutcome.Errored, "This test needs a box but the runner has no arena configured.");
				return false;
			}

			lease = options.Arena.TryLease(request);

			if (lease is null) {
				Complete(TestOutcome.Errored,
					"The arena had no room for this test's box. Every slot is leased or retained; " +
					"retained boxes from earlier failures are the usual cause.");
				return false;
			}
		}

		object? instance;
		object? body;

		try {
			instance = test.Method.IsStatic ? null : Instantiate(test.Method.DeclaringType!);
			body = test.Method.Invoke(instance, BuildArguments(test));
		}
		catch (Exception ex) {
			Complete(Classify(Unwrap(ex)), Describe(Unwrap(ex)), Unwrap(ex).StackTrace);
			return false;
		}

		if (test.BodyKind == TestBodyKind.Immediate) {
			Complete(TestOutcome.Passed, null);
			return false;
		}

		if (body is not IEnumerator enumerator) {
			Complete(TestOutcome.Errored, "A coroutine test returned null instead of an enumerator.");
			return false;
		}

		int timeout = test.TimeoutTicks > 0 ? test.TimeoutTicks : options.DefaultTimeoutTicks;
		coroutine = new TestCoroutine(enumerator, timeout);

		return true;
	}

	private object?[] BuildArguments(TestCase test)
	{
		if (!test.WantsContext)
			return [];

		if (options.CreateContext is null)
			throw new InvalidOperationException("This test asks for an ITestContext but the runner has no CreateContext configured.");

		context = options.CreateContext(lease);

		return [context];
	}

	private object? Instantiate(Type type)
		=> options.Activate is not null ? options.Activate(type) : Activator.CreateInstance(type);

	private void Finish()
	{
		TestCoroutine done = coroutine!;

		switch (done.State) {
			case CoroutineState.Completed:
				Complete(TestOutcome.Passed, null, ticks: done.ElapsedTicks);
				break;

			case CoroutineState.TimedOut:
				// A timeout is a failure rather than an error: the usual cause
				// is that the awaited condition never happened, which is the
				// subject misbehaving, not the test being malformed.
				Complete(TestOutcome.Failed, done.Exception?.Message, ticks: done.ElapsedTicks);
				break;

			default:
				Exception ex = done.Exception!;
				Complete(Classify(ex), Describe(ex), ex.StackTrace, done.ElapsedTicks);
				break;
		}
	}

	private void Complete(TestOutcome outcome, string? message, string? stackTrace = null, int? ticks = null)
	{
		TestCase test = current!;
		TimeSpan duration = options.TimeProvider.GetElapsedTime(startedAt);

		results.Add(new TestResult {
			ClassName = test.ClassName,
			Name = test.Name,
			Outcome = outcome,
			Message = message,
			StackTrace = stackTrace,
			Duration = duration,
			Ticks = ticks,
			Box = lease?.Interior.ToString(),
		});

		// Dispose before releasing the box, so a context that tears down what
		// the test spawned does so while the box is still its own.
		if (context is IDisposable disposable) {
			try {
				disposable.Dispose();
			}
			catch (Exception ex) {
				results[^1] = results[^1] with {
					Outcome = TestOutcome.Errored,
					Message = $"{results[^1].Message}\nTeardown then failed: {ex.GetType().Name}: {ex.Message}".TrimStart(),
				};
			}
		}

		if (lease is not null)
			options.Arena!.Release(lease, keepForInspection: options.KeepFailedBoxes && outcome is TestOutcome.Failed or TestOutcome.Errored);

		context = null;
		lease = null;
		coroutine = null;
		current = null;
	}

	private static Exception Unwrap(Exception ex)
		=> ex is TargetInvocationException { InnerException: Exception inner } ? inner : ex;

	private static TestOutcome Classify(Exception ex) => ex switch {
		SkipTestException => TestOutcome.Skipped,
		AssertionException => TestOutcome.Failed,
		_ => TestOutcome.Errored,
	};

	private static string Describe(Exception ex)
		=> ex is AssertionException or SkipTestException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
}
