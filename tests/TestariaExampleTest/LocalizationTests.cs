using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// That every piece of content has text a player will actually see.
/// <para/>
/// Localization is populated only by a load pass, so this is Tier 1 by
/// construction. It is also the check most worth automating for a content
/// mod: a missing display name is invisible in code review, ships happily,
/// and shows up in game as a raw key.
/// </summary>
public class LocalizationTests
{
	[LoadedTest]
	public void Every_item_has_a_display_name()
		=> AssertNamed(Subject.Content<ModItem>(), i => i.DisplayName.Value, "item");

	[LoadedTest]
	public void Every_npc_has_a_display_name()
		=> AssertNamed(Subject.Content<ModNPC>(), n => n.DisplayName.Value, "NPC");

	[LoadedTest]
	public void Every_buff_has_a_display_name()
		=> AssertNamed(Subject.Content<ModBuff>(), b => b.DisplayName.Value, "buff");

	[LoadedTest]
	public void Every_buff_has_a_description()
		=> AssertNamed(Subject.Content<ModBuff>(), b => b.Description.Value, "buff description");

	[LoadedTest]
	public void No_display_name_is_a_raw_localization_key()
	{
		// An unfilled key renders as "Mods.ExampleMod.Items.Foo.DisplayName",
		// which looks like text and passes an emptiness check.
		foreach (ModItem item in Subject.Content<ModItem>()) {
			string name = item.DisplayName.Value;

			Assert.False(
				name.StartsWith("Mods.", StringComparison.Ordinal),
				$"item '{item.Name}' shows the raw key '{name}' rather than a translation");
		}
	}

	private static void AssertNamed<T>(IReadOnlyList<T> content, Func<T, string> text, string kind)
		where T : IModType
	{
		Assert.NotEmpty(content);

		foreach (T item in content) {
			string value = text(item);

			Assert.False(string.IsNullOrWhiteSpace(value), $"{kind} '{item.Name}' has no display text");
		}
	}
}
