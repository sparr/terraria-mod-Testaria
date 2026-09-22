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
	/// <summary>Column header, written as the first line.</summary>
	public const string Header = "tier\tfreshWorld\tclassName\tname";

	/// <summary>Renders the catalogue, header first, one test per line.</summary>
	public static string ToTsv(IEnumerable<TestCase> tests)
	{
		ArgumentNullException.ThrowIfNull(tests);

		var builder = new StringBuilder();
		builder.Append(Header).Append('\n');

		foreach (TestCase test in tests) {
			builder
				.Append(test.Tier.ToString()).Append('\t')
				.Append(test.FreshWorld ? "yes" : "no").Append('\t')
				.Append(Clean(test.ClassName)).Append('\t')
				.Append(Clean(test.Name)).Append('\n');
		}

		return builder.ToString();
	}

	/// <summary>A one-line summary, for a human reading a console.</summary>
	public static string Summarize(IEnumerable<TestCase> tests)
	{
		List<TestCase> all = [.. tests];
		string byTier = string.Join(", ", all
			.GroupBy(t => t.Tier)
			.OrderBy(g => g.Key)
			.Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));

		int fresh = all.Count(t => t.FreshWorld);
		string freshNote = fresh > 0 ? $", {fresh} wanting a fresh world" : string.Empty;

		return all.Count == 0
			? "no tests"
			: string.Create(CultureInfo.InvariantCulture, $"{all.Count} test(s): {byTier}{freshNote}");
	}

	// A tab or newline would break the format. Neither can occur in a C#
	// identifier, so this only guards against a caller inventing names.
	private static string Clean(string value) => value.Replace('\t', ' ').Replace('\n', ' ');
}
