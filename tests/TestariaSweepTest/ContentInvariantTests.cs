using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// Things that should be true of any mod's content, asked of every mod that
/// happens to be loaded.
/// <para/>
/// These began life pinned to ExampleMod, where they were written to calibrate
/// the framework against a subject that could be assumed correct. Nothing in
/// them was ever about ExampleMod: an NPC with no life dies to its own spawn
/// effects whoever wrote it, and a display name that is still a localization
/// key looks like text and ships. Pinned to one mod they only ever asked about
/// that mod, so they are asked here of all of them.
/// <para/>
/// What stayed behind with ExampleMod is everything that assumed a particular
/// subject: that it registers mounts, that it contributes recipes, that a named
/// showcase item exists. Those are true of a reference mod and meaningless of a
/// recipe browser, and a check that fails for a mod being itself is worse than
/// no check.
/// </summary>
public class ContentInvariantTests
{
	public static IEnumerable<string> Items => ContentSweep.Every<ModItem>();
	public static IEnumerable<string> Npcs => ContentSweep.Every<ModNPC>();
	public static IEnumerable<string> Buffs => ContentSweep.Every<ModBuff>();
	public static IEnumerable<string> Tiles => ContentSweep.Every<ModTile>();

	// ---- localization -----------------------------------------------------

	/// <summary>
	/// An unfilled key renders as <c>Mods.SomeMod.Items.Foo.DisplayName</c>,
	/// which is not empty, looks like text to every check that only asks
	/// whether a name exists, and reaches a player as gibberish.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void An_item_has_a_display_name_that_is_not_a_raw_key(string qualified)
		=> AssertNamed(ContentSweep.Require<ModItem>(qualified).DisplayName.Value, qualified, "item");

	[LoadedTest]
	[CaseSource(nameof(Npcs))]
	public void An_npc_has_a_display_name_that_is_not_a_raw_key(string qualified)
		=> AssertNamed(ContentSweep.Require<ModNPC>(qualified).DisplayName.Value, qualified, "NPC");

	[LoadedTest]
	[CaseSource(nameof(Buffs))]
	public void A_buff_has_a_display_name_that_is_not_a_raw_key(string qualified)
		=> AssertNamed(ContentSweep.Require<ModBuff>(qualified).DisplayName.Value, qualified, "buff");

	[LoadedTest]
	[CaseSource(nameof(Buffs))]
	public void A_buff_has_a_description(string qualified)
		=> AssertNamed(ContentSweep.Require<ModBuff>(qualified).Description.Value, qualified, "buff description");

	[LoadedTest]
	[CaseSource(nameof(Tiles))]
	public void A_tile_has_a_name(string qualified)
		=> Assert.False(
			string.IsNullOrWhiteSpace(ContentSweep.Require<ModTile>(qualified).Name),
			$"{qualified} has no internal name");

	private static void AssertNamed(string text, string qualified, string kind)
	{
		Assert.False(string.IsNullOrWhiteSpace(text), $"{kind} {qualified} has no display text");

		Assert.False(
			text.StartsWith("Mods.", StringComparison.Ordinal),
			$"{kind} {qualified} shows the raw key '{text}' rather than a translation");
	}

	// ---- items ------------------------------------------------------------

	/// <summary>
	/// The sample the game keeps of every item has to agree with the item
	/// itself. Everything that inspects content without spawning it reads the
	/// sample, so one that disagrees is a quiet wrong answer everywhere.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void An_item_has_a_content_sample_that_agrees_with_it(string qualified)
	{
		ModItem item = ContentSweep.Require<ModItem>(qualified);

		Assert.True(
			ContentSamples.ItemsByType.ContainsKey(item.Type),
			$"{qualified} (id {item.Type}) has no content sample");
		Assert.Equal(item.Type, ContentSamples.ItemsByType[item.Type].type,
			$"{qualified}'s sample is a different item");
	}

	/// <summary>
	/// A stack limit below one is an item that cannot be held. Vanilla's
	/// default is 1, so this catches a <c>maxStack</c> set to 0 by hand.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void An_item_has_a_stack_limit_of_at_least_one(string qualified)
	{
		Item sample = ContentSamples.ItemsByType[ContentSweep.Require<ModItem>(qualified).Type];

		Assert.True(sample.maxStack >= 1, $"{qualified} has a maximum stack of {sample.maxStack}");
	}

	/// <summary>
	/// Value cannot be negative, and damage of exactly -1 is vanilla's sentinel
	/// for "not a weapon", which plenty of accessories use. Anything below that
	/// is a mistake.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void An_item_has_a_sensible_value_and_damage(string qualified)
	{
		Item sample = ContentSamples.ItemsByType[ContentSweep.Require<ModItem>(qualified).Type];

		Assert.True(sample.value >= 0, $"{qualified} is worth {sample.value}");
		Assert.True(sample.damage >= -1, $"{qualified} deals {sample.damage} damage");
	}

	/// <summary>
	/// A weapon that cannot be used is a weapon nobody can swing: damage above
	/// zero means something has to happen when the button is pressed.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void An_item_that_deals_damage_can_be_used(string qualified)
	{
		Item sample = ContentSamples.ItemsByType[ContentSweep.Require<ModItem>(qualified).Type];

		if (sample.damage <= 0)
			Assert.Skip($"{qualified} is not a weapon, so there is nothing to use.");

		Assert.True(sample.useTime > 0, $"{qualified} deals damage but has a use time of {sample.useTime}");
		Assert.True(sample.useAnimation > 0, $"{qualified} deals damage but has no use animation");
	}

	// ---- NPCs -------------------------------------------------------------

	[LoadedTest]
	[CaseSource(nameof(Npcs))]
	public void An_npc_has_a_content_sample(string qualified)
		=> Assert.True(
			ContentSamples.NpcsByNetId.ContainsKey(ContentSweep.Require<ModNPC>(qualified).Type),
			$"{qualified} has no content sample");

	/// <summary>
	/// An NPC with no life dies the instant it is hit by anything, including
	/// its own spawn effects.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Npcs))]
	public void An_npc_has_positive_maximum_life(string qualified)
	{
		NPC sample = ContentSamples.NpcsByNetId[ContentSweep.Require<ModNPC>(qualified).Type];

		Assert.True(sample.lifeMax > 0, $"{qualified} has {sample.lifeMax} maximum life");
	}

	/// <summary>
	/// An NPC with no width or height cannot be hit, cannot collide, and is
	/// drawn as nothing.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Npcs))]
	public void An_npc_has_a_non_zero_size(string qualified)
	{
		NPC sample = ContentSamples.NpcsByNetId[ContentSweep.Require<ModNPC>(qualified).Type];

		Assert.True(sample.width > 0, $"{qualified} is {sample.width} wide");
		Assert.True(sample.height > 0, $"{qualified} is {sample.height} tall");
	}
}
