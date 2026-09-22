using System.Collections;
using System.Reflection;

namespace Testaria;

/// <summary>Whether a test body runs to completion at once or across ticks.</summary>
public enum TestBodyKind
{
	/// <summary>Returns void. Runs within a single tick.</summary>
	Immediate,

	/// <summary>
	/// Returns <see cref="IEnumerator"/> and is driven a step per tick. The
	/// natural shape for gameplay assertions, which are inherently "over N
	/// ticks" rather than "the return value of this call".
	/// </summary>
	Coroutine,
}

/// <summary>One discovered test, ready to run.</summary>
public sealed record TestCase
{
	/// <summary>The test method.</summary>
	public required MethodInfo Method { get; init; }

	/// <summary>
	/// Arguments for this case, in parameter order and excluding the context.
	/// Empty for an ordinary test.
	/// </summary>
	public IReadOnlyList<object?> Arguments { get; init; } = [];

	/// <summary>
	/// The method's name, with the case's arguments appended when it has any.
	/// <para/>
	/// Each case is named rather than numbered, so a report says which case
	/// failed rather than merely that one did, and so a filter can single one
	/// out.
	/// </summary>
	public string Name => Method.Name + TestCaseLabel.For(Arguments);

	/// <summary>The declaring type's full name, as reported.</summary>
	public string ClassName => Method.DeclaringType?.FullName ?? "<unknown>";

	/// <summary>The environment this test needs.</summary>
	public required TestTier Tier { get; init; }

	/// <summary>Whether the body is immediate or a coroutine.</summary>
	public required TestBodyKind BodyKind { get; init; }

	/// <summary>True when the method takes an <see cref="ITestContext"/>.</summary>
	public required bool WantsContext { get; init; }

	/// <summary>Set when the test is to be reported as skipped rather than run.</summary>
	public string? SkipReason { get; init; }

	/// <summary>Tick budget, or zero for the runner's default.</summary>
	public int TimeoutTicks { get; init; }

	/// <summary>The box this test needs, or null for tiers that use none.</summary>
	public BoxRequest? Box { get; init; }

	/// <summary>True when the test asked for a fresh world instead of a box.</summary>
	public bool FreshWorld { get; init; }
}

/// <summary>
/// A method that looks like a test but cannot be run.
/// <para/>
/// Reported rather than skipped silently. A malformed test that vanishes from
/// the suite is worse than one that fails, because the suite still goes green.
/// </summary>
public sealed record TestDiscoveryError
{
	/// <summary>Where the problem is.</summary>
	public required string Location { get; init; }

	/// <summary>What is wrong, and what to do about it.</summary>
	public required string Message { get; init; }

	/// <summary>Renders this error as a reportable errored result.</summary>
	public TestResult ToResult()
	{
		int split = Location.LastIndexOf('.');
		string className = split > 0 ? Location[..split] : Location;
		string name = split > 0 ? Location[(split + 1)..] : Location;

		return new TestResult {
			ClassName = className,
			Name = name,
			Outcome = TestOutcome.Errored,
			Message = Message,
		};
	}
}

/// <summary>The outcome of scanning for tests.</summary>
public sealed record DiscoveryResult
{
	/// <summary>Tests that can be run.</summary>
	public required IReadOnlyList<TestCase> Tests { get; init; }

	/// <summary>Methods that look like tests but are malformed.</summary>
	public required IReadOnlyList<TestDiscoveryError> Errors { get; init; }
}
