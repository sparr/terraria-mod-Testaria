namespace Testaria.Tests;

/// <summary>
/// What the runner does with a seed: when it applies one, which one, and that
/// it always hands the generators back.
/// </summary>
public class RunnerSeedingTests
{
	private sealed class RecordingRandom : IRandomControl
	{
		public List<int> Seeded { get; } = [];

		public int Restores { get; private set; }

		public void Reseed(int seed) => Seeded.Add(seed);

		public void Restore() => Restores++;
	}

	private static DiscoveryResult DiscoverOne(string name)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);

		return new DiscoveryResult { Tests = [.. all.Tests.Where(t => t.Name == name)], Errors = [] };
	}

	private static (TestResult Result, RecordingRandom Random) Run(string name, int runSeed = 0)
	{
		var random = new RecordingRandom();
		DiscoveryResult one = DiscoverOne(name);
		XAssert.Single(one.Tests);

		TestRunResult run = new TestRunner(one, new TestRunnerOptions {
			MaxTier = TestTier.Loaded,
			Random = random,
			RunSeed = runSeed,
		}).RunToCompletion();

		return (run.Suites.Single().Results.Single(), random);
	}

	[Fact]
	public void A_test_is_seeded_from_its_own_identity()
	{
		(TestResult result, RecordingRandom random) = Run(nameof(Fixtures.Ordinary));

		int expected = TestSeed.For(0, typeof(Fixtures).FullName!, nameof(Fixtures.Ordinary));

		XAssert.Equal([expected], random.Seeded);
		XAssert.Equal(expected, result.Seed);
	}

	[Fact]
	public void The_run_seed_moves_it()
	{
		(TestResult result, _) = Run(nameof(Fixtures.Ordinary), runSeed: 99);

		XAssert.Equal(TestSeed.For(99, typeof(Fixtures).FullName!, nameof(Fixtures.Ordinary)), result.Seed);
	}

	[Fact]
	public void A_declared_seed_wins_over_the_derived_one()
	{
		(TestResult result, RecordingRandom random) = Run(nameof(Fixtures.Pinned));

		XAssert.Equal([4242], random.Seeded);
		XAssert.Equal(4242, result.Seed);
	}

	[Fact]
	public void A_declared_seed_ignores_the_run_seed()
		// Otherwise "the seed that reproduced the bug" would only reproduce it
		// at one run seed, which is not what the author asked for.
		=> XAssert.Equal(4242, Run(nameof(Fixtures.Pinned), runSeed: 99).Result.Seed);

	[Fact]
	public void The_generators_are_handed_back_when_a_test_passes()
		=> XAssert.Equal(1, Run(nameof(Fixtures.Ordinary)).Random.Restores);

	[Fact]
	public void The_generators_are_handed_back_when_a_test_throws()
	{
		(TestResult result, RecordingRandom random) = Run(nameof(Fixtures.Throws));

		XAssert.Equal(TestOutcome.Errored, result.Outcome);
		// The interesting half: a test that ends badly must not leave the game
		// pinned to its seed for everything that follows.
		XAssert.Equal(1, random.Restores);
	}

	[Fact]
	public void A_skipped_test_never_touches_the_generators()
	{
		(TestResult result, RecordingRandom random) = Run(nameof(Fixtures.Skipped));

		XAssert.Equal(TestOutcome.Skipped, result.Outcome);
		XAssert.Empty(random.Seeded);
		XAssert.Null(result.Seed);
	}

	[Fact]
	public void A_host_that_controls_no_generators_reports_no_seed()
	{
		DiscoveryResult one = DiscoverOne(nameof(Fixtures.Ordinary));

		TestRunResult run = new TestRunner(one, new TestRunnerOptions { MaxTier = TestTier.Loaded })
			.RunToCompletion();

		// Rather than reporting a seed nothing was ever seeded with, which
		// would promise a reproducibility the host cannot deliver.
		XAssert.Null(run.Suites.Single().Results.Single().Seed);
	}

	[Fact]
	public void The_seed_reaches_the_report()
	{
		(TestResult result, _) = Run(nameof(Fixtures.Ordinary));

		XAssert.Contains($"testaria-seed=\"{result.Seed}\"", JUnitXmlWriter.ToXml(new TestRunResult {
			Name = "seeded",
			Suites = [new TestSuiteResult { Name = "suite", Results = [result] }],
		}));
	}

	public class Fixtures
	{
		[LoadedTest]
		public void Ordinary() { }

		[LoadedTest]
		[Seed(4242)]
		public void Pinned() { }

		[LoadedTest]
		public void Throws() => throw new InvalidOperationException("deliberate");

		[LoadedTest(Skip = "not today")]
		public void Skipped() { }
	}
}
