using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// Invariants every item should hold, whatever it is.
/// <para/>
/// Written as invariants over the whole set rather than assertions about
/// particular items, so they keep their meaning as the subject changes, and so
/// they say something about the mod rather than about one line of it.
/// </summary>
public class ItemTests
{
	[LoadedTest]
	public void Every_item_has_a_content_sample()
	{
		// ContentSamples is how the rest of the game inspects an item without
		// spawning one. An item missing from it is invisible to a great deal
		// of tooling, including the bestiary and Journey mode research.
		foreach (ModItem item in Subject.Content<ModItem>()) {
			Assert.True(
				ContentSamples.ItemsByType.ContainsKey(item.Type),
				$"item '{item.Name}' (id {item.Type}) has no content sample");
		}
	}

	[LoadedTest]
	public void A_content_sample_agrees_with_the_item_it_samples()
	{
		foreach (ModItem item in Subject.Content<ModItem>()) {
			Item sample = ContentSamples.ItemsByType[item.Type];

			Assert.Equal(item.Type, sample.type);
		}
	}

	[LoadedTest]
	public void No_item_has_a_nonsensical_value_or_damage()
	{
		// Damage of -1 is vanilla's sentinel for "not a weapon", used by
		// plenty of accessories, so only values below that are wrong. Found by
		// this test failing on RubyEarrings, which is fine and the assertion
		// was not.
		foreach (ModItem item in Subject.Content<ModItem>()) {
			Item sample = ContentSamples.ItemsByType[item.Type];

			Assert.True(sample.value >= 0, $"item '{item.Name}' has a negative value of {sample.value}");
			Assert.True(sample.damage >= -1, $"item '{item.Name}' has damage of {sample.damage}, below the -1 sentinel");
		}
	}

	[LoadedTest]
	public void Every_weapon_has_a_use_time_and_animation()
	{
		// A weapon with a zero use time is unusable, and the symptom in game
		// is an item that simply does nothing when clicked.
		foreach (ModItem item in Subject.Content<ModItem>()) {
			Item sample = ContentSamples.ItemsByType[item.Type];

			if (sample.damage <= 0 || sample.useStyle == ItemUseStyleID.None)
				continue;

			Assert.True(sample.useTime > 0, $"weapon '{item.Name}' has a use time of {sample.useTime}");
			Assert.True(sample.useAnimation > 0, $"weapon '{item.Name}' has a use animation of {sample.useAnimation}");
		}
	}

	[LoadedTest]
	public void Every_stackable_item_has_a_sane_maximum()
	{
		foreach (ModItem item in Subject.Content<ModItem>()) {
			Item sample = ContentSamples.ItemsByType[item.Type];

			Assert.True(sample.maxStack >= 1, $"item '{item.Name}' has a maximum stack of {sample.maxStack}");
		}
	}

	[LoadedTest]
	public void The_showcase_item_is_findable_by_name()
	{
		// One concrete lookup, to pin that resolving by name works at all and
		// that the invariants above are not passing over an empty set.
		Assert.True(ModContent.TryFind(Subject.Name, "ExampleItem", out ModItem item));
		Assert.Equal("ExampleItem", item.Name);
	}
}
