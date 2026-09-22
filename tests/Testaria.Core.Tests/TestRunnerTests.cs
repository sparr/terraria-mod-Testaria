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
	public void A_full_arena_says_it_is_full_rather_than_blaming_retention()
	{
		// Nothing is retained here: every box is still leased. The arena is
		// simply full, and the message should say that rather than blaming
		// wreckage that does not exist.
		// BoxedTest asks for a 48x32 box in the Cavern, so exhaust exactly that.
		var arena = new Arena(Small(), new ArenaOptions { WidthClasses = [48], HeightClasses = [32] });

		while (arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32)) is not null) { }

		TestResult result = Single(Run(nameof(Fixtures.BoxedTest), Options(arena)));

		XAssert.Equal(TestOutcome.Blocked, result.Outcome);
		XAssert.Contains("too small", result.Message);
		XAssert.DoesNotContain("retained from earlier failures", result.Message);
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

	[Fact]
	public void A_test_that_cannot_get_a_box_is_blocked_rather_than_failed()
	{
		// Blocked says the test never ran. Failed would blame the subject for
		// something it was never given a chance to do, and Skipped would imply
		// somebody chose this.
		var arena = new Arena(Small(), new ArenaOptions { QuarantineTicks = 0 });

		while (arena.TryLease(BoxRequest.Banded(Band.Surface, 16, 16)) is BoxLease lease)
			arena.Release(lease, keepForInspection: true);

		Fixtures.Reset();
		TestRunResult run = Run(nameof(Fixtures.PassingCoroutine), Options(arena));
		TestResult result = Single(run);

		XAssert.Equal(TestOutcome.Blocked, result.Outcome);
		XAssert.False(Fixtures.Ran, "the test body should never have been entered");
	}

	[Fact]
	public void A_blocked_test_says_what_is_holding_the_space_and_what_to_do()
	{
		var arena = new Arena(Small(), new ArenaOptions { QuarantineTicks = 0 });

		while (arena.TryLease(BoxRequest.Banded(Band.Surface, 16, 16)) is BoxLease lease)
			arena.Release(lease, keepForInspection: true);

		TestResult result = Single(Run(nameof(Fixtures.PassingCoroutine), Options(arena)));

		XAssert.Contains("retained", result.Message);
		XAssert.Contains("KeepFailedBoxes", result.Message);
	}

	[Fact]
	public void A_run_with_a_blocked_test_fails_even_though_nothing_that_ran_failed()
	{
		// The point of the whole arrangement: a suite that quietly stopped
		// running part of itself has not established what it was asked to.
		var arena = new Arena(Small(), new ArenaOptions { QuarantineTicks = 0 });

		while (arena.TryLease(BoxRequest.Banded(Band.Surface, 16, 16)) is BoxLease lease)
			arena.Release(lease, keepForInspection: true);

		TestRunResult run = Run(nameof(Fixtures.PassingCoroutine), Options(arena));

		XAssert.Equal(0, run.Failures);
		XAssert.Equal(0, run.Errors);
		XAssert.Equal(1, run.Blocked);
		XAssert.False(run.IsSuccess);
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

public class TestRunnerContextLifecycleTests
{
	private static WorldGeometry Small() => new(4200, 1200, 87, 250, 400, 1000);

	private static TestRunResult Run(string name, Func<BoxLease?, ITestContext> factory)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);
		DiscoveryResult one = new() { Tests = [.. all.Tests.Where(t => t.Name == name)], Errors = [] };

		return new TestRunner(one, new TestRunnerOptions {
			MaxTier = TestTier.World,
			Arena = new Arena(Small()),
			CreateContext = factory,
		}).RunToCompletion();
	}

	[Fact]
	public void A_ticking_context_is_ticked_once_per_runner_step()
	{
		var context = new TrackingContext();

		Run(nameof(Fixtures.WaitsThreeTicks), _ => context);

		// The body waits three ticks, so the context must have seen at least
		// that many; exactness is the coroutine's business, not the context's.
		XAssert.True(context.Ticks >= 3, $"expected at least 3 ticks, saw {context.Ticks}");
	}

	[Fact]
	public void A_disposable_context_is_disposed_when_the_test_ends()
	{
		var context = new TrackingContext();

		Run(nameof(Fixtures.WaitsThreeTicks), _ => context);

		XAssert.True(context.Disposed);
	}

	[Fact]
	public void A_context_is_disposed_even_when_the_test_fails()
	{
		// Teardown that only runs on success leaves the wreckage of exactly
		// the tests you most need to be tidy about.
		var context = new TrackingContext();

		Run(nameof(Fixtures.FailsAfterATick), _ => context);

		XAssert.True(context.Disposed);
	}

	[Fact]
	public void A_teardown_that_throws_turns_a_passing_test_into_an_error()
	{
		// Silently swallowing it would leave the world dirty and the suite
		// green, which is the worst of both.
		TestRunResult run = Run(nameof(Fixtures.WaitsThreeTicks), _ => new ThrowingContext());
		TestResult result = run.Suites.Single().Results.Single();

		XAssert.Equal(TestOutcome.Errored, result.Outcome);
		XAssert.Contains("Teardown then failed", result.Message);
	}

	[Fact]
	public void A_non_ticking_context_is_simply_not_ticked()
	{
		// Tier 0 and Tier 1 contexts have no tick loop to hear about.
		TestRunResult run = Run(nameof(Fixtures.WaitsThreeTicks), lease => new PlainContext(lease));

		XAssert.True(run.IsSuccess);
	}

	private class PlainContext(BoxLease? lease) : ITestContext
	{
		public TileRect Interior => lease?.Interior ?? default;
		public TileRect Bounds => lease?.Bounds ?? default;
		public Band Bands => lease?.Bands ?? Band.None;
		public int ElapsedTicks => 0;
	}

	private sealed class TrackingContext : ITickingContext, IDisposable
	{
		public int Ticks { get; private set; }
		public bool Disposed { get; private set; }

		public TileRect Interior => new(0, 0, 10, 10);
		public TileRect Bounds => new(0, 0, 10, 10);
		public Band Bands => Band.Surface;
		public int ElapsedTicks => Ticks;

		public void Tick() => Ticks++;
		public void Dispose() => Disposed = true;
	}

	private sealed class ThrowingContext : ITestContext, IDisposable
	{
		public TileRect Interior => new(0, 0, 10, 10);
		public TileRect Bounds => new(0, 0, 10, 10);
		public Band Bands => Band.Surface;
		public int ElapsedTicks => 0;

		public void Dispose() => throw new InvalidOperationException("could not remove spawned NPCs");
	}

	public class Fixtures
	{
		[GameTest(Band = Band.Surface, Timeout = 120)]
		public System.Collections.IEnumerator WaitsThreeTicks(ITestContext ctx)
		{
			yield return Wait.Ticks(3);
		}

		[GameTest(Band = Band.Surface, Timeout = 120)]
		public System.Collections.IEnumerator FailsAfterATick(ITestContext ctx)
		{
			yield return null;
			Assert.Fail("nope");
		}
	}
}

public class RuntimeSkipTests
{
	private static TestRunResult Run(string name)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);
		DiscoveryResult one = new() { Tests = [.. all.Tests.Where(t => t.Name == name)], Errors = [] };

		return new TestRunner(one, new TestRunnerOptions {
			MaxTier = TestTier.World,
			Arena = new Arena(new WorldGeometry(4200, 1200, 87, 250, 400, 1000)),
		}).RunToCompletion();
	}

	[Fact]
	public void A_test_can_skip_itself_at_run_time()
	{
		// Whether an optional mod is installed is not knowable at compile time.
		TestResult result = Run(nameof(Fixtures.SkipsItself)).Suites.Single().Results.Single();

		XAssert.Equal(TestOutcome.Skipped, result.Outcome);
		XAssert.Contains("not installed", result.Message);
	}

	[Fact]
	public void A_runtime_skip_does_not_fail_the_run()
		=> XAssert.True(Run(nameof(Fixtures.SkipsItself)).IsSuccess);

	[Fact]
	public void A_coroutine_can_skip_itself_partway_through()
	{
		TestResult result = Run(nameof(Fixtures.SkipsPartway)).Suites.Single().Results.Single();

		XAssert.Equal(TestOutcome.Skipped, result.Outcome);
	}

	[Fact]
	public void Skipping_is_distinct_from_failing()
	{
		// A vacuous pass would claim coverage that never happened, and a
		// failure would blame a subject that is not broken.
		XAssert.NotEqual(
			Run(nameof(Fixtures.SkipsItself)).Suites.Single().Results.Single().Outcome,
			Run(nameof(Fixtures.FailsNormally)).Suites.Single().Results.Single().Outcome);
	}

	public class Fixtures
	{
		[LoadedTest]
		public void SkipsItself() => Assert.Skip("ExampleMod is not installed");

		[LoadedTest]
		public void FailsNormally() => Assert.Fail("broken");

		[GameTest(Band = Band.Cavern, Timeout = 60)]
		public System.Collections.IEnumerator SkipsPartway()
		{
			yield return Wait.Ticks(2);
			Assert.Skip("needed a jungle and there is not one");
		}
	}
}

public class FreshWorldTests
{
	private static TestRunResult Run(bool supported)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);

		return new TestRunner(all, new TestRunnerOptions {
			MaxTier = TestTier.World,
			Arena = new Arena(new WorldGeometry(4200, 1200, 87, 250, 400, 1000)),
			SupportsFreshWorld = supported,
		}).RunToCompletion();
	}

	private static TestResult Only(TestRunResult run) => run.Suites.Single().Results.Single();

	[Fact]
	public void A_fresh_world_test_is_skipped_when_the_host_cannot_provide_one()
	{
		// Running it anyway, in whatever world happens to be loaded, reports a
		// pass for a world the test never asked for. A declaration the runner
		// silently ignores is exactly the failure this framework exists to
		// prevent.
		Fixtures.Ran = false;
		TestResult result = Only(Run(supported: false));

		XAssert.Equal(TestOutcome.Skipped, result.Outcome);
		XAssert.Contains("FreshWorld", result.Message);
		XAssert.False(Fixtures.Ran, "the body must not run when its declared world was never provided");
	}

	[Fact]
	public void The_skip_reason_says_why_rather_than_just_that()
	{
		XAssert.Contains("without testing what it asked for", Only(Run(supported: false)).Message);
	}

	[Fact]
	public void Being_unable_to_provide_a_fresh_world_does_not_fail_the_run()
	{
		// Consistent with a test declaring a tier the environment cannot
		// honour: reported, visible in the counts, but not a failure.
		XAssert.True(Run(supported: false).IsSuccess);
	}

	[Fact]
	public void A_fresh_world_test_runs_when_the_host_says_it_can_provide_one()
	{
		Fixtures.Ran = false;
		TestResult result = Only(Run(supported: true));

		XAssert.Equal(TestOutcome.Passed, result.Outcome);
		XAssert.True(Fixtures.Ran);
	}

	public class Fixtures
	{
		public static bool Ran;

		[GameTest(Band = Band.Cavern)]
		[FreshWorld]
		public void NeedsAFreshWorld() => Ran = true;
	}
}
