namespace Testaria.Tests;

public class RequiredModsTests
{
	[Fact]
	public void Nothing_required_is_nothing_to_check()
	{
		XAssert.Empty(RequiredMods.Parse(null));
		XAssert.Empty(RequiredMods.Parse(""));
		XAssert.Empty(RequiredMods.Parse("   "));
	}

	[Fact]
	public void Names_are_split_on_commas()
		=> XAssert.Equal(["ExampleMod", "ExampleModTests"], RequiredMods.Parse("ExampleMod,ExampleModTests"));

	[Theory]
	[InlineData("A, B")]
	[InlineData("A,,B")]
	[InlineData("A B")]
	[InlineData(" A ; B ")]
	[InlineData("A,\tB")]
	public void Sloppy_separators_still_give_two_names(string value)
	{
		// This value is assembled by shell scripts as often as by the tool,
		// and a stray comma must not become a mod named "" that can never be
		// found and so fails every run.
		XAssert.Equal(["A", "B"], RequiredMods.Parse(value));
	}

	[Fact]
	public void A_name_repeated_is_only_asked_for_once()
		=> XAssert.Equal(["A", "B"], RequiredMods.Parse("A,B,A"));

	[Fact]
	public void Everything_present_is_nothing_missing()
		=> XAssert.Empty(RequiredMods.Missing(["A", "B"], ["Testaria", "A", "B"]));

	[Fact]
	public void What_is_absent_is_reported_in_the_order_it_was_asked_for()
		=> XAssert.Equal(["B", "D"], RequiredMods.Missing(["B", "C", "D"], ["C", "E"]));

	[Fact]
	public void Case_is_not_forgiven()
	{
		// tModLoader mod names are case sensitive identifiers. Treating
		// "examplemod" as "ExampleMod" would let a run pass a check the game
		// itself would fail, which is the opposite of the point.
		XAssert.Equal(["ExampleMod"], RequiredMods.Missing(["ExampleMod"], ["examplemod"]));
	}

	[Fact]
	public void The_message_names_what_is_missing_and_what_is_there()
	{
		string message = RequiredMods.Describe(["Calamity"], ["Testaria", "CalamityTests"]);

		XAssert.Contains("Calamity", message);
		XAssert.Contains("CalamityTests", message);
		// The usual cause, and the thing somebody needs to be told to go and
		// look at.
		XAssert.Contains("load", message);
	}

	[Fact]
	public void One_missing_mod_is_not_described_as_several()
	{
		XAssert.Contains("mod Calamity", RequiredMods.Describe(["Calamity"], ["Testaria"]));
		XAssert.Contains("mods A, B", RequiredMods.Describe(["A", "B"], ["Testaria"]));
	}

	[Fact]
	public void Describing_nothing_missing_is_a_programming_error()
		=> XAssert.Throws<ArgumentException>(() => RequiredMods.Describe([], ["Testaria"]));
}
