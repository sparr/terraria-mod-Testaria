namespace Testaria.Tests;

public class TestFilterTests
{
	// ClassName comes from the declaring type, so these are the real names a
	// filter would be matched against rather than invented ones.
	private static TestCase Case(string name)
		=> new() {
			Method = typeof(Subject).GetMethod(name)!,
			Tier = TestTier.Loaded,
			BodyKind = TestBodyKind.Immediate,
			WantsContext = false,
		};

	private static TestCase Zombie() => Case(nameof(Subject.Zombie_dies_to_a_sword));
	private static TestCase Recipe() => Case(nameof(Subject.Recipe_resolves));

	[Fact]
	public void The_all_filter_matches_everything()
	{
		XAssert.False(TestFilter.All.IsNarrowing);
		XAssert.True(TestFilter.All.Matches(Zombie()));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void An_empty_pattern_is_the_all_filter(string? pattern)
		=> XAssert.False(TestFilter.Parse(pattern).IsNarrowing);

	[Fact]
	public void A_bare_fragment_matches_as_a_substring()
	{
		// What someone typing part of a name means.
		XAssert.True(TestFilter.Parse("Zombie").Matches(Zombie()));
		XAssert.False(TestFilter.Parse("Zombie").Matches(Recipe()));
	}

	[Fact]
	public void Matching_is_case_insensitive()
		=> XAssert.True(TestFilter.Parse("zOmBiE").Matches(Zombie()));

	[Fact]
	public void An_escaped_fully_qualified_name_matches_exactly()
	{
		// A name is not a pattern. Nested types carry a '+' and namespaces a
		// '.', both of which mean something else to a regex, so pasting a name
		// from the catalogue needs escaping.
		TestCase zombie = Zombie();
		string literal = System.Text.RegularExpressions.Regex.Escape($"{zombie.ClassName}.{zombie.Name}");

		XAssert.True(TestFilter.Parse(literal).Matches(zombie));
	}

	[Fact]
	public void An_unescaped_nested_type_name_does_not_match_itself()
	{
		// Worth pinning: '+' means "one or more" rather than a literal plus,
		// so the obvious thing to type quietly fails. Documented on Parse.
		TestCase zombie = Zombie();

		XAssert.Contains("+", zombie.ClassName);
		XAssert.False(TestFilter.Parse($"{zombie.ClassName}.{zombie.Name}").Matches(zombie));
	}

	[Fact]
	public void A_class_name_matches_every_test_in_it()
	{
		XAssert.True(TestFilter.Parse("Subject").Matches(Zombie()));
		XAssert.True(TestFilter.Parse("Subject").Matches(Recipe()));
	}

	[Theory]
	[InlineData("Zombie.*sword")]
	[InlineData("^Zombie")]
	[InlineData("sword$")]
	[InlineData(".*dies.*")]
	[InlineData("^Zombie_dies_to_a_sword$")]
	public void Regular_expressions_match_as_expected(string pattern)
		=> XAssert.True(TestFilter.Parse(pattern).Matches(Zombie()));

	[Theory]
	[InlineData("^Recipe")]
	[InlineData("axe$")]
	[InlineData("^Zombie_dies_to_a_sword_and_more$")]
	public void Regular_expressions_reject_what_they_should(string pattern)
		=> XAssert.False(TestFilter.Parse(pattern).Matches(Zombie()));

	[Fact]
	public void Alternation_selects_several_tests_at_once()
	{
		// The thing a glob could not do, and a common reason to filter.
		TestFilter filter = TestFilter.Parse("Zombie|Recipe");

		XAssert.True(filter.Matches(Zombie()));
		XAssert.True(filter.Matches(Recipe()));
	}

	[Fact]
	public void Negative_lookahead_excludes_tests()
	{
		// "everything except" is why a match timeout is used rather than
		// non-backtracking matching, which would forbid lookarounds.
		TestFilter filter = TestFilter.Parse("^(?!.*Zombie)");

		XAssert.False(filter.Matches(Zombie()));
		XAssert.True(filter.Matches(Recipe()));
	}

	[Fact]
	public void Anchoring_distinguishes_a_prefix_from_a_substring()
	{
		XAssert.True(TestFilter.Parse("dies").Matches(Zombie()));
		XAssert.False(TestFilter.Parse("^dies").Matches(Zombie()));
	}

	[Fact]
	public void An_invalid_pattern_is_reported_rather_than_matching_nothing()
	{
		// Silently matching nothing would look like a suite with no tests.
		var ex = XAssert.Throws<ArgumentException>(() => TestFilter.Parse("Zombie("));

		XAssert.Contains("not a valid regular expression", ex.Message);
		XAssert.Contains("Zombie(", ex.Message);
	}

	[Fact]
	public void A_filter_reports_how_it_reads()
	{
		XAssert.Equal("(all)", TestFilter.All.ToString());
		XAssert.Equal("Zombie*", TestFilter.Parse("Zombie*").ToString());
	}

	public class Subject
	{
		public void Zombie_dies_to_a_sword() { }
		public void Recipe_resolves() { }
	}
}

public class RunnerFilterTests
{
	private static DiscoveryResult Discovered() => TestDiscovery.Discover([typeof(Fixtures)]);

	private static TestRunner Runner(string? pattern) => new(Discovered(), new TestRunnerOptions {
		MaxTier = TestTier.Loaded,
		Filter = TestFilter.Parse(pattern),
	});

	[Fact]
	public void Without_a_filter_everything_runs()
	{
		TestRunner runner = Runner(null);
		runner.RunToCompletion();

		XAssert.Equal(3, runner.Result.Total);
		XAssert.Equal(0, runner.FilteredOut);
	}

	[Fact]
	public void A_filter_narrows_the_run()
	{
		TestRunner runner = Runner("Alpha");
		runner.RunToCompletion();

		XAssert.Equal(1, runner.Result.Total);
		XAssert.Equal("Alpha", runner.Result.Suites.Single().Results.Single().Name);
	}

	[Fact]
	public void Filtered_out_tests_are_omitted_not_reported_as_skipped()
	{
		// Three hundred skip entries would bury the handful of results an
		// operator actually asked for.
		TestRunner runner = Runner("Alpha");
		runner.RunToCompletion();

		XAssert.Equal(0, runner.Result.Skipped);
		XAssert.DoesNotContain(runner.Result.Suites.SelectMany(s => s.Results), r => r.Name == "Beta");
	}

	[Fact]
	public void The_number_held_back_is_reported_so_a_narrowed_run_is_visible()
	{
		// Silently running less than asked is the one thing a filter must not
		// do, because the suite would look complete.
		TestRunner runner = Runner("Alpha");

		XAssert.Equal(2, runner.FilteredOut);
	}

	[Fact]
	public void A_filter_matching_nothing_runs_nothing_and_says_so()
	{
		TestRunner runner = Runner("NoSuchTest");
		runner.RunToCompletion();

		XAssert.Equal(0, runner.Result.Total);
		XAssert.Equal(3, runner.FilteredOut);
	}

	public class Fixtures
	{
		[LoadedTest]
		public void Alpha() { }

		[LoadedTest]
		public void Beta() { }

		[LoadedTest]
		public void Gamma() { }
	}
}
