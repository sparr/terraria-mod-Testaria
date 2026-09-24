namespace Testaria.Tool.Tests;

/// <summary>
/// Reading the verdict back out of the report.
/// <para/>
/// This is what decides the exit code, so it is what decides whether CI goes
/// red. A reader that miscounts is a framework that lies to a build.
/// </summary>
public class RunReportTests
{
	private const string Green = """
		<?xml version="1.0" encoding="utf-8"?>
		<testsuites name="Run" tests="3" failures="0" errors="0" skipped="1" time="1.0">
		  <testsuite name="Suite" tests="3" failures="0" errors="0" skipped="1" time="1.0">
		    <testcase name="a" classname="Suite" time="0.1" />
		    <testcase name="b" classname="Suite" time="0.1" />
		    <testcase name="c" classname="Suite" time="0.1"><skipped message="not today" /></testcase>
		  </testsuite>
		</testsuites>
		""";

	private const string Red = """
		<?xml version="1.0" encoding="utf-8"?>
		<testsuites name="Run" tests="4" failures="1" errors="2" skipped="0" time="1.0">
		  <testsuite name="Suite" tests="4" failures="1" errors="2" skipped="0" time="1.0">
		    <testcase name="a" classname="Suite" time="0.1" />
		    <testcase name="b" classname="Suite" time="0.1"><failure message="expected 1&#10;actual 2" /></testcase>
		    <testcase name="c" classname="Suite" time="0.1"><error message="InvalidOperationException: nope" /></testcase>
		    <testcase name="d" classname="Suite" time="0.1"><error type="Testaria.Blocked" message="no box" /></testcase>
		  </testsuite>
		</testsuites>
		""";

	[Fact]
	public void A_green_run_counts_its_passes_and_succeeds()
	{
		RunReport report = RunReport.Parse(Green);

		XAssert.Equal(3, report.Total);
		XAssert.Equal(2, report.Passed);
		XAssert.Equal(1, report.Skipped);
		XAssert.True(report.IsSuccess);
		XAssert.Empty(report.Problems);
	}

	[Fact]
	public void A_skip_does_not_make_a_run_red()
		// A skipped test reports honestly that it did not run, which is a
		// result rather than a problem.
		=> XAssert.True(RunReport.Parse(Green).IsSuccess);

	[Fact]
	public void Blocked_tests_are_counted_apart_from_other_errors()
	{
		RunReport report = RunReport.Parse(Red);

		// The report's own errors attribute is 2 and includes the blocked one,
		// so counting both would report three problems where there are two.
		XAssert.Equal(1, report.Errors);
		XAssert.Equal(1, report.Blocked);
		XAssert.Equal(1, report.Failures);
		XAssert.Equal(1, report.Passed);
	}

	[Fact]
	public void A_blocked_test_makes_the_run_red()
		// It never ran, so it established nothing, and a run that quietly
		// dropped part of itself has not proved what it claims to have proved.
		=> XAssert.False(RunReport.Parse(Red).IsSuccess);

	[Fact]
	public void Every_problem_is_reported_with_its_whole_message()
	{
		RunReport report = RunReport.Parse(Red);

		XAssert.Equal(3, report.Problems.Count);

		Problem failure = report.Problems.Single(p => p.Name == "b");

		XAssert.Equal("failure", failure.Kind);
		// Both lines. The expected and actual values live after the first one,
		// so truncating here would print the least useful sentence available.
		XAssert.Contains("expected 1", failure.Message);
		XAssert.Contains("actual 2", failure.Message);
	}

	[Fact]
	public void A_blocked_problem_knows_it_is_blocked()
		=> XAssert.True(RunReport.Parse(Red).Problems.Single(p => p.Name == "d").Blocked);

	[Fact]
	public void The_summary_reads_the_way_the_shell_harness_reads()
		=> XAssert.Equal(
			"4 tests: 1 passed, 1 failed, 1 errored, 1 blocked, 0 skipped",
			RunReport.Parse(Red).Summarize());

	[Fact]
	public void The_summary_says_nothing_about_blocking_when_nothing_was_blocked()
		=> XAssert.DoesNotContain("blocked", RunReport.Parse(Green).Summarize());

	[Fact]
	public void A_run_knows_how_many_tests_actually_ran()
	{
		// Two ran and one skipped, which is the number a CI guard cares about:
		// a suite whose subject went missing skips everything and reports a
		// clean run.
		XAssert.Equal(2, RunReport.Parse(Green).Ran);
		XAssert.Equal(4, RunReport.Parse(Red).Ran);
	}

	[Fact]
	public void A_run_where_everything_skipped_ran_nothing()
	{
		RunReport report = RunReport.Parse(
			"<testsuites name=\"Run\" tests=\"3\" failures=\"0\" errors=\"0\" skipped=\"3\" time=\"1.0\">"
			+ "<testsuite name=\"Suite\" tests=\"3\" failures=\"0\" errors=\"0\" skipped=\"3\" time=\"1.0\">"
			+ "<testcase name=\"a\" classname=\"Suite\"><skipped message=\"subject absent\" /></testcase>"
			+ "<testcase name=\"b\" classname=\"Suite\"><skipped message=\"subject absent\" /></testcase>"
			+ "<testcase name=\"c\" classname=\"Suite\"><skipped message=\"subject absent\" /></testcase>"
			+ "</testsuite></testsuites>");

		// The shape that started all this: every test politely skipped, the
		// report clean, and nothing whatsoever established.
		XAssert.Equal(0, report.Ran);
		XAssert.True(report.IsSuccess);
	}

	[Fact]
	public void An_empty_run_is_a_success()
	{
		RunReport report = RunReport.Parse("""
			<testsuites name="Run" tests="0" failures="0" errors="0" skipped="0" time="0" />
			""");

		// Whether an empty suite should be an error is a real question, but it
		// belongs to the runner, which knows why it is empty. The reader's job
		// is to report what the file says.
		XAssert.True(report.IsSuccess);
		XAssert.Equal(0, report.Total);
	}
}
