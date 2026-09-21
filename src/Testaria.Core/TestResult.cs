namespace Testaria;

/// <summary>
/// The outcome of one test, in a form that survives the trip out of the game
/// process and into a report.
/// </summary>
public sealed record TestResult
{
	/// <summary>The test method's name.</summary>
	public required string Name { get; init; }

	/// <summary>
	/// The declaring type's full name. Reported as the JUnit "classname",
	/// which is what most CI systems group by.
	/// </summary>
	public required string ClassName { get; init; }

	/// <summary>How the test finished.</summary>
	public required TestOutcome Outcome { get; init; }

	/// <summary>Wall clock duration.</summary>
	public TimeSpan Duration { get; init; }

	/// <summary>
	/// Game ticks consumed, for tests that ran inside the tick loop. Null for
	/// Tier 0 tests, which have no tick loop to consume.
	/// </summary>
	public int? Ticks { get; init; }

	/// <summary>
	/// Why the test did not pass, or why it was skipped. Null when
	/// <see cref="Outcome"/> is <see cref="TestOutcome.Passed"/>.
	/// </summary>
	public string? Message { get; init; }

	/// <summary>Stack trace for a failure or error, when one is available.</summary>
	public string? StackTrace { get; init; }

	/// <summary>
	/// Where in the world the test ran, as "x,y,width,height" in tile
	/// coordinates. Null for tests that used no box. Present so that a failure
	/// report can tell an author where to fly to look at the wreckage.
	/// </summary>
	public string? Box { get; init; }

	/// <summary>True when the test finished without a failure or an error.</summary>
	public bool IsSuccess => Outcome is TestOutcome.Passed or TestOutcome.Skipped;

	/// <summary>Convenience constructor for a passing result.</summary>
	public static TestResult Pass(string className, string name, TimeSpan duration = default)
		=> new() { ClassName = className, Name = name, Outcome = TestOutcome.Passed, Duration = duration };

	/// <summary>Convenience constructor for a failing result.</summary>
	public static TestResult Fail(string className, string name, string message, string? stackTrace = null, TimeSpan duration = default)
		=> new() { ClassName = className, Name = name, Outcome = TestOutcome.Failed, Message = message, StackTrace = stackTrace, Duration = duration };

	/// <summary>Convenience constructor for a skipped result.</summary>
	public static TestResult Skip(string className, string name, string reason)
		=> new() { ClassName = className, Name = name, Outcome = TestOutcome.Skipped, Message = reason };
}
