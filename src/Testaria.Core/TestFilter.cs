namespace Testaria;

/// <summary>
/// Narrows a run to some of the discovered tests.
/// <para/>
/// Filtered-out tests are *omitted* rather than reported as skipped. A skip
/// says the suite tried and could not; a filter says an operator deliberately
/// asked for less, and three hundred skip entries would bury the four results
/// they actually wanted. The count that was held back is reported instead, so
/// a narrowed run never looks like a complete one.
/// </summary>
public sealed class TestFilter
{
	private readonly string? pattern;

	private TestFilter(string? pattern) => this.pattern = pattern;

	/// <summary>Matches every test.</summary>
	public static TestFilter All { get; } = new(null);

	/// <summary>True when this filter narrows anything.</summary>
	public bool IsNarrowing => pattern is not null;

	/// <summary>How this filter reads in a report.</summary>
	public override string ToString() => pattern ?? "(all)";

	/// <summary>
	/// Builds a filter from a pattern.
	/// <para/>
	/// Matched case-insensitively against both the test's name and its
	/// <c>Class.Name</c>, so <c>Zombie</c>, <c>*Zombie*</c> and
	/// <c>MyMod.Tests.NpcTests.Zombie_dies</c> all work. <c>*</c> matches any
	/// run of characters; a pattern with no <c>*</c> is treated as a substring,
	/// since that is what someone typing a fragment means.
	/// </summary>
	public static TestFilter Parse(string? pattern)
		=> string.IsNullOrWhiteSpace(pattern) ? All : new TestFilter(pattern.Trim());

	/// <summary>Whether a test should run.</summary>
	public bool Matches(TestCase test)
	{
		ArgumentNullException.ThrowIfNull(test);

		if (pattern is null)
			return true;

		return MatchesText(test.Name) || MatchesText($"{test.ClassName}.{test.Name}");
	}

	private bool MatchesText(string text)
	{
		if (!pattern!.Contains('*'))
			return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);

		return Glob(pattern, 0, text, 0);
	}

	/// <summary>
	/// Wildcard match, iterative on the pattern and recursive only at a
	/// <c>*</c>, so a pathological pattern cannot blow the stack.
	/// </summary>
	private static bool Glob(string pattern, int p, string text, int t)
	{
		while (p < pattern.Length) {
			if (pattern[p] == '*') {
				// Collapse runs of stars; each is equivalent to one.
				while (p < pattern.Length && pattern[p] == '*')
					p++;

				if (p == pattern.Length)
					return true;

				for (int i = t; i <= text.Length; i++) {
					if (Glob(pattern, p, text, i))
						return true;
				}

				return false;
			}

			if (t >= text.Length || char.ToUpperInvariant(pattern[p]) != char.ToUpperInvariant(text[t]))
				return false;

			p++;
			t++;
		}

		return t == text.Length;
	}
}
