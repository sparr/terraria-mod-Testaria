using System.Collections;

namespace Testaria.Tests;

/// <summary>
/// That the runner actually applies each test's pacing markers, and always
/// hands the next test a thawed world.
/// </summary>
public class RunnerPacingTests
{
	private static WorldGeometry Small() => new(4200, 1200, 87, 250, 400, 1000);

	private static TestRunner RunnerFor(string name, RunPacing pacing)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);
		var one = new DiscoveryResult {
			Tests = [.. all.Tests.Where(t => t.Name == name)],
			Errors = [],
		};

		XAssert.Single(one.Tests);

		return new TestRunner(one, new TestRunnerOptions {
			MaxTier = TestTier.World,
			Arena = new Arena(Small()),
			Pacing = pacing,
			CreateContext = _ => new Bare(),
		});
	}

	[Fact]
	public void A_StartPaused_test_freezes_the_world_as_it_begins()
	{
		var pacing = new RunPacing();
		TestRunner runner = RunnerFor(nameof(Fixtures.Frozen), pacing);

		runner.Step();

		XAssert.True(pacing.IsFrozen);
	}

	[Fact]
	public void An_ordinary_test_leaves_the_world_running()
	{
		var pacing = new RunPacing();
		TestRunner runner = RunnerFor(nameof(Fixtures.Ordinary), pacing);

		runner.Step();

		XAssert.False(pacing.IsFrozen);
	}

	[Fact]
	public void A_RealTime_test_opts_out_while_it_runs()
	{
		var pacing = new RunPacing();
		pacing.SetMode(PacingMode.Unbounded);
		TestRunner runner = RunnerFor(nameof(Fixtures.Slow), pacing);

		runner.Step();

		XAssert.True(pacing.CurrentTestWantsRealtime);
		XAssert.Equal(PacingMode.Realtime, pacing.EffectiveMode);
	}

	[Fact]
	public void A_test_that_pauses_and_never_resumes_does_not_strand_the_run()
	{
		var pacing = new RunPacing();
		TestRunner runner = RunnerFor(nameof(Fixtures.Frozen), pacing);

		runner.RunToCompletion();

		// However that test ended, the next one has to be able to tick.
		XAssert.False(pacing.IsFrozen);
		XAssert.False(pacing.CurrentTestWantsRealtime);
	}

	[Fact]
	public void The_runs_own_pacing_survives_a_test_that_opted_out()
	{
		var pacing = new RunPacing();
		pacing.SetMode(PacingMode.Bounded, 240);
		TestRunner runner = RunnerFor(nameof(Fixtures.Slow), pacing);

		runner.RunToCompletion();

		XAssert.Equal(PacingMode.Bounded, pacing.Mode);
		XAssert.Equal(240, pacing.TargetTicksPerSecond);
	}

	/// <summary>A context with no world behind it; these tests only watch the pacing.</summary>
	private sealed class Bare : ITestContext
	{
		public TileRect Interior => default;

		public TileRect Bounds => default;

		public Band Bands => Band.None;

		public int ElapsedTicks => 0;
	}

	private class Fixtures
	{
		[GameTest(Band = Band.Cavern)]
		public void Ordinary() { }

		// A coroutine, because an immediate test finishes inside the same step
		// that starts it, and the freeze is released the moment it completes.
		[GameTest(Band = Band.Cavern)]
		[StartPaused]
		public IEnumerator Frozen()
		{
			yield return Wait.Ticks(1);
		}

		[GameTest(Band = Band.Cavern)]
		[RealTime]
		public IEnumerator Slow()
		{
			yield return Wait.Ticks(2);
		}
	}
}
