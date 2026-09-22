namespace Testaria.Tests;

public class TestCatalogTests
{
	private static IReadOnlyList<TestCase> Discovered() => TestDiscovery.Discover([typeof(Fixtures)]).Tests;

	[Fact]
	public void The_header_comes_first()
		=> XAssert.Equal(TestCatalog.Header, TestCatalog.ToTsv(Discovered()).Split('\n')[0]);

	[Fact]
	public void Every_test_gets_a_row()
	{
		string[] lines = TestCatalog.ToTsv(Discovered()).TrimEnd('\n').Split('\n');

		XAssert.Equal(Discovered().Count + 1, lines.Length);
	}

	[Fact]
	public void Rows_have_four_tab_separated_fields()
	{
		foreach (string line in TestCatalog.ToTsv(Discovered()).TrimEnd('\n').Split('\n'))
			XAssert.Equal(4, line.Split('\t').Length);
	}

	[Fact]
	public void The_fresh_world_column_is_what_a_harness_plans_on()
	{
		// Running a dedicated process per fresh-world test is the only honest
		// way to give it one, so this column has to be right.
		string tsv = TestCatalog.ToTsv(Discovered());
		string fresh = tsv.Split('\n').Single(l => l.Contains(nameof(Fixtures.WantsAFreshWorld), StringComparison.Ordinal));
		string ordinary = tsv.Split('\n').Single(l => l.Contains(nameof(Fixtures.Ordinary), StringComparison.Ordinal));

		XAssert.Equal("yes", fresh.Split('\t')[1]);
		XAssert.Equal("no", ordinary.Split('\t')[1]);
	}

	[Fact]
	public void The_tier_column_carries_the_declared_tier()
	{
		string line = TestCatalog.ToTsv(Discovered()).Split('\n')
			.Single(l => l.Contains(nameof(Fixtures.Ordinary), StringComparison.Ordinal));

		XAssert.Equal(nameof(TestTier.World), line.Split('\t')[0]);
	}

	[Fact]
	public void An_empty_catalogue_is_still_a_valid_table()
	{
		string tsv = TestCatalog.ToTsv([]);

		XAssert.Equal(TestCatalog.Header + "\n", tsv);
	}

	[Fact]
	public void The_summary_counts_by_tier_and_flags_fresh_world_tests()
	{
		string summary = TestCatalog.Summarize(Discovered());

		XAssert.Contains("loaded", summary);
		XAssert.Contains("world", summary);
		XAssert.Contains("wanting a fresh world", summary);
	}

	[Fact]
	public void The_summary_says_so_when_there_is_nothing()
		=> XAssert.Equal("no tests", TestCatalog.Summarize([]));

	public class Fixtures
	{
		[LoadedTest]
		public void Registered() { }

		[GameTest(Band = Band.Cavern)]
		public void Ordinary() { }

		[GameTest(Band = Band.Cavern)]
		[FreshWorld]
		public void WantsAFreshWorld() { }
	}
}
