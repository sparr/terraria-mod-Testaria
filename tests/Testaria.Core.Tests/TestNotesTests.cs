using System.Xml.Linq;

namespace Testaria.Tests;

/// <summary>
/// Notes are the channel for things a test noticed that are not verdicts: an
/// NPC that left its box, a box that had to be recycled twice. They belong on
/// the result because they explain whatever odd thing happens to a neighbour
/// afterwards, and nowhere else would a person ever see them.
/// </summary>
public class TestNotesTests
{
	private static WorldGeometry Small() => new(4200, 1200, 87, 250, 400, 1000);

	private static TestResult Run(string name, ITestContext context)
	{
		DiscoveryResult all = TestDiscovery.Discover([typeof(Fixtures)]);
		DiscoveryResult one = new() {
			Tests = [.. all.Tests.Where(t => t.Name == name)],
			Errors = [],
		};
		XAssert.Single(one.Tests);

		return new TestRunner(one, new TestRunnerOptions {
			MaxTier = TestTier.World,
			Arena = new Arena(Small()),
			CreateContext = _ => context,
		}).RunToCompletion().Suites.Single().Results.Single();
	}

	[Fact]
	public void Notes_are_carried_onto_the_result()
	{
		TestResult result = Run(nameof(Fixtures.Passes), new Noted("NPC 3 left the box at tick 40"));

		XAssert.Contains("NPC 3 left the box", result.Output);
	}

	[Fact]
	public void Notes_do_not_change_the_verdict()
	{
		// An entity leaving its box is not the test's fault, and often is
		// exactly what the test was watching for.
		TestResult result = Run(nameof(Fixtures.Passes), new Noted("something wandered off"));

		XAssert.Equal(TestOutcome.Passed, result.Outcome);
	}

	[Fact]
	public void Notes_survive_alongside_a_failure()
	{
		TestResult result = Run(nameof(Fixtures.Fails), new Noted("its target had wandered off"));

		XAssert.Equal(TestOutcome.Failed, result.Outcome);
		XAssert.Equal("deliberate", result.Message);
		XAssert.Contains("wandered off", result.Output);
	}

	[Fact]
	public void A_boxed_test_that_never_asks_for_its_context_still_gets_its_notes()
	{
		// Asking for the parameter is a convenience for the test body, not a
		// declaration of interest in box watching.
		XAssert.False(TestDiscovery.Discover([typeof(Fixtures)])
			.Tests.Single(t => t.Name == nameof(Fixtures.Passes)).WantsContext);

		XAssert.Contains("wandered off", Run(nameof(Fixtures.Passes), new Noted("wandered off")).Output);
	}

	[Fact]
	public void A_test_that_does_ask_for_its_context_is_handed_the_same_one()
	{
		var context = new Noted("wandered off");

		XAssert.Equal(TestOutcome.Passed, Run(nameof(Fixtures.Inspects), context).Outcome);
		XAssert.Same(context, Fixtures.Seen);
		XAssert.Contains("wandered off", Run(nameof(Fixtures.Inspects), context).Output);
	}

	[Fact]
	public void A_context_with_nothing_to_say_produces_no_output()
		=> XAssert.Null(Run(nameof(Fixtures.Passes), new Noted()).Output);

	[Fact]
	public void A_context_that_does_not_take_notes_at_all_produces_no_output()
		=> XAssert.Null(Run(nameof(Fixtures.Passes), new Silent()).Output);

	[Fact]
	public void Several_notes_are_all_kept_in_order()
	{
		TestResult result = Run(nameof(Fixtures.Passes), new Noted("first", "second"));

		XAssert.Equal("first\nsecond", result.Output);
	}

	[Fact]
	public void Notes_reach_the_report_as_system_out()
	{
		// system-out is where CI systems already look for per-test output, so
		// notes need no reader-side support to be visible.
		TestRunResult run = TestRunResult.FromResults("run", [
			TestResult.Pass("Suite", "A") with { Output = "NPC 3 left the box" },
		]);

		XAssert.Equal("NPC 3 left the box", TestCase(run).Element("system-out")!.Value);
	}

	[Fact]
	public void A_result_with_no_output_writes_no_system_out_element()
		=> XAssert.Null(TestCase(TestRunResult.FromResults("run", [TestResult.Pass("Suite", "A")])).Element("system-out"));

	[Fact]
	public void The_failure_is_written_before_the_output()
	{
		// Whatever reads the report should meet the verdict before the
		// commentary.
		TestRunResult run = TestRunResult.FromResults("run", [
			TestResult.Fail("Suite", "A", "zombie should be dead") with { Output = "it wandered off" },
		]);

		XAssert.Equal(["failure", "system-out"], TestCase(run).Elements().Select(e => e.Name.LocalName));
	}

	[Fact]
	public void Output_is_sanitised_like_every_other_string()
	{
		// A vertical tab is legal in a C# string and illegal in XML 1.0; an
		// unsanitised one makes the whole report unparseable.
		TestRunResult run = TestRunResult.FromResults("run", [
			TestResult.Pass("Suite", "A") with { Output = $"before{(char)11}after" },
		]);

		XAssert.Equal("beforeafter", TestCase(run).Element("system-out")!.Value);
	}

	private static XElement TestCase(TestRunResult run)
		=> XDocument.Parse(JUnitXmlWriter.ToXml(run, DateTimeOffset.UnixEpoch)).Descendants("testcase").Single();

	private sealed class Noted(params string[] notes) : ITestNotes
	{
		public IReadOnlyList<string> Notes { get; } = notes;
		public TileRect Interior => default;
		public TileRect Bounds => default;
		public Band Bands => Band.Cavern;
		public int ElapsedTicks => 0;
	}

	private sealed class Silent : ITestContext
	{
		public TileRect Interior => default;
		public TileRect Bounds => default;
		public Band Bands => Band.Cavern;
		public int ElapsedTicks => 0;
	}

	public class Fixtures
	{
		public static ITestContext? Seen;

		[GameTest(Band = Band.Cavern)]
		public void Passes() { }

		[GameTest(Band = Band.Cavern)]
		public void Inspects(ITestContext ctx) => Seen = ctx;

		[GameTest(Band = Band.Cavern)]
		public void Fails() => Assert.Fail("deliberate");
	}
}
