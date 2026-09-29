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

	// One invoker per method, built on first use. Held by the runner rather
	// than statically: a compiled invoker closes over a MethodInfo belonging to
	// a test mod's assembly, and a static cache would keep that assembly alive
	// across a reload. A null value records a signature that could not be
	// bound, so it is tried once rather than on every case.
	private readonly Dictionary<MethodInfo, Func<object?, object?[], object?>?> invokers = [];
	private int next;

	private TestCase? current;
	private TestCoroutine? coroutine;
	private BoxLease? lease;
	private ITestContext? context;
	private long startedAt;

	// How many assertions had been made when this test began. A test that ends
	// with the number unchanged has passed without checking anything.
	private int assertionsAtStart;
	private int? seed;

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

	/// <summary>
	/// The context handed to the running test, if it asked for one.
	/// <para/>
	/// Exposed because watching a box for intruders has to happen earlier in
	/// the game's tick than the runner itself runs, and the watcher needs
	/// something to watch.
	/// </summary>
	public ITestContext? CurrentContext => context;

	/// <summary>
	/// The running test's full name, or null between tests.
	/// <para/>
	/// For measurement and diagnostics that happen outside the runner but
	/// belong to a test, such as watching a box after its tenant has gone.
	/// </summary>
	public string? CurrentTestName => current is { } test ? $"{test.ClassName}.{test.Name}" : null;

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

			// Tier on every one of these, not just on the results that ran.
			// A tier 3 test skipped for want of a client is precisely the
			// result somebody wants to find by tier, and leaving it off made
			// the tier 3 gate unable to see the tests it exists to check.
			if (test.SkipReason is string reason) {
				results.Add(TestResult.Skip(test.ClassName, test.Name, reason) with { Tier = test.Tier });
				continue;
			}

			if (test.Tier > options.MaxTier) {
				results.Add(TestResult.Skip(test.ClassName, test.Name,
					$"Needs tier {test.Tier} but this environment supports up to {options.MaxTier}.") with { Tier = test.Tier });
				continue;
			}

			// After the tier gate and before the world one, because a test
			// naming mods it cannot have is answered by the mod list whatever
			// the world looks like.
			if (test.UnmetModRequirement(options.LoadedMods) is string unmet) {
				results.Add(TestResult.Skip(test.ClassName, test.Name, unmet) with { Tier = test.Tier });
				continue;
			}

			if (test.NeedsOwnWorld(options.IsolateMutatingTests) && !options.SupportsFreshWorld) {
				// Reported, never quietly run. A [FreshWorld] test turned loose
				// in whatever world happens to be loaded would pass while
				// proving nothing.
				//
				// A [MutatesGlobalState] test reaches here only when the run
				// asked for it to be isolated, so the same rule applies: the run
				// asked for something the host cannot give, and running anyway
				// would answer a question nobody asked. Turning the isolation
				// off is the fix, and the message says which switch did it.
				results.Add(TestResult.Skip(test.ClassName, test.Name,
					test.FreshWorld
						? "Declares [FreshWorld], which this runner cannot provide. Running it in the current world would report a pass without testing what it asked for."
						: "Declares [MutatesGlobalState] and this run asked for such tests to be isolated, which this runner cannot provide. Either give the run a world of its own or stop asking for the isolation.") with { Tier = test.Tier });
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
		assertionsAtStart = Assert.Invocations;
		seed = null;

		// Before the body is invoked, because an immediate test's entire life
		// happens inside that call. Derived from the test's own identity, so
		// running one test out of a suite of a thousand gives it the seed it
		// would have had in the full run, which is what makes a reported
		// failure reproducible by rerunning just that test.
		if (options.Random is IRandomControl random) {
			seed = test.Seed ?? TestSeed.For(options.RunSeed, test.ClassName, test.Name);
			random.Reseed(seed.Value);
		}

		// Each test starts from the run's own pacing, whatever the last one
		// left behind. A test that pauses and then throws must not freeze the
		// rest of the suite.
		if (options.Pacing is RunPacing pacing) {
			pacing.ResetForNextTest();
			pacing.CurrentTestWantsRealtime = test.RealTime;

			if (test.StartPaused)
				pacing.Pause();
		}

		if (test.Box is BoxRequest request) {
			if (options.Arena is null) {
				Complete(TestOutcome.Errored, "This test needs a box but the runner has no arena configured.");
				return false;
			}

			try {
				lease = options.Arena.TryLease(request);
			}
			catch (Exception ex) {
				// A request the world cannot satisfy at all, such as a box
				// taller than the band it named, rather than an arena that is
				// merely full. The arena says exactly what is wrong, so the
				// message is passed through rather than replaced.
				//
				// Caught here because the alternative is much worse than it
				// looks: measured, an exception thrown out of a test hook is
				// silently swallowed by tModLoader, so the test never
				// completes, the session never advances, and the run hangs
				// until the harness gives up ten minutes later. A clear
				// message inside an exception nobody sees is no message at all
				// (PLAN.md section 8.4).
				Complete(TestOutcome.Errored, $"This test's box could not be leased. {ex.Message}");

				return false;
			}

			if (lease is null) {
				Complete(TestOutcome.Blocked, DescribeExhaustion(options.Arena));
				return false;
			}
		}

		// Eagerly, and not just when the test declares the parameter. A boxed
		// test that never asks for its context still occupies a box, and the
		// watching that catches escapes and contamination lives on the context.
		// Creating it only on request would silently exempt those tests from
		// both.
		if (lease is not null && options.CreateContext is not null)
			context = options.CreateContext(lease);

		object? instance;
		object? body;

		try {
			instance = test.Method.IsStatic ? null : Instantiate(test.Method.DeclaringType!);
			body = Call(test, instance);
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

	/// <summary>
	/// The context first, if the test wants one, then the case's arguments in
	/// declaration order.
	/// </summary>
	private object?[] BuildArguments(TestCase test)
	{
		if (!test.WantsContext)
			return [.. test.Arguments];

		if (options.CreateContext is null)
			throw new InvalidOperationException("This test asks for an ITestContext but the runner has no CreateContext configured.");

		// Already made in Begin for a boxed test; a boxless one makes it here.
		context ??= options.CreateContext(lease);

		object?[] arguments = new object?[test.Arguments.Count + 1];
		arguments[0] = context;

		for (int i = 0; i < test.Arguments.Count; i++)
			arguments[i + 1] = test.Arguments[i];

		return arguments;
	}

	/// <summary>
	/// Why there was no box, in terms of what the reader can do about it.
	/// <para/>
	/// Retained boxes are the usual cause and the message says so plainly,
	/// because the remedy is a decision only the author can make: go and look
	/// at the wreckage, or give up the evidence and rerun. Nothing here
	/// recycles a retained box to keep going, since that would destroy the
	/// very thing retention was for.
	/// </summary>
	private static string DescribeExhaustion(Arena arena)
	{
		ArenaStats stats = arena.Stats;

		if (stats.Retained == 0) {
			return "This test needs a box and the arena has none left: " +
				$"{stats.Active} of {stats.SlotsCarved} slots are in use and {stats.Quarantined} are cooling down, " +
				"with no room to carve another. The world is too small for this suite.";
		}

		return "This test never ran: the arena had no box for it. " +
			$"{stats.Retained} of {stats.SlotsCarved} slots are retained from earlier failures and are never reused, " +
			"so that the state a failing test left behind survives for you to go and look at. " +
			"Inspect them, then rerun. Set KeepFailedBoxes to false to give that ground up instead.";
	}

	/// <summary>
	/// Calls the test's method, through <see cref="TestInvoker"/> where the
	/// signature allows and reflection where it does not.
	/// <para/>
	/// The difference is what reaches the game's log rather than what reaches
	/// the caller: <see cref="TestInvoker"/> explains it.
	/// </summary>
	private object? Call(TestCase test, object? instance)
	{
		object?[] arguments = BuildArguments(test);

		if (!invokers.TryGetValue(test.Method, out Func<object?, object?[], object?>? invoker))
			invokers[test.Method] = invoker = TestInvoker.For(test.Method);

		return invoker is not null
			? invoker(instance, arguments)
			: test.Method.Invoke(instance, arguments);
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
		// Unfreeze before anything else. However this test ended, the world
		// has to be moving again for the next one to get its ticks.
		options.Pacing?.ResetForNextTest();

		// Likewise the generators: a seeded one left in place would make
		// everything after the run deterministic in a way nobody asked for,
		// including the next world the process generates.
		options.Random?.Restore();

		TestCase test = current!;
		TimeSpan duration = options.TimeProvider.GetElapsedTime(startedAt);

		List<string> collected = context is ITestNotes noted ? [.. noted.Notes] : [];

		// A test that passed without a single assertion reaching it has
		// established nothing, and looks exactly like one that established
		// everything. Not an error, because "this does not throw" is a real
		// thing to test and needs no assertion to say so, but worth saying out
		// loud: two tests written against this framework passed this way in
		// one afternoon, both looping over a collection that was always empty.
		if (outcome == TestOutcome.Passed && Assert.Invocations == assertionsAtStart)
			collected.Add("This test passed without making a single assertion, so it proved nothing. "
				+ "If that is deliberate, say so with an assertion that states what is being relied on.");

		string? notes = collected.Count > 0 ? string.Join("\n", collected) : null;

		results.Add(new TestResult {
			ClassName = test.ClassName,
			Name = test.Name,
			Outcome = outcome,
			Tier = test.Tier,
			Message = message,
			StackTrace = stackTrace,
			Output = notes,
			Duration = duration,
			Ticks = ticks,
			Box = lease?.Interior.ToString(),
			Seed = seed,
		});

		// A test whose box was not its own did not really run, whatever its
		// assertions concluded, so the result is downgraded before anyone
		// reads it. Only a result that would otherwise stand is downgraded: a
		// genuine failure is more informative than a note about the weather.
		if (outcome is TestOutcome.Passed
			&& context is IContaminationAware aware
			&& aware.Contamination.Count > 0) {
			results[^1] = results[^1] with {
				Outcome = TestOutcome.Errored,
				Message = "The box was contaminated, so this result cannot be trusted: "
					+ string.Join("; ", aware.Contamination),
			};
		}

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
