using System.Text.RegularExpressions;

namespace Testaria;

/// <summary>
/// Narrows a run to some of the discovered tests, by regular expression.
/// <para/>
/// Regex rather than a bespoke glob. It is a language developers already know,
/// it brings alternation and exclusion, which are what test selection actually
/// wants, and it means no hand-written matcher to get subtly wrong. A bare
/// fragment still behaves as expected, because an unanchored regex search is
/// exactly a substring match:
/// <list type="bullet">
/// <item><description><c>Zombie</c> matches anything containing it.</description></item>
/// <item><description><c>Zombie|Skeleton</c> matches either.</description></item>
/// <item><description><c>^MyMod\.Tests\.NpcTests</c> anchors to one class.</description></item>
/// <item><description><c>^(?!.*Slow)</c> runs everything except the slow ones.</description></item>
/// </list>
/// <para/>
/// A name is not a pattern. Namespaces contain <c>.</c> and nested types
/// contain <c>+</c>, both of which mean something else to a regex, so a name
/// copied out of the catalogue should be escaped before being used as an exact
/// filter. The <c>.</c> is harmless in practice, since it still matches the
/// character it stands for; the <c>+</c> is not, and quietly matches nothing.
/// <para/>
/// Filtered-out tests are omitted rather than reported as skipped. A skip says
/// the suite tried and could not; a filter says an operator deliberately asked
/// for less, and three hundred skip entries would bury the handful of results
/// they wanted. The count held back is reported instead, so a narrowed run
/// never looks like a complete one.
/// </summary>
public sealed class TestFilter
{
	/// <summary>
	/// Cap on a single match. Generous for any sane pattern, and the reason a
	/// pathological one cannot hang a run. A timeout is used rather than
	/// non-backtracking matching so that lookarounds keep working, since
	/// excluding tests is a common enough reason to reach for a filter.
	/// </summary>
	private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

	private readonly Regex? regex;
	private readonly string? pattern;

	private TestFilter(Regex? regex, string? pattern)
	{
		this.regex = regex;
		this.pattern = pattern;
	}

	/// <summary>Matches every test.</summary>
	public static TestFilter All { get; } = new(null, null);

	/// <summary>True when this filter narrows anything.</summary>
	public bool IsNarrowing => regex is not null;

	/// <summary>How this filter reads in a report.</summary>
	public override string ToString() => pattern ?? "(all)";

	/// <summary>
	/// Builds a filter from a regular expression, matched case-insensitively
	/// against both the test's name and its <c>Class.Name</c>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// The pattern is not a valid regular expression. Reported rather than
	/// silently matching nothing, which would look like a suite with no tests.
	/// </exception>
	public static TestFilter Parse(string? pattern)
	{
		if (string.IsNullOrWhiteSpace(pattern))
			return All;

		string trimmed = pattern.Trim();

		try {
			var regex = new Regex(trimmed, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

			return new TestFilter(regex, trimmed);
		}
		catch (ArgumentException ex) {
			throw new ArgumentException($"'{trimmed}' is not a valid regular expression: {ex.Message}", nameof(pattern), ex);
		}
	}

	/// <summary>Whether a test should run.</summary>
	public bool Matches(TestCase test)
	{
		ArgumentNullException.ThrowIfNull(test);

		if (regex is null)
			return true;

		try {
			return regex.IsMatch(test.Name) || regex.IsMatch($"{test.ClassName}.{test.Name}");
		}
		catch (RegexMatchTimeoutException) {
			// Excluding the test would quietly shrink the suite, so treat a
			// pattern too slow to evaluate as matching nothing loudly instead.
			throw new InvalidOperationException(
				$"The filter '{pattern}' took longer than {MatchTimeout.TotalSeconds:0} second(s) to evaluate against '{test.ClassName}.{test.Name}'. Simplify it.");
		}
	}
}
