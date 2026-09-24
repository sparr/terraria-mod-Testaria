namespace Testaria.Tool.Tests;

/// <summary>
/// Reading the command line.
/// <para/>
/// Worth testing at this length because every one of these flags is a silent
/// failure when it goes missing: a dropped <c>--filter</c> runs the whole
/// suite, a dropped <c>--blank</c> runs against a real world, and both report
/// a perfectly ordinary looking result.
/// </summary>
public class CommandLineTests
{
	private static RunOptions Parse(params string[] args)
	{
		ParseResult result = CommandLine.Parse(args);

		XAssert.Null(result.Error);
		XAssert.NotNull(result.Options);

		return result.Options;
	}

	private static string Error(params string[] args)
	{
		ParseResult result = CommandLine.Parse(args);

		XAssert.NotNull(result.Error);

		return result.Error;
	}

	[Fact]
	public void No_arguments_asks_for_help()
		=> XAssert.True(CommandLine.Parse([]).WantsHelp);

	[Theory]
	[InlineData("-h")]
	[InlineData("--help")]
	[InlineData("help")]
	public void Help_is_asked_for_in_the_usual_ways(string flag)
		=> XAssert.True(CommandLine.Parse([flag]).WantsHelp);

	[Fact]
	public void Help_after_a_verb_still_asks_for_help()
		=> XAssert.True(CommandLine.Parse(["run", "--help"]).WantsHelp);

	[Fact]
	public void An_unknown_command_is_refused()
		=> XAssert.Contains("frobnicate", Error("frobnicate"));

	[Fact]
	public void An_unknown_option_is_refused()
		// Rather than ignored: a misspelled flag that is silently dropped runs
		// something other than what was asked for and says nothing about it.
		=> XAssert.Contains("--fliter", Error("run", "--mod", "M", "--fliter", "x"));

	[Fact]
	public void A_run_needs_at_least_one_mod()
		=> XAssert.Contains("--mod", Error("run"));

	[Fact]
	public void Mods_keep_the_order_they_were_given_in()
		// Load order, which decides modded id assignment, so it is not a set.
		=> XAssert.Equal(["A", "B"], Parse("run", "--mod", "A", "--mod", "B").Mods);

	[Fact]
	public void List_is_the_same_options_without_the_running()
	{
		RunOptions options = Parse("list", "--mod", "A", "--filter", "Slime");

		XAssert.True(options.List);
		XAssert.Equal("Slime", options.Filter);
	}

	[Fact]
	public void Everything_has_a_default_worth_having()
	{
		RunOptions options = Parse("run", "--mod", "A");

		XAssert.Equal("Testaria", options.Name);
		XAssert.Equal(42, options.WorldSeed);
		XAssert.Equal(600, options.TimeoutSeconds);
		XAssert.Null(options.Filter);
		// Null rather than zero, so the tool passes no seed flag at all and
		// the game's own default decides. Two places claiming the default is
		// where they drift apart.
		XAssert.Null(options.RunSeed);
		XAssert.False(options.BlankWorld);
		XAssert.False(options.FreshWorld);
	}

	[Fact]
	public void The_flags_that_take_values_take_them()
	{
		RunOptions options = Parse(
			"run", "--mod", "A",
			"--name", "Suite", "--filter", "Zombie", "--seed", "7", "--world-seed", "99",
			"--speed", "max", "--timeout", "120", "--results", "out.xml",
			"--tml", "/tml", "--mods-dir", "/mods", "--project", "/src/MyMod");

		XAssert.Equal("Suite", options.Name);
		XAssert.Equal("Zombie", options.Filter);
		XAssert.Equal(7, options.RunSeed);
		XAssert.Equal(99, options.WorldSeed);
		XAssert.Equal("max", options.Speed);
		XAssert.Equal(120, options.TimeoutSeconds);
		XAssert.Equal("out.xml", options.ResultsOut);
		XAssert.Equal("/tml", options.TmlPath);
		XAssert.Equal("/mods", options.ModsDirectory);
		XAssert.Equal(["/src/MyMod"], options.Projects);
	}

	[Fact]
	public void The_flags_that_take_nothing_take_nothing()
	{
		RunOptions options = Parse("run", "--mod", "A", "--blank", "--fresh-world", "--keep-scratch", "--quiet", "--verbose");

		XAssert.True(options.BlankWorld);
		XAssert.True(options.FreshWorld);
		XAssert.True(options.KeepScratch);
		XAssert.True(options.Quiet);
		XAssert.True(options.Verbose);
	}

	[Fact]
	public void Measuring_is_off_unless_asked_for()
	{
		XAssert.False(Parse("run", "--mod", "A").Measure);
		XAssert.True(Parse("run", "--mod", "A", "--measure").Measure);
	}

	[Fact]
	public void A_value_flag_with_no_value_says_which_one()
		=> XAssert.Contains("--filter", Error("run", "--mod", "A", "--filter"));

	[Fact]
	public void A_flag_cannot_swallow_the_next_flag_as_its_value()
		// "--filter --blank" is a mistake, not a filter named --blank.
		=> XAssert.Contains("--filter", Error("run", "--mod", "A", "--filter", "--blank"));

	[Theory]
	[InlineData("--seed", "soon")]
	[InlineData("--world-seed", "big")]
	[InlineData("--timeout", "later")]
	public void A_number_that_is_not_a_number_is_refused(string flag, string value)
		=> XAssert.Contains(flag, Error("run", "--mod", "A", flag, value));

	[Fact]
	public void A_minimum_number_of_tests_can_be_demanded()
		=> XAssert.Equal(12, Parse("run", "--mod", "A", "--require", "12").Require);

	[Fact]
	public void At_least_one_test_is_demanded_by_default()
	{
		// A run in which nothing ran has established nothing, and there is no
		// reason anybody would want that reported as success.
		XAssert.Equal(1, Parse("run", "--mod", "A").Require);
	}

	[Fact]
	public void The_demand_can_be_switched_off()
		=> XAssert.Equal(0, Parse("run", "--mod", "A", "--require", "0").Require);

	[Fact]
	public void A_nonsensical_demand_is_refused()
		=> XAssert.Contains("--require", Error("run", "--mod", "A", "--require", "lots"));

	[Fact]
	public void A_timeout_must_be_positive()
		// Zero would mean "give up before starting", which is never what
		// anyone means by it.
		=> XAssert.Contains("--timeout", Error("run", "--mod", "A", "--timeout", "0"));

	[Fact]
	public void A_negative_run_seed_is_perfectly_legal()
		// Seeds are just bits, and UnifiedRandom takes the absolute value.
		=> XAssert.Equal(-7, Parse("run", "--mod", "A", "--seed", "-7").RunSeed);

	[Fact]
	public void The_usage_text_names_every_exit_code()
	{
		XAssert.Contains("0  every test passed", CommandLine.Usage);
		XAssert.Contains("1  a test failed", CommandLine.Usage);
		XAssert.Contains("2  the harness", CommandLine.Usage);
	}
}
