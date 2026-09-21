using System.Collections;

namespace Testaria.Tests;

public class TestRunnerTests
{
	private static WorldGeometry Small() => new(4200, 1200, 87, 250, 400, 1000);

	private static DiscoveryResult DiscoverOne(string name)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);

		return new DiscoveryResult {
			Tests = [.. all.Tests.Where(t => t.Name == name)],
			Errors = [],
		};
	}

	private static TestRunResult Run(string name, TestRunnerOptions? options = null)
	{
		DiscoveryResult one = DiscoverOne(name);
		XAssert.Single(one.Tests);

		return new TestRunner(one, options ?? Options()).RunToCompletion();
	}

	/// <summary>Options with an arena, since every GameTest fixture asks for a box.</summary>
	private static TestRunnerOptions Options(Arena? arena = null) => new() {
		MaxTier = TestTier.World,
		Arena = arena ?? new Arena(Small()),
		CreateContext = lease => new StubContext(lease),
	};

	/// <summary>Options with no arena at all, for the tests that check that path.</summary>
	private static TestRunnerOptions OptionsWithoutArena() => new() {
		MaxTier = TestTier.World,
		CreateContext = lease => new StubContext(lease),
	};

	private static TestResult Single(TestRunResult run) => run.Suites.Single().Results.Single();

	[Fact]
	public void An_empty_run_finishes_immediately_and_succeeds()
	{
		var runner = new TestRunner([], Options());

		XAssert.False(runner.Step());
		XAssert.Equal(RunnerState.Finished, runner.State);
		XAssert.True(runner.Result.IsSuccess);
	}

	[Fact]
	public void A_passing_immediate_test_is_recorded_as_passed()
	{
		Fixtures.Reset();
		TestResult result = Single(Run(nameof(Fixtures.PassingImmediate)));

		XAssert.Equal(TestOutcome.Passed, result.Outcome);
		XAssert.True(Fixtures.Ran);
		XAssert.Null(result.Ticks);
	}

	[Fact]
	public void A_static_test_method_runs_without_an_instance()
	{
		Fixtures.Reset();

		XAssert.Equal(TestOutcome.Passed, Single(Run(nameof(Fixtures.StaticPassing))).Outcome);
		XAssert.True(Fixtures.Ran);
	}

	[Fact]
	public void An_assertion_failure_is_a_failure_and_keeps_its_message()
	{
		TestResult result = Single(Run(nameof(Fixtures.FailingAssertion)));

		XAssert.Equal(TestOutcome.Failed, result.Outcome);
		XAssert.Contains("zombie", result.Message);
	}

	[Fact]
	public void A_thrown_exception_is_an_error_and_is_unwrapped_from_reflection()
	{
		// Invoking by reflection wraps everything in TargetInvocationException;
		// reporting that instead of the real cause would be useless.
		TestResult result = Single(Run(nameof(Fixtures.ThrowingImmediate)));

		XAssert.Equal(TestOutcome.Errored, result.Outcome);
		XAssert.Contains("InvalidOperationException", result.Message);
		XAssert.DoesNotContain("TargetInvocation", result.Message);
	}

	[Fact]
	public void A_skipped_test_is_reported_and_its_body_never_runs()
	{
		Fixtures.Reset();
		TestResult result = Single(Run(nameof(Fixtures.SkippedTest)));

		XAssert.Equal(TestOutcome.Skipped, result.Outcome);
		XAssert.Equal("not ready", result.Message);
		XAssert.False(Fixtures.Ran);
	}

	[Fact]
	public void A_test_above_the_environment_tier_is_skipped_rather_than_run()
	{
		// The Tier 0 boundary made operational. Running a Tier 2 test in a bare
		// host would not fail cleanly, it would run against default
		// initialized statics and might well pass.
		Fixtures.Reset();
		TestRunResult run = Run(nameof(Fixtures.PassingCoroutine), new TestRunnerOptions { MaxTier = TestTier.Unit });
		TestResult result = Single(run);

		XAssert.Equal(TestOutcome.Skipped, result.Outcome);
		XAssert.Contains("World", result.Message);
		XAssert.Contains("Unit", result.Message);
		XAssert.False(Fixtures.Ran);
	}

	[Fact]
	public void A_skipped_run_still_counts_as_success()
	{
		TestRunResult run = Run(nameof(Fixtures.PassingCoroutine), new TestRunnerOptions { MaxTier = TestTier.Unit });

		XAssert.True(run.IsSuccess);
	}

	[Fact]
	public void Discovery_errors_are_folded_into_the_results()
	{
		var discovery = new DiscoveryResult {
			Tests = [],
			Errors = [new TestDiscoveryError { Location = "Some.Type.BadTest", Message = "malformed" }],
		};

		TestRunResult run = new TestRunner(discovery, Options()).RunToCompletion();

		XAssert.Equal(1, run.Errors);
		XAssert.False(run.IsSuccess);
	}

	[Fact]
	public void A_coroutine_test_spans_ticks_and_records_them()
	{
		Fixtures.Reset();
		TestResult result = Single(Run(nameof(Fixtures.PassingCoroutine)));

		XAssert.Equal(TestOutcome.Passed, result.Outcome);
		XAssert.NotNull(result.Ticks);
		XAssert.True(result.Ticks > 1, $"expected several ticks, got {result.Ticks}");
	}

	[Fact]
	public void The_runner_does_not_finish_while_a_coroutine_is_still_going()
	{
		var runner = new TestRunner(DiscoverOne(nameof(Fixtures.PassingCoroutine)), Options());

		XAssert.True(runner.Step());
		XAssert.Equal(RunnerState.Running, runner.State);
		XAssert.NotNull(runner.Current);
	}

	[Fact]
	public void A_coroutine_that_overruns_its_budget_is_a_failure_not_an_error()
	{
		// The usual cause is that the awaited condition never happened, which
		// is the subject misbehaving rather than the test being malformed.
		TestResult result = Single(Run(nameof(Fixtures.HangingCoroutine)));

		XAssert.Equal(TestOutcome.Failed, result.Outcome);
		XAssert.Contains("budget", result.Message);
	}

	[Fact]
	public void An_assertion_failure_inside_a_coroutine_is_still_a_failure()
	{
		TestResult result = Single(Run(nameof(Fixtures.FailingCoroutine)));

		XAssert.Equal(TestOutcome.Failed, result.Outcome);
		XAssert.Contains("later", result.Message);
	}

	[Fact]
	public void A_boxed_test_leases_a_box_and_records_its_coordinates()
	{
		var arena = new Arena(Small());
		TestResult result = Single(Run(nameof(Fixtures.BoxedTest), Options(arena)));

		XAssert.Equal(TestOutcome.Passed, result.Outcome);
		XAssert.NotNull(result.Box);
		XAssert.Equal(1, arena.Stats.SlotsCarved);
	}

	[Fact]
	public void A_passing_boxed_test_returns_its_box_to_quarantine()
	{
		var arena = new Arena(Small(), new ArenaOptions { QuarantineTicks = 60 });
		Run(nameof(Fixtures.BoxedTest), Options(arena));

		XAssert.Equal(0, arena.Stats.Active);
		XAssert.Equal(0, arena.Stats.Retained);
	}

	[Fact]
	public void A_failing_boxed_test_keeps_its_box_for_inspection()
	{
		var arena = new Arena(Small());
		var options = Options(arena) with { KeepFailedBoxes = true };

		Run(nameof(Fixtures.FailingBoxedTest), options);

		XAssert.Equal(1, arena.Stats.Retained);
	}

	[Fact]
	public void Keeping_failed_boxes_can_be_turned_off()
	{
		var arena = new Arena(Small());
		var options = Options(arena) with { KeepFailedBoxes = false };

		Run(nameof(Fixtures.FailingBoxedTest), options);

		XAssert.Equal(0, arena.Stats.Retained);
	}

	[Fact]
	public void A_boxed_test_with_no_arena_is_an_error_rather_than_running_nowhere()
	{
		TestResult result = Single(Run(nameof(Fixtures.BoxedTest), OptionsWithoutArena()));

		XAssert.Equal(TestOutcome.Errored, result.Outcome);
		XAssert.Contains("no arena", result.Message);
	}

	[Fact]
	public void Arena_exhaustion_is_reported_with_a_message_naming_the_likely_cause()
	{
		// Retained boxes from earlier failures are the usual way a sequential
		// run runs out of room, so the message says so.
		// BoxedTest asks for a 48x32 box in the Cavern, so exhaust exactly that.
		var arena = new Arena(Small(), new ArenaOptions { WidthClasses = [48], HeightClasses = [32] });

		while (arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32)) is not null) { }

		TestResult result = Single(Run(nameof(Fixtures.BoxedTest), Options(arena)));

		XAssert.Equal(TestOutcome.Errored, result.Outcome);
		XAssert.Contains("retained", result.Message);
	}

	[Fact]
	public void A_test_asking_for_a_context_receives_one_describing_its_box()
	{
		Fixtures.Reset();
		var arena = new Arena(Small());

		XAssert.Equal(TestOutcome.Passed, Single(Run(nameof(Fixtures.ContextTest), Options(arena))).Outcome);
		XAssert.NotNull(Fixtures.SeenContext);
		XAssert.False(Fixtures.SeenContext!.Interior.IsEmpty);
	}

	[Fact]
	public void A_context_test_with_no_factory_configured_is_an_error()
	{
		var options = new TestRunnerOptions { MaxTier = TestTier.World, Arena = new Arena(Small()) };
		TestResult result = Single(Run(nameof(Fixtures.ContextTest), options));

		XAssert.Equal(TestOutcome.Errored, result.Outcome);
		XAssert.Contains("CreateContext", result.Message);
	}

	[Fact]
	public void Tests_run_in_order_and_every_one_is_reported()
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);
		TestRunResult run = new TestRunner(all, Options(new Arena(Small()))).RunToCompletion();

		XAssert.Equal(all.Tests.Count, run.Total);
	}

	[Fact]
	public void The_arena_is_ticked_so_quarantine_advances_during_a_run()
	{
		var arena = new Arena(Small(), new ArenaOptions { QuarantineTicks = 2 });
		DiscoveryResult two = new() {
			Tests = [.. TestDiscovery.Discover([typeof(Fixtures)]).Tests.Where(t => t.Name == nameof(Fixtures.BoxedTest))],
			Errors = [],
		};

		// The same single test, run repeatedly, must recycle one slot rather
		// than carving a new one each time.
		for (int i = 0; i < 20; i++)
			new TestRunner(two.Tests, Options(arena)).RunToCompletion();

		XAssert.Equal(1, arena.Stats.SlotsCarved);
	}

	[Fact]
	public void Durations_come_from_the_injected_clock()
	{
		var clock = new FakeTime();
		var options = Options() with { TimeProvider = clock };
		DiscoveryResult one = DiscoverOne(nameof(Fixtures.PassingImmediate));
		var runner = new TestRunner(one, options);

		clock.Advance(TimeSpan.FromMilliseconds(250));
		runner.RunToCompletion();

		XAssert.True(Single(runner.Result).Duration >= TimeSpan.Zero);
	}

	// ---- fixtures ----

	private sealed class StubContext(BoxLease? lease) : ITestContext
	{
		public TileRect Interior => lease?.Interior ?? default;
		public TileRect Bounds => lease?.Bounds ?? default;
		public Band Bands => lease?.Bands ?? Band.None;
		public int ElapsedTicks => 0;
	}

	private sealed class FakeTime : TimeProvider
	{
		private long now;

		public override long GetTimestamp() => now;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public void Advance(TimeSpan by) => now += by.Ticks;
	}

	public class Fixtures
	{
		public static bool Ran;
		public static ITestContext? SeenContext;

		public static void Reset()
		{
			Ran = false;
			SeenContext = null;
		}

		[LoadedTest]
		public void PassingImmediate() => Ran = true;

		[LoadedTest]
		public static void StaticPassing() => Ran = true;

		[LoadedTest]
		public void FailingAssertion() => Assert.Fail("zombie should be dead");

		[LoadedTest]
		public void ThrowingImmediate() => throw new InvalidOperationException("broken");

		[LoadedTest(Skip = "not ready")]
		public void SkippedTest() => Ran = true;

		[GameTest(Band = Band.Surface, Timeout = 120)]
		public IEnumerator PassingCoroutine()
		{
			Ran = true;
			yield return Wait.Ticks(3);
		}

		[GameTest(Band = Band.Surface, Timeout = 10)]
		public IEnumerator HangingCoroutine()
		{
			while (true)
				yield return null;
		}

		[GameTest(Band = Band.Surface, Timeout = 120)]
		public IEnumerator FailingCoroutine()
		{
			yield return Wait.Ticks(2);
			Assert.Fail("failed later");
		}

		[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 60)]
		public IEnumerator BoxedTest() { yield break; }

		[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 60)]
		public IEnumerator FailingBoxedTest()
		{
			yield return null;
			Assert.Fail("nope");
		}

		[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 60)]
		public IEnumerator ContextTest(ITestContext ctx)
		{
			SeenContext = ctx;
			yield break;
		}
	}
}
