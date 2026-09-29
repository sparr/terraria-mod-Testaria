using System.Globalization;
using System.Text;

namespace Testaria;

/// <summary>
/// Renders the discovered tests as a table a harness can read.
/// <para/>
/// Listing without running is what lets a harness plan: chiefly, run a
/// dedicated process per test that asked for a fresh world, which is the only
/// honest way to give it one.
/// <para/>
/// Tab separated rather than JSON so that a shell script can consume it with
/// <c>cut</c> and nothing else, and because no field here can contain a tab.
/// </summary>
public static class TestCatalog
{
	/// <summary>
	/// Column header, written as the first line.
	/// <para/>
	/// Columns are appended rather than reordered, because the readers index by
	/// position: <c>scripts/run-fresh.sh</c> takes 2, 3 and 4 by number.
	/// </summary>
	public const string Header = "tier\tfreshWorld\tclassName\tname\trequiresMods\trequiresModsAbsent";

	/// <summary>
	/// Renders the catalogue, header first, one test per line.
	/// <para/>
	/// The "fresh world" column answers what a harness needs to know, which is
	/// whether this test wants a process to itself, so it has to account for the
	/// run's isolation switch as well as the attributes. A
	/// <c>[MutatesGlobalState]</c> test wants its own world only when the run
	/// asked for that, and a catalogue that ignored the switch would have the
	/// harness put every such test in one shared world while the switch claimed
	/// otherwise.
	/// </summary>
	/// <param name="tests">The tests to list.</param>
	/// <param name="isolateMutatingTests">
	/// Whether the run gives every <c>[MutatesGlobalState]</c> test a world of
	/// its own. Matches <see cref="TestRunnerOptions.IsolateMutatingTests"/>.
	/// </param>
	public static string ToTsv(IEnumerable<TestCase> tests, bool isolateMutatingTests = false)
	{
		ArgumentNullException.ThrowIfNull(tests);

		var builder = new StringBuilder();
		builder.Append(Header).Append('\n');

		foreach (TestCase test in tests) {
			builder
				.Append(test.Tier.ToString()).Append('\t')
				.Append(test.NeedsOwnWorld(isolateMutatingTests) ? "yes" : "no").Append('\t')
				.Append(Clean(test.ClassName)).Append('\t')
				.Append(Clean(test.Name)).Append('\t')
				.Append(Clean(string.Join(",", test.RequiresMods))).Append('\t')
				.Append(Clean(string.Join(",", test.RequiresModsAbsent))).Append('\n');
		}

		return builder.ToString();
	}

	/// <summary>A one-line summary, for a human reading a console.</summary>
	/// <inheritdoc cref="ToTsv" path="/param"/>
	public static string Summarize(IEnumerable<TestCase> tests, bool isolateMutatingTests = false)
	{
		List<TestCase> all = [.. tests];
		string byTier = string.Join(", ", all
			.GroupBy(t => t.Tier)
			.OrderBy(g => g.Key)
			.Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));

		int fresh = all.Count(t => t.NeedsOwnWorld(isolateMutatingTests));
		string freshNote = fresh > 0 ? $", {fresh} wanting a fresh world" : string.Empty;

		return all.Count == 0
			? "no tests"
			: string.Create(CultureInfo.InvariantCulture, $"{all.Count} test(s): {byTier}{freshNote}");
	}

	// A tab or newline would break the format. Neither can occur in a C#
	// identifier, so this only guards against a caller inventing names.
	private static string Clean(string value) => value.Replace('\t', ' ').Replace('\n', ' ');
}
