namespace Testaria;

/// <summary>
/// Thrown when an assertion fails. Distinguished from every other exception so
/// that a runner can report an honest assertion failure separately from a test
/// that simply broke, which is the difference between
/// <see cref="TestOutcome.Failed"/> and <see cref="TestOutcome.Errored"/>.
/// </summary>
public class AssertionException : Exception
{
	/// <summary>Creates an assertion failure with the given message.</summary>
	public AssertionException(string message) : base(message) { }

	/// <summary>Creates an assertion failure wrapping an underlying cause.</summary>
	public AssertionException(string message, Exception inner) : base(message, inner) { }
}
