namespace Testaria.Tests;

public class BoxWatchTests
{
	private static readonly TileRect Box = new(100, 200, 80, 48);

	private static (int, WorldPoint) At(int id, int offsetX, int offsetY)
		=> (id, BoxSpace.At(Box, offsetX, offsetY));

	private static (int, WorldPoint) Outside(int id)
		=> (id, TileCoordinates.CenterOfTile(500, 900));

	[Fact]
	public void An_unowned_entity_inside_the_box_is_an_intruder()
	{
		var intruders = BoxWatch.FindIntruders(Box, [At(7, 4, 4)], new HashSet<int>()).ToList();

		XAssert.Equal([7], intruders);
	}

	[Fact]
	public void An_owned_entity_inside_the_box_is_not_an_intruder()
		=> XAssert.Empty(BoxWatch.FindIntruders(Box, [At(7, 4, 4)], new HashSet<int> { 7 }));

	[Fact]
	public void An_unowned_entity_outside_the_box_is_nobody_s_business()
		=> XAssert.Empty(BoxWatch.FindIntruders(Box, [Outside(7)], new HashSet<int>()));

	[Fact]
	public void An_owned_entity_outside_the_box_has_escaped()
	{
		var escapees = BoxWatch.FindEscapees(Box, [Outside(7)], new HashSet<int> { 7 }).ToList();

		XAssert.Equal([7], escapees);
	}

	[Fact]
	public void An_owned_entity_still_inside_has_not_escaped()
		=> XAssert.Empty(BoxWatch.FindEscapees(Box, [At(7, 4, 4)], new HashSet<int> { 7 }));

	[Fact]
	public void An_unowned_entity_outside_is_not_an_escapee_either()
		=> XAssert.Empty(BoxWatch.FindEscapees(Box, [Outside(7)], new HashSet<int>()));

	[Fact]
	public void The_two_checks_partition_the_problem()
	{
		// Every entity is exactly one of: mine and home, mine and escaped,
		// theirs and intruding, theirs and elsewhere.
		(int, WorldPoint)[] entities = [At(1, 2, 2), Outside(2), At(3, 5, 5), Outside(4)];
		var owned = new HashSet<int> { 1, 2 };

		XAssert.Equal([3], BoxWatch.FindIntruders(Box, entities, owned));
		XAssert.Equal([2], BoxWatch.FindEscapees(Box, entities, owned));
	}

	[Fact]
	public void The_box_edge_counts_as_inside()
	{
		XAssert.Equal([9], BoxWatch.FindIntruders(Box, [At(9, 0, 0)], new HashSet<int>()));
		XAssert.Equal([9], BoxWatch.FindIntruders(Box, [At(9, Box.Width - 1, Box.Height - 1)], new HashSet<int>()));
	}

	[Fact]
	public void Nothing_at_all_means_nothing_to_report()
	{
		XAssert.Empty(BoxWatch.FindIntruders(Box, [], new HashSet<int>()));
		XAssert.Empty(BoxWatch.FindEscapees(Box, [], new HashSet<int> { 1 }));
	}
}

public class ContaminationTests
{
	private static WorldGeometry Small() => new(4200, 1200, 87, 250, 400, 1000);

	private static TestRunResult Run(string name, ITestContext context)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);
		DiscoveryResult one = new() { Tests = [.. all.Tests.Where(t => t.Name == name)], Errors = [] };

		return new TestRunner(one, new TestRunnerOptions {
			MaxTier = TestTier.World,
			Arena = new Arena(Small()),
			CreateContext = _ => context,
		}).RunToCompletion();
	}

	[Fact]
	public void A_passing_test_whose_box_was_contaminated_is_downgraded_to_an_error()
	{
		// Not a failure, which would blame a subject that may be fine, and
		// certainly not a pass, which would claim coverage that did not happen.
		TestResult result = Run(nameof(Fixtures.Passes), new Contaminated("NPC 42 (Zombie) entered the box"))
			.Suites.Single().Results.Single();

		XAssert.Equal(TestOutcome.Errored, result.Outcome);
		XAssert.Contains("cannot be trusted", result.Message);
		XAssert.Contains("NPC 42", result.Message);
	}

	[Fact]
	public void A_clean_box_leaves_a_passing_test_alone()
	{
		TestResult result = Run(nameof(Fixtures.Passes), new Contaminated())
			.Suites.Single().Results.Single();

		XAssert.Equal(TestOutcome.Passed, result.Outcome);
	}

	[Fact]
	public void A_genuine_failure_outranks_a_note_about_contamination()
	{
		// The assertion message is the more useful of the two, so it stands.
		TestResult result = Run(nameof(Fixtures.Fails), new Contaminated("something wandered in"))
			.Suites.Single().Results.Single();

		XAssert.Equal(TestOutcome.Failed, result.Outcome);
		XAssert.Contains("deliberate", result.Message);
	}

	[Fact]
	public void Every_intruder_is_named_not_just_counted()
	{
		TestResult result = Run(nameof(Fixtures.Passes), new Contaminated("first thing", "second thing"))
			.Suites.Single().Results.Single();

		XAssert.Contains("first thing", result.Message);
		XAssert.Contains("second thing", result.Message);
	}

	[Fact]
	public void A_context_that_does_not_watch_is_simply_not_asked()
	{
		TestResult result = Run(nameof(Fixtures.Passes), new Oblivious())
			.Suites.Single().Results.Single();

		XAssert.Equal(TestOutcome.Passed, result.Outcome);
	}

	private sealed class Contaminated(params string[] seen) : IContaminationAware
	{
		public IReadOnlyList<string> Contamination { get; } = seen;
		public TileRect Interior => new(0, 0, 10, 10);
		public TileRect Bounds => new(0, 0, 10, 10);
		public Band Bands => Band.Cavern;
		public int ElapsedTicks => 0;
	}

	private sealed class Oblivious : ITestContext
	{
		public TileRect Interior => new(0, 0, 10, 10);
		public TileRect Bounds => new(0, 0, 10, 10);
		public Band Bands => Band.Cavern;
		public int ElapsedTicks => 0;
	}

	public class Fixtures
	{
		[GameTest(Band = Band.Cavern)]
		public void Passes(ITestContext ctx) => Assert.NotNull(ctx);

		[GameTest(Band = Band.Cavern)]
		public void Fails(ITestContext ctx) => Assert.Fail("deliberate");
	}
}
