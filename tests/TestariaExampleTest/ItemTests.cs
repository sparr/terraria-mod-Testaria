using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// Invariants every item should hold, one case per item.
/// <para/>
/// Parameterised rather than looped, so a failure names the item rather than
/// the loop, one bad item does not hide the rest, and a single item can be
/// filtered for while fixing it.
/// </summary>
public class ItemTests
{
	/// <summary>Every item the subject registers, by name.</summary>
	public static IEnumerable<string> Items => Subject.NamesOf<ModItem>();

	private static Item SampleOf(string name)
	{
		Assert.True(ModContent.TryFind(Subject.Name, name, out ModItem item), $"'{name}' no longer resolves by name");
		Assert.True(ContentSamples.ItemsByType.ContainsKey(item.Type), $"item '{name}' (id {item.Type}) has no content sample");

		return ContentSamples.ItemsByType[item.Type];
	}

	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void Has_a_content_sample_that_agrees_with_it(string name)
	{
		// ContentSamples is how the rest of the game inspects an item without
		// spawning one. An item missing from it is invisible to a great deal
		// of tooling.
		Assert.True(ModContent.TryFind(Subject.Name, name, out ModItem item));

		Assert.Equal(item.Type, SampleOf(name).type);
	}

	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void Has_a_display_name_that_is_not_a_raw_key(string name)
	{
		Assert.True(ModContent.TryFind(Subject.Name, name, out ModItem item));
		string display = item.DisplayName.Value;

		Assert.False(string.IsNullOrWhiteSpace(display), $"item '{name}' has no display name");

		// An unfilled key renders as "Mods.ExampleMod.Items.Foo.DisplayName",
		// which looks like text and survives an emptiness check.
		Assert.False(display.StartsWith("Mods.", StringComparison.Ordinal), $"item '{name}' shows the raw key '{display}'");
	}

	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void Has_a_sensible_value_and_damage(string name)
	{
		Item sample = SampleOf(name);

		// Damage of -1 is vanilla's sentinel for "not a weapon", used by
		// plenty of accessories, so only values below it are wrong.
		Assert.True(sample.value >= 0, $"item '{name}' has a negative value of {sample.value}");
		Assert.True(sample.damage >= -1, $"item '{name}' has damage of {sample.damage}, below the -1 sentinel");
	}

	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void Has_a_stack_limit_of_at_least_one(string name)
		=> Assert.True(SampleOf(name).maxStack >= 1, $"item '{name}' has a maximum stack of {SampleOf(name).maxStack}");

	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void Is_usable_if_it_is_a_weapon(string name)
	{
		Item sample = SampleOf(name);

		if (sample.damage <= 0 || sample.useStyle == ItemUseStyleID.None)
			return;

		// A weapon with a zero use time does nothing at all when clicked.
		Assert.True(sample.useTime > 0, $"weapon '{name}' has a use time of {sample.useTime}");
		Assert.True(sample.useAnimation > 0, $"weapon '{name}' has a use animation of {sample.useAnimation}");
	}

	[LoadedTest]
	public void The_showcase_item_is_findable_by_name()
	{
		// One concrete lookup, to pin that the per-item cases above are not
		// quietly running over an empty set.
		Assert.True(ModContent.TryFind(Subject.Name, "ExampleItem", out ModItem item));
		Assert.Equal("ExampleItem", item.Name);
	}
}
