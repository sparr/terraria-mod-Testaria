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

	/// <summary>
	/// The test could not be run because the harness had nothing to run it
	/// in, most often an arena whose remaining space is all retained from
	/// earlier failures.
	/// <para/>
	/// Distinct from <see cref="Skipped"/>, which is a decision, and from
	/// <see cref="Errored"/>, which says something about the test. This says
	/// only that the test never got its turn, and nothing at all about
	/// whether it would have passed. It fails the run regardless: a suite
	/// that silently stopped running part of itself must not report success.
	/// </summary>
	Blocked,
}
