namespace Testaria;

/// <summary>
/// The results of one class's worth of tests. Maps to a JUnit
/// <c>&lt;testsuite&gt;</c>.
/// </summary>
public sealed record TestSuiteResult
{
	/// <summary>Suite name, conventionally the declaring type's full name.</summary>
	public required string Name { get; init; }

	/// <summary>The individual test results in this suite.</summary>
	public required IReadOnlyList<TestResult> Results { get; init; }

	/// <summary>Total number of tests, including skipped ones.</summary>
	public int Total => Results.Count;

	/// <summary>Number of tests whose assertions failed.</summary>
	public int Failures => Results.Count(r => r.Outcome == TestOutcome.Failed);

	/// <summary>Number of tests that broke rather than failed an assertion.</summary>
	public int Errors => Results.Count(r => r.Outcome == TestOutcome.Errored);

	/// <summary>Number of tests that were not run.</summary>
	public int Skipped => Results.Count(r => r.Outcome == TestOutcome.Skipped);

	/// <summary>Tests that never got a chance to run.</summary>
	public int Blocked => Results.Count(r => r.Outcome == TestOutcome.Blocked);

	/// <summary>Number of tests that ran and passed.</summary>
	public int Passed => Results.Count(r => r.Outcome == TestOutcome.Passed);

	/// <summary>Sum of the individual test durations.</summary>
	public TimeSpan Duration => new(Results.Sum(r => r.Duration.Ticks));
}

/// <summary>
/// The results of an entire run. Maps to a JUnit <c>&lt;testsuites&gt;</c>.
/// </summary>
public sealed record TestRunResult
{
	/// <summary>Run name, conventionally the mod or assembly under test.</summary>
	public required string Name { get; init; }

	/// <summary>The suites in this run.</summary>
	public required IReadOnlyList<TestSuiteResult> Suites { get; init; }

	/// <summary>Total number of tests across all suites.</summary>
	public int Total => Suites.Sum(s => s.Total);

	/// <summary>Total assertion failures across all suites.</summary>
	public int Failures => Suites.Sum(s => s.Failures);

	/// <summary>Total errors across all suites.</summary>
	public int Errors => Suites.Sum(s => s.Errors);

	/// <summary>Total skipped tests across all suites.</summary>
	public int Skipped => Suites.Sum(s => s.Skipped);

	/// <summary>Tests that never got a chance to run.</summary>
	public int Blocked => Suites.Sum(s => s.Blocked);

	/// <summary>Total passing tests across all suites.</summary>
	public int Passed => Suites.Sum(s => s.Passed);

	/// <summary>Sum of all suite durations.</summary>
	public TimeSpan Duration => new(Suites.Sum(s => s.Duration.Ticks));

	/// <summary>
	/// True when nothing failed and nothing errored. This is what a runner
	/// should map to its process exit code.
	/// </summary>
	/// <para/>
	/// Blocked tests count against it. They say nothing about the subject, but
	/// a run that quietly stopped running part of itself has not established
	/// what it was asked to establish, and reporting success would be a lie of
	/// omission.
	public bool IsSuccess => Failures == 0 && Errors == 0 && Blocked == 0;

	/// <summary>
	/// Groups a flat sequence of results into suites by
	/// <see cref="TestResult.ClassName"/>, preserving first-seen order for both
	/// suites and the tests within them so that a report reads in run order.
	/// </summary>
	public static TestRunResult FromResults(string name, IEnumerable<TestResult> results)
	{
		ArgumentNullException.ThrowIfNull(results);

		List<TestSuiteResult> suites = results
			.GroupBy(r => r.ClassName, StringComparer.Ordinal)
			.Select(g => new TestSuiteResult { Name = g.Key, Results = g.ToList() })
			.ToList();

		return new TestRunResult { Name = name, Suites = suites };
	}
}
