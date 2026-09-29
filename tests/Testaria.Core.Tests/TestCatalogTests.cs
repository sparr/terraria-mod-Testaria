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

	/// <summary>
	/// Every row has as many fields as the header names, and the readers index
	/// by position, so a column may be appended and never moved.
	/// </summary>
	[Fact]
	public void Rows_have_a_field_for_every_column_in_the_header()
	{
		int columns = TestCatalog.Header.Split('\t').Length;

		foreach (string line in TestCatalog.ToTsv(Discovered()).TrimEnd('\n').Split('\n'))
			XAssert.Equal(columns, line.Split('\t').Length);
	}

	/// <summary>
	/// The columns run-fresh.sh reads by number, pinned so that appending
	/// another cannot quietly move them.
	/// </summary>
	[Fact]
	public void The_first_four_columns_keep_their_positions()
		=> XAssert.Equal(["tier", "freshWorld", "className", "name"], TestCatalog.Header.Split('\t')[..4]);

	/// <summary>
	/// A harness planning runs needs to know which mods a test asked for, since
	/// no single run can satisfy a suite that wants one loaded and absent.
	/// </summary>
	[Fact]
	public void The_mod_columns_carry_what_a_test_declared()
	{
		string row = TestCatalog.ToTsv(Discovered()).Split('\n')
			.Single(l => l.Contains(nameof(Fixtures.NeedsAFriend), StringComparison.Ordinal));

		string[] fields = row.Split('\t');

		XAssert.Equal("Friend", fields[4]);
		XAssert.Equal("Rival", fields[5]);
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

		[LoadedTest]
		[RequiresMod("Friend")]
		[RequiresModAbsent("Rival")]
		public void NeedsAFriend() { }
	}
}
