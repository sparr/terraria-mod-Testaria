using System.Collections;

namespace Testaria.Tests;

public class TestDiscoveryTests
{
	private static DiscoveryResult Discover<T>() => TestDiscovery.Discover([typeof(T)]);

	[Fact]
	public void Finds_a_void_loaded_test()
	{
		DiscoveryResult result = Discover<WellFormed>();
		TestCase test = result.Tests.Single(t => t.Name == nameof(WellFormed.LoadedVoid));

		XAssert.Empty(result.Errors);
		XAssert.Equal(TestTier.Loaded, test.Tier);
		XAssert.Equal(TestBodyKind.Immediate, test.BodyKind);
		XAssert.False(test.WantsContext);
		XAssert.Null(test.Box);
	}

	[Fact]
	public void Finds_a_coroutine_game_test_and_records_its_box()
	{
		TestCase test = Discover<WellFormed>().Tests.Single(t => t.Name == nameof(WellFormed.GameCoroutine));

		XAssert.Equal(TestTier.World, test.Tier);
		XAssert.Equal(TestBodyKind.Coroutine, test.BodyKind);
		XAssert.NotNull(test.Box);
		XAssert.Equal(BoxKind.Banded, test.Box!.Value.Kind);
		XAssert.Equal(Band.Cavern, test.Box!.Value.Bands);
	}

	[Fact]
	public void Records_that_a_test_wants_a_context()
	{
		TestCase test = Discover<WellFormed>().Tests.Single(t => t.Name == nameof(WellFormed.WithContext));

		XAssert.True(test.WantsContext);
	}

	[Fact]
	public void A_spanning_game_test_yields_a_spanning_box_with_gaps_filled()
	{
		TestCase test = Discover<WellFormed>().Tests.Single(t => t.Name == nameof(WellFormed.Spanning));

		XAssert.Equal(BoxKind.Spanning, test.Box!.Value.Kind);
		XAssert.Equal(Band.Surface | Band.Underground | Band.Cavern, test.Box!.Value.Bands);
	}

	[Fact]
	public void A_skip_reason_is_carried_rather_than_dropping_the_test()
	{
		// A skipped test must still appear in the suite, or the suite silently
		// shrinks and nobody notices the coverage left.
		TestCase test = Discover<WellFormed>().Tests.Single(t => t.Name == nameof(WellFormed.Skipped));

		XAssert.Equal("needs 1.4.5", test.SkipReason);
	}

	[Fact]
	public void A_timeout_is_carried_in_ticks()
	{
		TestCase test = Discover<WellFormed>().Tests.Single(t => t.Name == nameof(WellFormed.GameCoroutine));

		XAssert.Equal(600, test.TimeoutTicks);
	}

	[Fact]
	public void Static_test_methods_are_supported()
		=> XAssert.Contains(Discover<WellFormed>().Tests, t => t.Name == nameof(WellFormed.StaticTest));

	[Fact]
	public void Unmarked_methods_are_ignored()
		=> XAssert.DoesNotContain(Discover<WellFormed>().Tests, t => t.Name == nameof(WellFormed.NotATest));

	[Fact]
	public void FreshWorld_is_orthogonal_to_getting_a_box()
	{
		// A fresh world isolates a test from other tests and from
		// world-global state; a box gives it somewhere defined to work.
		// Withholding the box left such a test with an empty Interior and
		// nowhere to put anything.
		TestCase test = Discover<WellFormed>().Tests.Single(t => t.Name == nameof(WellFormed.NeedsFreshWorld));

		XAssert.True(test.FreshWorld);
		XAssert.NotNull(test.Box);
	}

	[Fact]
	public void FreshWorld_on_a_class_applies_to_every_test_in_it()
	{
		DiscoveryResult result = Discover<FreshWorldHost>();

		XAssert.All(result.Tests, t => XAssert.True(t.FreshWorld));
		XAssert.NotEmpty(result.Tests);
	}

	[Fact]
	public void A_non_public_test_method_is_an_error_not_a_silent_omission()
	{
		DiscoveryResult result = Discover<Malformed>();

		XAssert.Contains(result.Errors, e => e.Location.EndsWith("NotPublic", StringComparison.Ordinal));
		XAssert.DoesNotContain(result.Tests, t => t.Name == "NotPublic");
	}

	[Fact]
	public void A_wrong_return_type_is_an_error_that_says_what_to_do()
	{
		TestDiscoveryError error = Discover<Malformed>().Errors
			.Single(e => e.Location.EndsWith(nameof(Malformed.WrongReturnType), StringComparison.Ordinal));

		XAssert.Contains("void or IEnumerator", error.Message);
	}

	[Fact]
	public void Too_many_parameters_is_an_error()
		=> XAssert.Contains(Discover<Malformed>().Errors,
			e => e.Location.EndsWith(nameof(Malformed.TooManyParameters), StringComparison.Ordinal));

	[Fact]
	public void A_data_parameter_with_no_source_is_an_error()
	{
		// A non-context parameter is data now, so the fault is not the
		// parameter's type but that nothing supplies a value for it.
		TestDiscoveryError error = Discover<Malformed>().Errors
			.Single(e => e.Location.EndsWith(nameof(Malformed.WrongParameterType), StringComparison.Ordinal));

		XAssert.Contains("[Case]", error.Message);
		XAssert.Contains("[CaseSource]", error.Message);
	}

	[Fact]
	public void A_generic_test_method_is_an_error()
		=> XAssert.Contains(Discover<Malformed>().Errors,
			e => e.Location.EndsWith(nameof(Malformed.Generic), StringComparison.Ordinal));

	[Fact]
	public void An_instance_test_without_a_parameterless_constructor_is_an_error()
	{
		TestDiscoveryError error = Discover<NoParameterlessConstructor>().Errors.Single();

		XAssert.Contains("parameterless constructor", error.Message);
	}

	[Fact]
	public void An_instance_test_on_an_abstract_type_is_an_error()
	{
		TestDiscoveryError error = Discover<AbstractHost>().Errors.Single();

		XAssert.Contains("abstract", error.Message);
	}

	[Fact]
	public void A_discovery_error_renders_as_an_errored_result()
	{
		TestDiscoveryError error = Discover<AbstractHost>().Errors.Single();
		TestResult result = error.ToResult();

		XAssert.Equal(TestOutcome.Errored, result.Outcome);
		XAssert.Equal(nameof(AbstractHost.Orphan), result.Name);
		XAssert.Contains(nameof(AbstractHost), result.ClassName);
	}

	[Fact]
	public void Discovering_a_type_with_no_tests_yields_nothing_and_no_errors()
	{
		DiscoveryResult result = Discover<TestDiscoveryTests>();

		XAssert.Empty(result.Tests);
		XAssert.Empty(result.Errors);
	}

	[Fact]
	public void Discovering_an_assembly_finds_the_fixtures_in_it()
	{
		DiscoveryResult result = TestDiscovery.Discover(typeof(WellFormed).Assembly);

		XAssert.Contains(result.Tests, t => t.Name == nameof(WellFormed.LoadedVoid));
	}

	// ---- fixtures ----

	public class WellFormed
	{
		[LoadedTest]
		public void LoadedVoid() { }

		[LoadedTest]
		public static void StaticTest() { }

		[GameTest(Band = Band.Cavern, Width = 80, Height = 48, Timeout = 600)]
		public IEnumerator GameCoroutine() { yield break; }

		[GameTest]
		public IEnumerator WithContext(ITestContext ctx) { yield break; }

		[GameTest(Spans = Band.Surface | Band.Cavern, Width = 48)]
		public IEnumerator Spanning() { yield break; }

		[LoadedTest(Skip = "needs 1.4.5")]
		public void Skipped() { }

		[GameTest]
		[FreshWorld]
		public void NeedsFreshWorld() { }

		public void NotATest() { }
	}

	[FreshWorld]
	public class FreshWorldHost
	{
		[GameTest]
		public void One() { }

		[GameTest]
		public void Two() { }
	}

	public class Malformed
	{
		[LoadedTest]
		internal void NotPublic() { }

		[LoadedTest]
		public int WrongReturnType() => 0;

		[LoadedTest]
		public void TooManyParameters(ITestContext a, ITestContext b) { }

		[LoadedTest]
		public void WrongParameterType(int notAContext) { }

		[LoadedTest]
		public void Generic<T>() { }
	}

	public class NoParameterlessConstructor(int unused)
	{
		private readonly int value = unused;

		[LoadedTest]
		public void Orphan() => XAssert.Equal(value, value);
	}

	public abstract class AbstractHost
	{
		[LoadedTest]
		public void Orphan() { }
	}
}
