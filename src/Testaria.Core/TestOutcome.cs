namespace Testaria;

/// <summary>
/// How a single test finished.
/// </summary>
public enum TestOutcome
{
	/// <summary>The test ran to completion and every assertion held.</summary>
	Passed,

	/// <summary>An assertion failed. The test itself worked correctly.</summary>
	Failed,

	/// <summary>
	/// The test threw something that was not an assertion failure, or its
	/// harness could not run it. Distinguished from <see cref="Failed"/>
	/// because it usually indicates a broken test rather than a broken subject.
	/// </summary>
	Errored,

	/// <summary>The test was deliberately not run.</summary>
	Skipped,
}
