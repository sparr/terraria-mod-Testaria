namespace Testaria.Tests;

public class TestRunResultTests
{
	[Fact]
	public void FromResults_groups_by_class_name()
	{
		var run = TestRunResult.FromResults("run", [
			TestResult.Pass("A", "one"),
			TestResult.Pass("B", "two"),
			TestResult.Pass("A", "three"),
		]);

		XAssert.Equal(2, run.Suites.Count);
		XAssert.Equal("A", run.Suites[0].Name);
		XAssert.Equal(2, run.Suites[0].Total);
		XAssert.Equal("B", run.Suites[1].Name);
		XAssert.Equal(1, run.Suites[1].Total);
	}

	[Fact]
	public void FromResults_preserves_first_seen_order_not_alphabetical_order()
	{
		var run = TestRunResult.FromResults("run", [
			TestResult.Pass("Zebra", "one"),
			TestResult.Pass("Aardvark", "two"),
		]);

		// Run order is the useful order for a human reading a report, so
		// grouping must not quietly sort.
		XAssert.Equal(["Zebra", "Aardvark"], run.Suites.Select(s => s.Name));
	}

	[Fact]
	public void FromResults_preserves_test_order_within_a_suite()
	{
		var run = TestRunResult.FromResults("run", [
			TestResult.Pass("A", "first"),
			TestResult.Pass("A", "second"),
			TestResult.Pass("A", "third"),
		]);

		XAssert.Equal(["first", "second", "third"], run.Suites[0].Results.Select(r => r.Name));
	}

	[Fact]
	public void FromResults_on_an_empty_sequence_yields_an_empty_successful_run()
	{
		var run = TestRunResult.FromResults("run", []);

		XAssert.Empty(run.Suites);
		XAssert.Equal(0, run.Total);
		XAssert.True(run.IsSuccess);
	}

	[Fact]
	public void Counts_are_tallied_per_outcome()
	{
		var run = TestRunResult.FromResults("run", [
			TestResult.Pass("A", "passing"),
			TestResult.Fail("A", "failing", "nope"),
			new TestResult { ClassName = "A", Name = "erroring", Outcome = TestOutcome.Errored, Message = "broke" },
			TestResult.Skip("A", "skipping", "not yet"),
		]);

		XAssert.Equal(4, run.Total);
		XAssert.Equal(1, run.Passed);
		XAssert.Equal(1, run.Failures);
		XAssert.Equal(1, run.Errors);
		XAssert.Equal(1, run.Skipped);
	}

	[Fact]
	public void Durations_sum_across_tests_and_suites()
	{
		var run = TestRunResult.FromResults("run", [
			TestResult.Pass("A", "one", TimeSpan.FromMilliseconds(250)),
			TestResult.Pass("B", "two", TimeSpan.FromMilliseconds(750)),
		]);

		XAssert.Equal(TimeSpan.FromSeconds(1), run.Duration);
	}

	[Fact]
	public void IsSuccess_is_false_for_failures_and_for_errors()
	{
		var failed = TestRunResult.FromResults("r", [TestResult.Fail("A", "x", "nope")]);
		var errored = TestRunResult.FromResults("r", [
			new TestResult { ClassName = "A", Name = "x", Outcome = TestOutcome.Errored },
		]);

		XAssert.False(failed.IsSuccess);
		XAssert.False(errored.IsSuccess);
	}

	[Fact]
	public void IsSuccess_is_true_when_tests_were_only_skipped()
	{
		// A skipped test is not a broken one, so a run of nothing but skips
		// must not fail the build.
		var run = TestRunResult.FromResults("r", [TestResult.Skip("A", "x", "not yet")]);

		XAssert.True(run.IsSuccess);
	}

	[Fact]
	public void TestResult_IsSuccess_treats_passed_and_skipped_alike()
	{
		XAssert.True(TestResult.Pass("A", "x").IsSuccess);
		XAssert.True(TestResult.Skip("A", "x", "r").IsSuccess);
		XAssert.False(TestResult.Fail("A", "x", "m").IsSuccess);
	}
}
