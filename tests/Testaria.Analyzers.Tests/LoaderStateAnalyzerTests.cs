

namespace Testaria.Analyzers.Tests;

/// <summary>
/// The Tier 0 boundary, asserted rule by rule.
/// <para/>
/// Each case here is one row of the measurement table in PLAN.md section 2.2:
/// the surface that answers wrongly rather than throwing is an error, and the
/// surface that stays true without a game is left alone. Getting the second
/// half wrong would be as bad as getting the first half wrong, because a rule
/// that cries wolf is a rule people turn off.
/// </summary>
public class LoaderStateAnalyzerTests
{
	private const string Using = "using Terraria;\nusing Terraria.ID;\nusing Terraria.ModLoader;\nusing Terraria.Localization;\n";

	[Fact]
	public void ContentSamples_is_an_error_because_it_answers_empty()
		=> XAssert.Equal(
			["TSTA001"],
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					public int Count() => ContentSamples.ItemsByType.Count;
				}
				"""));

	[Fact]
	public void ModContent_lookups_are_an_error()
		=> XAssert.Equal(
			["TSTA001"],
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					public int Type() => ModContent.ItemType<Item>();
				}
				"""));

	[Fact]
	public void ID_sets_are_an_error_because_nothing_resized_them()
		=> XAssert.Equal(
			["TSTA001"],
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					public int Length() => ItemID.Sets.Deprecated.Length;
				}
				"""));

	[Fact]
	public void Localization_is_an_error_whichever_door_it_comes_through()
		=> XAssert.Equal(
			["TSTA001", "TSTA001"],
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					public string Item() => Lang.GetItemNameValue(3507);

					public string Key() => Language.GetTextValue("Mods.Whatever.Key");
				}
				"""));

	[Fact]
	public void Only_the_load_reporting_members_of_ModLoader_are_an_error()
		=> XAssert.Equal(
			["TSTA001"],
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					public int Loaded() => ModLoader.Mods.Length;

					public string Version() => ModLoader.version;
				}
				"""));

	[Fact]
	public void Consts_and_plain_construction_stay_legal()
		=> XAssert.Empty(
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					public int Vanilla() => ItemID.Count;

					public int Damage() => new Item().damage;
				}
				"""));

	[Fact]
	public void A_member_that_declares_the_dependency_is_left_alone()
		=> XAssert.Empty(
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					[Testaria.RequiresLoadedGame("reads the sample cache")]
					public int Count() => ContentSamples.ItemsByType.Count;
				}
				"""));

	[Fact]
	public void A_declaration_on_the_type_covers_its_members()
		=> XAssert.Empty(
			AnalyzerHarness.Ids(Using + """
				[Testaria.RequiresLoadedGame]
				public class Subject
				{
					public int Count() => ContentSamples.ItemsByType.Count;
				}
				"""));

	[Fact]
	public void A_test_that_declares_a_tier_with_a_game_is_left_alone()
		=> XAssert.Empty(
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					[Testaria.LoadedTest]
					public void Registers() => System.Console.WriteLine(ContentSamples.ItemsByType.Count);
				}
				"""));

	[Fact]
	public void A_mod_assembly_is_exempt_because_it_cannot_run_without_a_load_pass()
		=> XAssert.Empty(
			AnalyzerHarness.Ids(Using + """
				public class SomeContent : ILoadable
				{
				}

				public class Subject
				{
					public int Count() => ContentSamples.ItemsByType.Count;
				}
				"""));

	[Fact]
	public void The_build_property_turns_the_whole_rule_off()
		=> XAssert.Empty(
			AnalyzerHarness.Ids(
				Using + """
					public class Subject
					{
						public int Count() => ContentSamples.ItemsByType.Count;
					}
					""",
				loaderStateAnalysis: "false"));

	[Fact]
	public void A_declared_dependency_travels_to_its_callers()
		=> XAssert.Equal(
			["TSTA002"],
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					[Testaria.RequiresLoadedGame]
					public int Count() => ContentSamples.ItemsByType.Count;

					public bool Assert() => Count() > 0;
				}
				"""));

	[Fact]
	public void A_caller_that_declares_it_too_stops_the_chain()
		=> XAssert.Empty(
			AnalyzerHarness.Ids(Using + """
				public class Subject
				{
					[Testaria.RequiresLoadedGame]
					public int Count() => ContentSamples.ItemsByType.Count;

					[Testaria.RequiresLoadedGame]
					public bool Assert() => Count() > 0;
				}
				"""));

	[Fact]
	public void The_diagnostic_names_what_it_found()
	{
		Microsoft.CodeAnalysis.Diagnostic diagnostic = XAssert.Single(AnalyzerHarness.Analyze(Using + """
			public class Subject
			{
				public int Length() => ItemID.Sets.Deprecated.Length;
			}
			"""));

		// Naming the member rather than the expression is what makes the
		// error actionable: "ItemID.Sets.Deprecated" says which of the many
		// things on that line is the problem.
		XAssert.Contains("ItemID.Sets.Deprecated", diagnostic.GetMessage());
	}
}
