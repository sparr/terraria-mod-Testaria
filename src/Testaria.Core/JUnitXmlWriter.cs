using System.Globalization;
using System.Text;
using System.Xml;

namespace Testaria;

/// <summary>
/// Renders a <see cref="TestRunResult"/> as JUnit XML.
/// <para/>
/// JUnit XML rather than TRX because it is the one format GitHub Actions,
/// GitLab, Jenkins, and TeamCity all read without a custom reporter. The
/// in-game runner cannot be a VSTest host, so emitting the universal report
/// format is what makes an in-game run legible to CI at all.
/// </summary>
public static class JUnitXmlWriter
{
	/// <summary>Renders the run as a JUnit XML document.</summary>
	/// <param name="run">The run to render.</param>
	/// <param name="timestamp">
	/// Timestamp recorded on each suite. Injectable so that output is
	/// reproducible under test; defaults to now.
	/// </param>
	public static string ToXml(TestRunResult run, DateTimeOffset? timestamp = null)
	{
		ArgumentNullException.ThrowIfNull(run);

		var output = new StringBuilder();
		var settings = new XmlWriterSettings {
			Indent = true,
			IndentChars = "  ",
			Encoding = Encoding.UTF8,
			OmitXmlDeclaration = false,
		};

		// Through a Utf8StringWriter, not the StringBuilder directly. A
		// StringWriter reports UTF-16, being a .NET string, so XmlWriter would
		// stamp encoding="utf-16" on a document that every caller then writes
		// out as UTF-8 bytes. Strict parsers reject the mismatch outright:
		// Python's ElementTree fails with "encoding specified in XML
		// declaration is incorrect".
		using (var text = new Utf8StringWriter(output))
		using (XmlWriter writer = XmlWriter.Create(text, settings))
			Write(writer, run, timestamp ?? DateTimeOffset.Now);

		return output.ToString();
	}

	private static void Write(XmlWriter writer, TestRunResult run, DateTimeOffset timestamp)
	{
		writer.WriteStartDocument();
		writer.WriteStartElement("testsuites");
		WriteCounts(writer, run.Name, run.Total, run.Failures, run.Errors, run.Skipped, run.Duration);

		foreach (TestSuiteResult suite in run.Suites) {
			writer.WriteStartElement("testsuite");
			WriteCounts(writer, suite.Name, suite.Total, suite.Failures, suite.Errors, suite.Skipped, suite.Duration);
			writer.WriteAttributeString("timestamp", timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

			foreach (TestResult result in suite.Results)
				WriteTestCase(writer, result);

			writer.WriteEndElement();
		}

		writer.WriteEndElement();
		writer.WriteEndDocument();
	}

	private static void WriteCounts(XmlWriter writer, string name, int tests, int failures, int errors, int skipped, TimeSpan duration)
	{
		writer.WriteAttributeString("name", Sanitize(name));
		writer.WriteAttributeString("tests", Int(tests));
		writer.WriteAttributeString("failures", Int(failures));
		writer.WriteAttributeString("errors", Int(errors));
		writer.WriteAttributeString("skipped", Int(skipped));
		writer.WriteAttributeString("time", Seconds(duration));
	}

	private static void WriteTestCase(XmlWriter writer, TestResult result)
	{
		writer.WriteStartElement("testcase");
		writer.WriteAttributeString("name", Sanitize(result.Name));
		writer.WriteAttributeString("classname", Sanitize(result.ClassName));
		writer.WriteAttributeString("time", Seconds(result.Duration));

		// Non-standard attributes, namespaced by prefix so that strict
		// consumers ignore them rather than choking. Both are Terraria
		// specific and have no JUnit equivalent.
		if (result.Ticks is int ticks)
			writer.WriteAttributeString("testaria-ticks", Int(ticks));

		if (result.Box is not null)
			writer.WriteAttributeString("testaria-box", Sanitize(result.Box));

		switch (result.Outcome) {
			case TestOutcome.Failed:
				WriteProblem(writer, "failure", result);
				break;

			case TestOutcome.Errored:
				WriteProblem(writer, "error", result);
				break;

			case TestOutcome.Skipped:
				writer.WriteStartElement("skipped");
				if (result.Message is not null)
					writer.WriteAttributeString("message", Sanitize(result.Message));
				writer.WriteEndElement();
				break;

			case TestOutcome.Passed:
			default:
				break;
		}

		writer.WriteEndElement();
	}

	private static void WriteProblem(XmlWriter writer, string element, TestResult result)
	{
		writer.WriteStartElement(element);
		writer.WriteAttributeString("message", Sanitize(result.Message ?? string.Empty));
		writer.WriteAttributeString("type", element == "failure" ? nameof(AssertionException) : "Exception");

		if (result.StackTrace is not null)
			writer.WriteString(Sanitize(result.StackTrace));

		writer.WriteEndElement();
	}

	private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

	private static string Seconds(TimeSpan duration)
		=> duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

	/// <summary>
	/// Strips characters that are not legal in XML 1.0. Assertion messages can
	/// carry arbitrary text out of the game, and a single stray control
	/// character would otherwise produce a document that every CI parser
	/// rejects, turning a readable test failure into an unreadable one.
	/// </summary>
	private static string Sanitize(string value)
	{
		if (!NeedsSanitizing(value))
			return value;

		var builder = new StringBuilder(value.Length);

		for (int i = 0; i < value.Length; i++) {
			char c = value[i];

			// A well formed surrogate pair encodes a character above U+FFFF,
			// which is legal. Copy both halves and skip past them, so that
			// astral characters survive instead of being torn in half.
			if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) {
				builder.Append(c).Append(value[i + 1]);
				i++;
				continue;
			}

			if (IsLegalXmlChar(c))
				builder.Append(c);
		}

		return builder.ToString();
	}

	private static bool NeedsSanitizing(string value)
	{
		for (int i = 0; i < value.Length; i++) {
			char c = value[i];

			if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) {
				i++;
				continue;
			}

			if (!IsLegalXmlChar(c))
				return true;
		}

		return false;
	}

	/// <summary>
	/// XML 1.0 legal characters, excluding the surrogate range, which is
	/// handled as pairs by the callers above.
	/// </summary>
	/// <summary>
	/// A <see cref="StringWriter"/> that claims UTF-8, so the XML declaration
	/// matches the bytes the document is eventually written as.
	/// </summary>
	private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder)
	{
		public override Encoding Encoding => Encoding.UTF8;
	}

	private static bool IsLegalXmlChar(char c)
		=> c is '\t' or '\n' or '\r'
			|| (c >= '\u0020' && c <= '\uD7FF')
			|| (c >= '\uE000' && c <= '\uFFFD');
}
