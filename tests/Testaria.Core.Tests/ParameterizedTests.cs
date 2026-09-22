using System.Collections;

namespace Testaria.Tests;

public class TestCaseLabelTests
{
	[Fact]
	public void No_arguments_means_no_label()
		=> XAssert.Equal(string.Empty, TestCaseLabel.For([]));

	[Fact]
	public void Arguments_are_rendered_in_order()
		=> XAssert.Equal("(1, 2)", TestCaseLabel.For([1, 2]));

	[Fact]
	public void Strings_are_quoted_so_whitespace_is_visible()
		=> XAssert.Equal("(\"a b\")", TestCaseLabel.For(["a b"]));

	[Fact]
	public void Nulls_and_booleans_read_as_themselves()
		=> XAssert.Equal("(null, true, false)", TestCaseLabel.For([null, true, false]));

	[Fact]
	public void Numbers_use_an_invariant_format()
	{
		// A comma decimal separator would make the label unparseable as well
		// as unfilterable.
		XAssert.Equal("(1.5)", TestCaseLabel.For([1.5]));
	}

	[Fact]
	public void A_long_argument_is_cut_so_the_report_stays_readable()
	{
		string label = TestCaseLabel.For([new string('x', 200)]);

		XAssert.True(label.Length < 60, $"label was {label.Length} characters");
		XAssert.Contains("...", label);
	}
}

public class ParameterExpansionTests
{
	private static IReadOnlyList<TestCase> Cases<T>(string name)
		=> [.. TestDiscovery.Discover([typeof(T)]).Tests.Where(t => t.Method.Name == name)];

	private static IReadOnlyList<TestDiscoveryError> Errors<T>(string name)
		=> [.. TestDiscovery.Discover([typeof(T)]).Errors.Where(e => e.Location.EndsWith(name, StringComparison.Ordinal))];

	[Fact]
	public void Each_case_attribute_becomes_its_own_test()
		=> XAssert.Equal(3, Cases<Fixtures>(nameof(Fixtures.Inline)).Count);

	[Fact]
	public void A_case_is_named_by_its_arguments_not_merely_numbered()
	{
		// The whole reason to expand rather than loop: a report should say
		// which case failed, and a filter should be able to single it out.
		IReadOnlyList<TestCase> cases = Cases<Fixtures>(nameof(Fixtures.Inline));

		XAssert.Contains(cases, c => c.Name == "Inline(1)");
		XAssert.Contains(cases, c => c.Name == "Inline(2)");
	}

	[Fact]
	public void Arguments_are_carried_on_the_case()
		=> XAssert.Equal([1], Cases<Fixtures>(nameof(Fixtures.Inline)).First(c => c.Name == "Inline(1)").Arguments);

	[Fact]
	public void Several_arguments_expand_together()
	{
		TestCase only = Cases<Fixtures>(nameof(Fixtures.TwoArguments)).Single();

		XAssert.Equal("TwoArguments(2, \"two\")", only.Name);
	}

	[Fact]
	public void A_source_member_supplies_cases()
	{
		// The reason parameterisation was worth building: cases that only
		// exist once the game is loaded cannot be written out inline.
		IReadOnlyList<TestCase> cases = Cases<Fixtures>(nameof(Fixtures.FromProperty));

		XAssert.Equal(3, cases.Count);
		XAssert.Contains(cases, c => c.Name == "FromProperty(\"beta\")");
	}

	[Fact]
	public void A_source_may_be_a_property_a_field_or_a_method()
	{
		XAssert.NotEmpty(Cases<Fixtures>(nameof(Fixtures.FromProperty)));
		XAssert.NotEmpty(Cases<Fixtures>(nameof(Fixtures.FromField)));
		XAssert.NotEmpty(Cases<Fixtures>(nameof(Fixtures.FromMethod)));
	}

	[Fact]
	public void A_single_argument_source_need_not_wrap_each_value_in_an_array()
		=> XAssert.Equal(2, Cases<Fixtures>(nameof(Fixtures.FromBareValues)).Count);

	[Fact]
	public void Inline_and_source_cases_combine()
		=> XAssert.Equal(3, Cases<Fixtures>(nameof(Fixtures.Combined)).Count);

	[Fact]
	public void A_context_may_lead_the_arguments()
	{
		TestCase only = Cases<Fixtures>(nameof(Fixtures.WithContext)).Single();

		XAssert.True(only.WantsContext);
		XAssert.Equal([7], only.Arguments);
		XAssert.Equal("WithContext(7)", only.Name);
	}

	[Fact]
	public void A_coroutine_can_be_parameterised_too()
	{
		IReadOnlyList<TestCase> cases = Cases<Fixtures>(nameof(Fixtures.Coroutine));

		XAssert.Equal(2, cases.Count);
		XAssert.All(cases, c => XAssert.Equal(TestBodyKind.Coroutine, c.BodyKind));
	}

	[Fact]
	public void An_unparameterised_test_is_unaffected()
	{
		TestCase only = Cases<Fixtures>(nameof(Fixtures.Plain)).Single();

		XAssert.Empty(only.Arguments);
		XAssert.Equal("Plain", only.Name);
	}

	[Fact]
	public void A_case_of_the_wrong_length_is_reported_and_the_rest_still_run()
	{
		// One malformed case should not take the others with it.
		XAssert.Single(Errors<Fixtures>(nameof(Fixtures.WrongArity)));
		XAssert.Single(Cases<Fixtures>(nameof(Fixtures.WrongArity)));
	}

	[Fact]
	public void A_missing_source_member_is_reported()
	{
		TestDiscoveryError error = Errors<Fixtures>(nameof(Fixtures.MissingSource)).Single();

		XAssert.Contains("NoSuchMember", error.Message);
	}

	[Fact]
	public void A_source_that_is_not_a_sequence_is_reported()
		=> XAssert.Contains("not a sequence", Errors<Fixtures>(nameof(Fixtures.BadSource)).Single().Message);

	[Fact]
	public void Data_attributes_on_a_test_that_takes_nothing_are_reported()
		=> XAssert.Contains("takes no arguments", Errors<Fixtures>(nameof(Fixtures.DataButNoParameters)).Single().Message);

	[Fact]
	public void A_context_anywhere_but_first_is_reported()
		=> XAssert.Contains("first parameter", Errors<Fixtures>(nameof(Fixtures.ContextNotFirst)).Single().Message);

	[Fact]
	public void A_filter_can_single_out_one_case()
	{
		// Naming cases is what makes this possible; numbering them would not.
		TestFilter filter = TestFilter.Parse(@"Inline\(2\)");
		IReadOnlyList<TestCase> matched = [.. Cases<Fixtures>(nameof(Fixtures.Inline)).Where(filter.Matches)];

		XAssert.Single(matched);
		XAssert.Equal("Inline(2)", matched[0].Name);
	}

	public class Fixtures
	{
		public static IEnumerable<object?[]> Names => [["alpha"], ["beta"], ["gamma"]];
		public static readonly IEnumerable<object?[]> FieldNames = [["one"]];
		public static IEnumerable<object?[]> Method() => [["m"]];
		public static IEnumerable<string> Bare => ["x", "y"];
		public static string NotASequence => "nope";

		[LoadedTest]
		[Case(1)]
		[Case(2)]
		[Case(3)]
		public void Inline(int value) => XAssert.True(value > 0);

		[LoadedTest]
		[Case(2, "two")]
		public void TwoArguments(int number, string word) => XAssert.NotNull(word);

		[LoadedTest]
		[CaseSource(nameof(Names))]
		public void FromProperty(string name) => XAssert.NotNull(name);

		[LoadedTest]
		[CaseSource(nameof(FieldNames))]
		public void FromField(string name) => XAssert.NotNull(name);

		[LoadedTest]
		[CaseSource(nameof(Method))]
		public void FromMethod(string name) => XAssert.NotNull(name);

		[LoadedTest]
		[CaseSource(nameof(Bare))]
		public void FromBareValues(string name) => XAssert.NotNull(name);

		[LoadedTest]
		[Case("inline")]
		[CaseSource(nameof(Bare))]
		public void Combined(string name) => XAssert.NotNull(name);

		[GameTest(Band = Band.Cavern)]
		[Case(7)]
		public void WithContext(ITestContext ctx, int value) => XAssert.NotNull(ctx);

		[GameTest(Band = Band.Cavern)]
		[Case(1)]
		[Case(2)]
		public IEnumerator Coroutine(int value) { yield break; }

		[LoadedTest]
		public void Plain() { }

		[LoadedTest]
		[Case(1)]
		[Case(1, 2)]
		public void WrongArity(int value) { }

		[LoadedTest]
		[CaseSource("NoSuchMember")]
		public void MissingSource(string value) { }

		[LoadedTest]
		[CaseSource(nameof(NotASequence))]
		public void BadSource(string value) { }

		[LoadedTest]
		[Case(1)]
		public void DataButNoParameters() { }

		[LoadedTest]
		[Case(1)]
		public void ContextNotFirst(int value, ITestContext ctx) { }
	}
}

public class EmptyCaseSourceTests
{
	[Fact]
	public void A_source_with_no_cases_reports_a_skip_rather_than_vanishing()
	{
		// An empty source is legitimate when the content it enumerates is not
		// installed. Reporting nothing at all would leave the suite looking
		// complete; reporting a failure would blame nobody in particular.
		TestCase only = TestDiscovery.Discover([typeof(Fixtures)]).Tests
			.Single(t => t.Method.Name == nameof(Fixtures.NothingToRun));

		XAssert.NotNull(only.SkipReason);
		XAssert.Contains("no cases", only.SkipReason);
	}

	[Fact]
	public void An_empty_source_is_not_a_discovery_error()
		=> XAssert.Empty(TestDiscovery.Discover([typeof(Fixtures)]).Errors);

	public class Fixtures
	{
		public static IEnumerable<object?[]> Nothing => [];

		[LoadedTest]
		[CaseSource(nameof(Nothing))]
		public void NothingToRun(string value) { }
	}
}
