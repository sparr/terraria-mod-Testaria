namespace Testaria.Tests;

/// <summary>
/// <see cref="RequiresModAttribute"/> and
/// <see cref="RequiresModAbsentAttribute"/>, which say which half of a mod's
/// behaviour a test is about.
/// <para/>
/// A weak reference exists so a mod does one thing with its optional dependency
/// and another without, and a run can only be in one of those states at a time.
/// The failure this exists to prevent is silent: a test written for the
/// integrated path, run with the other mod absent, exercises the fallback and
/// passes, having asked nothing it meant to ask. So an unmet requirement is a
/// skip carrying its reason, never a pass, and never a failure either, since
/// nothing is wrong with a test whose turn has not come.
/// </summary>
public class ModRequirementTests
{
	private sealed class Fixtures
	{
		[LoadedTest]
		[RequiresMod("Friend")]
		public void NeedsFriend() { }

		[LoadedTest]
		[RequiresModAbsent("Friend")]
		public void NeedsNoFriend() { }

		[LoadedTest]
		[RequiresMod("Friend")]
		[RequiresMod("Other")]
		public void NeedsBoth() { }

		[LoadedTest]
		public void Ordinary() { }
	}

	[RequiresMod("Friend")]
	private sealed class WholeClassNeedsFriend
	{
		[LoadedTest]
		public void One() { }

		[LoadedTest]
		[RequiresModAbsent("Rival")]
		public void AlsoNeedsRivalGone() { }
	}

	private static TestCase Case(string name)
		=> TestDiscovery.Discover([typeof(Fixtures)]).Tests.Single(t => t.Name == name);

	private static IReadOnlySet<string> Loaded(params string[] mods)
		=> mods.ToHashSet(StringComparer.OrdinalIgnoreCase);

	[Fact]
	public void A_declared_requirement_is_discovered()
	{
		XAssert.Equal(["Friend"], Case(nameof(Fixtures.NeedsFriend)).RequiresMods);
		XAssert.Equal(["Friend"], Case(nameof(Fixtures.NeedsNoFriend)).RequiresModsAbsent);
		XAssert.Equal(["Friend", "Other"], Case(nameof(Fixtures.NeedsBoth)).RequiresMods);
	}

	/// <summary>A class declaration reaches every test in it, and a method adds to it.</summary>
	[Fact]
	public void A_class_declaration_reaches_its_methods()
	{
		IReadOnlyList<TestCase> tests = TestDiscovery.Discover([typeof(WholeClassNeedsFriend)]).Tests;

		XAssert.All(tests, t => XAssert.Equal(["Friend"], t.RequiresMods));

		XAssert.Equal(
			["Rival"],
			tests.Single(t => t.Name == nameof(WholeClassNeedsFriend.AlsoNeedsRivalGone)).RequiresModsAbsent);
	}

	[Fact]
	public void A_test_that_declares_nothing_is_never_gated()
	{
		XAssert.Null(Case(nameof(Fixtures.Ordinary)).UnmetModRequirement(Loaded()));
		XAssert.Null(Case(nameof(Fixtures.Ordinary)).UnmetModRequirement(null));
	}

	[Fact]
	public void A_needed_mod_that_is_loaded_satisfies_the_requirement()
		=> XAssert.Null(Case(nameof(Fixtures.NeedsFriend)).UnmetModRequirement(Loaded("Friend")));

	[Fact]
	public void A_needed_mod_that_is_missing_is_named()
	{
		string? reason = Case(nameof(Fixtures.NeedsFriend)).UnmetModRequirement(Loaded("Other"));

		XAssert.NotNull(reason);
		XAssert.Contains("Friend", reason);
	}

	[Fact]
	public void Every_missing_mod_is_named_at_once()
	{
		string? reason = Case(nameof(Fixtures.NeedsBoth)).UnmetModRequirement(Loaded());

		XAssert.NotNull(reason);
		XAssert.Contains("Friend", reason);
		XAssert.Contains("Other", reason);
	}

	[Fact]
	public void A_mod_that_must_be_absent_and_is_satisfies_the_requirement()
		=> XAssert.Null(Case(nameof(Fixtures.NeedsNoFriend)).UnmetModRequirement(Loaded("Other")));

	[Fact]
	public void A_mod_that_must_be_absent_and_is_loaded_is_named()
	{
		string? reason = Case(nameof(Fixtures.NeedsNoFriend)).UnmetModRequirement(Loaded("Friend"));

		XAssert.NotNull(reason);
		XAssert.Contains("Friend", reason);
	}

	/// <summary>
	/// The case that would otherwise pass for the wrong reason. Outside a loaded
	/// game there is no mod list, and answering "absent" would report a pass for
	/// a fallback path nothing exercised.
	/// </summary>
	[Fact]
	public void A_runner_that_cannot_tell_skips_rather_than_assuming_absence()
	{
		XAssert.NotNull(Case(nameof(Fixtures.NeedsFriend)).UnmetModRequirement(null));
		XAssert.NotNull(Case(nameof(Fixtures.NeedsNoFriend)).UnmetModRequirement(null));
	}

	[Fact]
	public void The_runner_reports_an_unmet_requirement_as_a_skip()
	{
		TestResult result = Single(Run(nameof(Fixtures.NeedsFriend), Loaded()));

		XAssert.Equal(TestOutcome.Skipped, result.Outcome);
		XAssert.Contains("Friend", result.Message);
	}

	[Fact]
	public void The_runner_runs_a_test_whose_requirement_is_met()
		=> XAssert.Equal(
			TestOutcome.Passed,
			Single(Run(nameof(Fixtures.NeedsFriend), Loaded("Friend"))).Outcome);

	[Fact]
	public void The_runner_runs_a_test_whose_absence_requirement_is_met()
		=> XAssert.Equal(
			TestOutcome.Passed,
			Single(Run(nameof(Fixtures.NeedsNoFriend), Loaded("Other"))).Outcome);

	private static TestRunResult Run(string name, IReadOnlySet<string>? loadedMods)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);

		var one = new DiscoveryResult {
			Tests = [all.Tests.Single(t => t.Name == name)],
			Errors = all.Errors,
		};

		return new TestRunner(one, new TestRunnerOptions {
			RunName = "mods",
			MaxTier = TestTier.Loaded,
			LoadedMods = loadedMods,
		}).RunToCompletion();
	}

	private static TestResult Single(TestRunResult run) => run.Suites.Single().Results.Single();
}
