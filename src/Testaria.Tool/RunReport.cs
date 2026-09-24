using System.Globalization;
using System.Xml.Linq;

namespace Testaria.Tool;

/// <summary>One test that did not pass, as the report recorded it.</summary>
/// <param name="Kind">"failure" or "error", as JUnit names them.</param>
/// <param name="ClassName">The test's declaring type.</param>
/// <param name="Name">The test's name, including its case arguments.</param>
/// <param name="Message">Everything the runner said about it.</param>
/// <param name="Blocked">True when the test never ran at all.</param>
public sealed record Problem(string Kind, string ClassName, string Name, string Message, bool Blocked);

/// <summary>
/// The verdict of a run, read back out of the JUnit XML the game wrote.
/// <para/>
/// Read from the report rather than from the game's console output on purpose:
/// the report is the artifact CI consumes, so a harness that agreed with the
/// console and disagreed with the report would be reporting a verdict nobody
/// else can see.
/// </summary>
public sealed record RunReport
{
	/// <summary>How many tests the report covers.</summary>
	public int Total { get; init; }

	/// <summary>Tests that failed an assertion or ran out of ticks.</summary>
	public int Failures { get; init; }

	/// <summary>Tests that could not run or threw something that was not an assertion.</summary>
	public int Errors { get; init; }

	/// <summary>Tests the harness could not find room for, counted apart from other errors.</summary>
	public int Blocked { get; init; }

	/// <summary>Tests reported as skipped.</summary>
	public int Skipped { get; init; }

	/// <summary>Tests that passed.</summary>
	public int Passed => Total - Failures - Errors - Blocked - Skipped;

	/// <summary>
	/// Tests that actually ran, which is everything that was not skipped.
	/// <para/>
	/// The number worth guarding in CI. A suite whose subject went missing
	/// skips every test and reports a clean run, which looks exactly like a
	/// suite that passed.
	/// </summary>
	public int Ran => Total - Skipped;

	/// <summary>Everything that did not pass, in report order.</summary>
	public IReadOnlyList<Problem> Problems { get; init; } = [];

	/// <summary>
	/// True when the run established what it set out to.
	/// <para/>
	/// Blocked counts against it: a test that never ran has said nothing about
	/// its subject, and a run that quietly dropped part of itself has not
	/// proved what it claims to have proved.
	/// </summary>
	public bool IsSuccess => Failures == 0 && Errors == 0 && Blocked == 0;

	/// <summary>Reads a JUnit XML report.</summary>
	public static RunReport Read(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);

		return Parse(XDocument.Load(path).Root
			?? throw new InvalidDataException($"'{path}' has no root element."));
	}

	/// <summary>Reads a JUnit XML report from text, which is what the tests do.</summary>
	public static RunReport Parse(string xml)
		=> Parse(XDocument.Parse(xml).Root
			?? throw new InvalidDataException("The report has no root element."));

	private static RunReport Parse(XElement root)
	{
		List<Problem> problems = [];
		int blocked = 0;

		foreach (XElement test in root.Descendants("testcase")) {
			foreach (XElement bad in test.Elements()) {
				if (bad.Name.LocalName is not ("failure" or "error"))
					continue;

				// Blocked tests are written as errors so that CI reaches the
				// same verdict the runner did, and are told apart by the type
				// attribute rather than by reading the message.
				bool isBlocked = (string?)bad.Attribute("type") == "Testaria.Blocked";

				if (isBlocked)
					blocked++;

				problems.Add(new Problem(
					bad.Name.LocalName,
					(string?)test.Attribute("classname") ?? "<unknown>",
					(string?)test.Attribute("name") ?? "<unknown>",
					(string?)bad.Attribute("message") ?? "(no message)",
					isBlocked));
			}
		}

		int errors = Count(root, "errors");

		return new RunReport {
			Total = Count(root, "tests"),
			Failures = Count(root, "failures"),
			// The report's own errors count includes the blocked ones, so they
			// are subtracted here rather than counted twice.
			Errors = errors - blocked,
			Blocked = blocked,
			Skipped = Count(root, "skipped"),
			Problems = problems,
		};
	}

	/// <summary>The one-line verdict, in the same shape the shell harness prints.</summary>
	public string Summarize()
	{
		string summary = $"{Total} tests: {Passed} passed, {Failures} failed, {Errors} errored";

		if (Blocked > 0)
			summary += $", {Blocked} blocked";

		return summary + $", {Skipped} skipped";
	}

	private static int Count(XElement root, string attribute)
		=> int.TryParse((string?)root.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
			? value
			: 0;
}
