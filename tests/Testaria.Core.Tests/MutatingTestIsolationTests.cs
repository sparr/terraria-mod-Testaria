namespace Testaria.Tests;

/// <summary>
/// <see cref="MutatesGlobalStateAttribute"/> and
/// <see cref="TestRunnerOptions.IsolateMutatingTests"/>, the one switch that
/// turns a preference for a world of one's own into a requirement.
/// <para/>
/// The distinction under test is which way the default falls. A test marked
/// <c>[MutatesGlobalState]</c> runs in a shared world unless the run asks
/// otherwise, where a <c>[FreshWorld]</c> test never does. Getting that backwards
/// would either make the ordinary run refuse to test anything, or make the
/// isolation silently unavailable.
/// </summary>
public class MutatingTestIsolationTests
{
	private sealed class Fixtures
	{
		[LoadedTest]
		[MutatesGlobalState]
		public void Mutates() { }

		[LoadedTest]
		[FreshWorld]
		public void Requires() { }

		[LoadedTest]
		public void Ordinary() { }
	}

	[MutatesGlobalState]
	private sealed class WholeClassMutates
	{
		[LoadedTest]
		public void One() { }

		[LoadedTest]
		public void Two() { }
	}

	private static TestCase Case(string name)
		=> TestDiscovery.Discover([typeof(Fixtures)]).Tests.Single(t => t.Name == name);

	[Fact]
	public void The_attribute_is_discovered()
	{
		XAssert.True(Case(nameof(Fixtures.Mutates)).MutatesGlobalState);
		XAssert.False(Case(nameof(Fixtures.Ordinary)).MutatesGlobalState);
	}

	/// <summary>It is a preference, not a requirement, so it is not FreshWorld.</summary>
	[Fact]
	public void Mutating_is_not_the_same_as_requiring_a_fresh_world()
	{
		XAssert.False(Case(nameof(Fixtures.Mutates)).FreshWorld);
		XAssert.True(Case(nameof(Fixtures.Requires)).FreshWorld);
	}

	[Fact]
	public void On_a_class_it_applies_to_every_test_in_it()
		=> XAssert.All(
			TestDiscovery.Discover([typeof(WholeClassMutates)]).Tests,
			t => XAssert.True(t.MutatesGlobalState));

	/// <summary>
	/// The default: a mutating test wants no world of its own, so it runs in
	/// whatever world is loaded.
	/// </summary>
	[Fact]
	public void Without_the_switch_a_mutating_test_needs_no_world_of_its_own()
		=> XAssert.False(Case(nameof(Fixtures.Mutates)).NeedsOwnWorld(isolateMutatingTests: false));

	/// <summary>With the switch it is treated exactly as though it had declared [FreshWorld].</summary>
	[Fact]
	public void With_the_switch_a_mutating_test_needs_a_world_of_its_own()
		=> XAssert.True(Case(nameof(Fixtures.Mutates)).NeedsOwnWorld(isolateMutatingTests: true));

	/// <summary>The switch changes nothing for a test that never said it mutates anything.</summary>
	[Fact]
	public void The_switch_does_not_reach_an_ordinary_test()
	{
		XAssert.False(Case(nameof(Fixtures.Ordinary)).NeedsOwnWorld(isolateMutatingTests: false));
		XAssert.False(Case(nameof(Fixtures.Ordinary)).NeedsOwnWorld(isolateMutatingTests: true));
	}

	/// <summary>And a declared requirement is unaffected by the switch either way.</summary>
	[Fact]
	public void A_declared_requirement_ignores_the_switch()
	{
		XAssert.True(Case(nameof(Fixtures.Requires)).NeedsOwnWorld(isolateMutatingTests: false));
		XAssert.True(Case(nameof(Fixtures.Requires)).NeedsOwnWorld(isolateMutatingTests: true));
	}

	/// <summary>
	/// Asking for isolation the host cannot give is a skip that names the switch,
	/// not a quiet run in a shared world.
	/// </summary>
	[Fact]
	public void Isolation_the_host_cannot_give_is_reported()
	{
		TestResult result = Single(Run(isolate: true, supportsFreshWorld: false));

		XAssert.Equal(TestOutcome.Skipped, result.Outcome);
		XAssert.Contains("MutatesGlobalState", result.Message);
	}

	/// <summary>And with the host able to provide it, the test simply runs.</summary>
	[Fact]
	public void Isolation_the_host_can_give_lets_the_test_run()
	{
		XAssert.Equal(TestOutcome.Passed,
			Single(Run(isolate: true, supportsFreshWorld: true)).Outcome);
	}

	/// <summary>With the switch off it runs without the host promising anything.</summary>
	[Fact]
	public void Without_the_switch_it_runs_in_a_shared_world()
	{
		XAssert.Equal(TestOutcome.Passed,
			Single(Run(isolate: false, supportsFreshWorld: false)).Outcome);
	}

	/// <summary>
	/// Runs just the mutating fixture, the way <see cref="TestRunnerTests"/> does:
	/// discovery narrowed to one case rather than a filter, so the run's shape is
	/// not part of what is under test.
	/// </summary>
	private static TestRunResult Run(bool isolate, bool supportsFreshWorld)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);

		var one = new DiscoveryResult {
			Tests = [all.Tests.Single(t => t.Name == nameof(Fixtures.Mutates))],
			Errors = all.Errors,
		};

		return new TestRunner(one, new TestRunnerOptions {
			RunName = "isolation",
			MaxTier = TestTier.Loaded,
			IsolateMutatingTests = isolate,
			SupportsFreshWorld = supportsFreshWorld,
		}).RunToCompletion();
	}

	private static TestResult Single(TestRunResult run) => run.Suites.Single().Results.Single();
}
