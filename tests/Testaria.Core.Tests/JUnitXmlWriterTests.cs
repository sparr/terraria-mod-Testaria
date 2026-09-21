using System.Xml;
using System.Xml.Linq;

namespace Testaria.Tests;

public class JUnitXmlWriterTests
{
	private static readonly DateTimeOffset FixedStamp =
		new(2026, 9, 21, 14, 30, 0, TimeSpan.Zero);

	private static XElement Render(params TestResult[] results)
		=> XDocument.Parse(JUnitXmlWriter.ToXml(TestRunResult.FromResults("run", results), FixedStamp)).Root!;

	[Fact]
	public void Output_is_a_well_formed_document_rooted_at_testsuites()
	{
		XElement root = Render(TestResult.Pass("A", "x"));

		XAssert.Equal("testsuites", root.Name.LocalName);
		XAssert.Single(root.Elements("testsuite"));
	}

	[Fact]
	public void Root_carries_aggregate_counts()
	{
		XElement root = Render(
			TestResult.Pass("A", "passing"),
			TestResult.Fail("A", "failing", "nope"),
			TestResult.Skip("B", "skipping", "not yet"));

		XAssert.Equal("3", root.Attribute("tests")!.Value);
		XAssert.Equal("1", root.Attribute("failures")!.Value);
		XAssert.Equal("0", root.Attribute("errors")!.Value);
		XAssert.Equal("1", root.Attribute("skipped")!.Value);
	}

	[Fact]
	public void Each_class_becomes_a_testsuite_with_its_own_counts()
	{
		XElement root = Render(
			TestResult.Pass("A", "one"),
			TestResult.Fail("B", "two", "nope"));

		List<XElement> suites = root.Elements("testsuite").ToList();

		XAssert.Equal(2, suites.Count);
		XAssert.Equal("A", suites[0].Attribute("name")!.Value);
		XAssert.Equal("0", suites[0].Attribute("failures")!.Value);
		XAssert.Equal("B", suites[1].Attribute("name")!.Value);
		XAssert.Equal("1", suites[1].Attribute("failures")!.Value);
	}

	[Fact]
	public void A_passing_testcase_has_no_child_elements()
	{
		XElement testcase = Render(TestResult.Pass("A", "x")).Descendants("testcase").Single();

		XAssert.Equal("x", testcase.Attribute("name")!.Value);
		XAssert.Equal("A", testcase.Attribute("classname")!.Value);
		XAssert.Empty(testcase.Elements());
	}

	[Fact]
	public void A_failure_carries_message_type_and_stack_trace()
	{
		XElement failure = Render(
			TestResult.Fail("A", "x", "expected 5 got 3", "at Foo.Bar()"))
			.Descendants("failure").Single();

		XAssert.Equal("expected 5 got 3", failure.Attribute("message")!.Value);
		XAssert.Equal(nameof(AssertionException), failure.Attribute("type")!.Value);
		XAssert.Contains("at Foo.Bar()", failure.Value);
	}

	[Fact]
	public void An_error_is_reported_separately_from_a_failure()
	{
		// The distinction matters: a failure means the subject is broken, an
		// error usually means the test is.
		XElement root = Render(new TestResult {
			ClassName = "A", Name = "x", Outcome = TestOutcome.Errored, Message = "NullReference",
		});

		XAssert.Empty(root.Descendants("failure"));
		XAssert.Equal("NullReference", root.Descendants("error").Single().Attribute("message")!.Value);
	}

	[Fact]
	public void A_skipped_test_carries_its_reason()
	{
		XElement skipped = Render(TestResult.Skip("A", "x", "needs a world"))
			.Descendants("skipped").Single();

		XAssert.Equal("needs a world", skipped.Attribute("message")!.Value);
	}

	[Fact]
	public void Durations_are_seconds_with_three_decimals_in_invariant_format()
	{
		XElement testcase = Render(TestResult.Pass("A", "x", TimeSpan.FromMilliseconds(1500)))
			.Descendants("testcase").Single();

		// Must be "1.500" regardless of the ambient culture's decimal separator,
		// or CI parsers reject it.
		XAssert.Equal("1.500", testcase.Attribute("time")!.Value);
	}

	[Fact]
	public void Tick_counts_and_box_coordinates_are_emitted_when_present()
	{
		XElement testcase = Render(new TestResult {
			ClassName = "A", Name = "x", Outcome = TestOutcome.Passed, Ticks = 340, Box = "100,200,80,48",
		}).Descendants("testcase").Single();

		XAssert.Equal("340", testcase.Attribute("testaria-ticks")!.Value);
		XAssert.Equal("100,200,80,48", testcase.Attribute("testaria-box")!.Value);
	}

	[Fact]
	public void Tick_counts_and_box_coordinates_are_omitted_when_absent()
	{
		XElement testcase = Render(TestResult.Pass("A", "x")).Descendants("testcase").Single();

		XAssert.Null(testcase.Attribute("testaria-ticks"));
		XAssert.Null(testcase.Attribute("testaria-box"));
	}

	[Fact]
	public void Xml_special_characters_in_messages_are_escaped_not_mangled()
	{
		XElement failure = Render(
			TestResult.Fail("A", "x", "expected <tag> & \"quotes\""))
			.Descendants("failure").Single();

		XAssert.Equal("expected <tag> & \"quotes\"", failure.Attribute("message")!.Value);
	}

	[Fact]
	public void Illegal_control_characters_are_stripped_rather_than_producing_invalid_xml()
	{
		// A single stray control character would otherwise make the whole report
		// unparseable, turning a readable failure into no report at all.
		XElement failure = Render(TestResult.Fail("A", "x", "before\u0000after"))
			.Descendants("failure").Single();

		XAssert.Equal("beforeafter", failure.Attribute("message")!.Value);
	}

	[Fact]
	public void Legal_whitespace_control_characters_survive()
	{
		XElement failure = Render(TestResult.Fail("A", "x", "line one\nline two\ttabbed"))
			.Descendants("failure").Single();

		XAssert.Contains("line two", failure.Attribute("message")!.Value);
	}

	[Fact]
	public void Astral_characters_survive_as_intact_surrogate_pairs()
	{
		// Naive char-by-char filtering would tear a surrogate pair in half and
		// produce invalid XML, which is worse than the problem it solves.
		XElement failure = Render(TestResult.Fail("A", "x", "boss died \U0001F480"))
			.Descendants("failure").Single();

		XAssert.Equal("boss died \U0001F480", failure.Attribute("message")!.Value);
	}

	[Fact]
	public void A_lone_surrogate_is_stripped_rather_than_throwing()
	{
		XElement failure = Render(TestResult.Fail("A", "x", "bad \ud800 half"))
			.Descendants("failure").Single();

		XAssert.Equal("bad  half", failure.Attribute("message")!.Value);
	}

	[Fact]
	public void Timestamp_is_injectable_so_output_is_reproducible()
	{
		string first = JUnitXmlWriter.ToXml(TestRunResult.FromResults("r", [TestResult.Pass("A", "x")]), FixedStamp);
		string second = JUnitXmlWriter.ToXml(TestRunResult.FromResults("r", [TestResult.Pass("A", "x")]), FixedStamp);

		XAssert.Equal(first, second);
		XAssert.Contains("2026-09-21T14:30:00", first);
	}

	[Fact]
	public void An_empty_run_still_produces_a_valid_document()
	{
		XElement root = XDocument.Parse(
			JUnitXmlWriter.ToXml(TestRunResult.FromResults("empty", []), FixedStamp)).Root!;

		XAssert.Equal("testsuites", root.Name.LocalName);
		XAssert.Equal("0", root.Attribute("tests")!.Value);
		XAssert.Empty(root.Elements());
	}

	[Fact]
	public void Output_parses_under_a_strict_conformance_reader()
	{
		string xml = JUnitXmlWriter.ToXml(TestRunResult.FromResults("run", [
			TestResult.Pass("A", "one"),
			TestResult.Fail("A", "two", "nope", "at X()"),
			TestResult.Skip("B", "three", "later"),
		]), FixedStamp);

		using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
		while (reader.Read()) { }
	}
}
