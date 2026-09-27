using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// Whether the IDs a piece of content points at exist.
/// <para/>
/// Most of Terraria's content fields hold an integer indexing an array sized to
/// the loaded count. An item whose <c>shoot</c> names a projectile that was never
/// registered throws <c>IndexOutOfRangeException</c> the first time somebody
/// swings it, which may be on one client, in one situation, long after the mod
/// shipped.
/// <para/>
/// Every "unset" value below was read off the field's own declaration rather than
/// assumed, because the check is only sound if the sentinel is exact. They are
/// not consistent with each other: <c>createTile</c> and the equip slots use -1,
/// while <c>shoot</c>, <c>ammo</c>, <c>buffType</c> and <c>makeNPC</c> use 0.
/// Guessing would have produced a check that reported every ordinary accessory.
/// </summary>
public static class ReferenceSweep
{
	/// <summary>
	/// One ID-valued field on an item, with what "nothing" looks like and what
	/// bounds it.
	/// </summary>
	private readonly record struct Field(string Name, int Value, int Unset, int Limit, string Names);

	/// <summary>
	/// Asserts that every ID an item names is either unset or registered.
	/// <para/>
	/// Reported together rather than one field at a time: the subject is the
	/// item, and an item with two bad references should say so once.
	/// </summary>
	public static void ItemReferencesResolve(string qualified)
	{
		int type = ContentSweep.Require<ModItem>(qualified).Type;

		// Whether a sample exists at all is the content invariants' question,
		// and they ask it. Indexing a missing one here would error the test
		// rather than fail it, and blame this check for a different defect.
		if (!ContentSamples.ItemsByType.TryGetValue(type, out Item? sample) || sample is null) {
			Assert.Skip($"{qualified} (id {type}) has no content sample, so there are no "
				+ "fields to read.");
		}

		List<string> problems = [];

		foreach (Field field in Fields(sample)) {
			if (field.Value == field.Unset || field.Limit <= 0)
				continue;

			if (field.Value < 0 || field.Value >= field.Limit) {
				problems.Add($"{field.Name} is {field.Value}, and there "
					+ (field.Limit == 1 ? "is" : "are") + $" {field.Limit} {field.Names} "
					+ $"(unset is {field.Unset})");
			}
		}

		// Rarity is the one field whose valid values are not a range. Vanilla
		// keeps four negative specials and nothing between them, so -5 is as
		// wrong as 500 and a bounds test would miss it.
		if (!IsKnownRarity(sample.rare)) {
			problems.Add($"rare is {sample.rare}, which is neither a loaded rarity "
				+ $"(0 to {RarityLoader.RarityCount - 1}) nor one of vanilla's negative "
				+ "specials (-1 gray, -11 quest, -12 expert, -13 master)");
		}

		if (problems.Count == 0)
			return;

		Assert.Fail($"{qualified} points at content that does not exist, in "
			+ $"{problems.Count} " + (problems.Count == 1 ? "field" : "fields")
			+ ":\n  " + string.Join("\n  ", problems));
	}

	/// <summary>
	/// Asserts that an NPC declaring a banner item also declares the banner it
	/// belongs to.
	/// <para/>
	/// tModLoader computes this exact condition at load and only logs it
	/// (<c>NPCLoader.FinishSetup</c>), which makes it a check somebody has
	/// already written and nobody asserts. Only the half that is reachable from
	/// outside the loader is covered: the rest compares against
	/// <c>NPCLoader.bannerToItem</c>, which is private, and reflecting into it to
	/// re-derive a warning the loader already emits is not worth the coupling.
	/// </summary>
	public static void NpcBannerAgrees(string qualified)
	{
		ModNPC npc = ContentSweep.Require<ModNPC>(qualified);

		if (npc.BannerItem == 0)
			Assert.Skip($"{qualified} declares no banner item, so there is nothing to agree with.");

		Assert.NotEqual(0, npc.Banner,
			$"{qualified} declares BannerItem {npc.BannerItem} but leaves Banner at 0, so "
			+ "the item exists and nothing is ever counted against it");
	}

	/// <summary>
	/// The ID-valued fields of an item, each with its own sentinel and bound.
	/// <para/>
	/// The equip slots are bounded by the texture arrays rather than by a loader
	/// count, and that is worth a note because the obvious route is wrong.
	/// <c>EquipLoader.nextEquip</c> is internal, and
	/// <c>EquipLoader.GetEquipTexture</c> is public but consults a dictionary
	/// holding only modded textures, so it answers null for every vanilla slot
	/// and cannot serve as a bound. The texture arrays are resized to the same
	/// counts and are public.
	/// <para/>
	/// A bound that comes back as zero is treated as unknown and skipped by the
	/// caller, which is what happens if an array has not been resized in
	/// whatever process this is running in.
	/// </summary>
	private static IEnumerable<Field> Fields(Item sample) => [
		new("createTile", sample.createTile, -1, TileLoader.TileCount, "tiles"),
		new("createWall", sample.createWall, -1, WallLoader.WallCount, "walls"),
		new("shoot", sample.shoot, ProjectileID.None, ProjectileLoader.ProjectileCount, "projectiles"),
		new("ammo", sample.ammo, AmmoID.None, ItemLoader.ItemCount, "items"),
		new("useAmmo", sample.useAmmo, AmmoID.None, ItemLoader.ItemCount, "items"),
		new("buffType", sample.buffType, 0, BuffLoader.BuffCount, "buffs"),
		new("makeNPC", sample.makeNPC, 0, NPCLoader.NPCCount, "NPCs"),
		// No sentinel: 0 is a real use style, so every value has to be in range.
		// int.MinValue stands in for "nothing here means unset".
		new("useStyle", sample.useStyle, int.MinValue, ItemLoader.UseStyleCount, "use styles"),
		new("headSlot", sample.headSlot, -1, Length(TextureAssets.ArmorHead), "head equip slots"),
		new("bodySlot", sample.bodySlot, -1, Length(TextureAssets.ArmorBody), "body equip slots"),
		new("legSlot", sample.legSlot, -1, Length(TextureAssets.ArmorLeg), "leg equip slots"),
	];

	/// <summary>
	/// Whether a rarity is one the game can draw.
	/// <para/>
	/// The negative specials are enumerated rather than bounded, because vanilla
	/// uses -1, -11, -12 and -13 and nothing in between: treating the range as
	/// -13 upward would accept -7, which is not a rarity.
	/// </summary>
	private static bool IsKnownRarity(int rare)
		=> rare is ItemRarityID.Gray or ItemRarityID.Quest or ItemRarityID.Expert or ItemRarityID.Master
			|| (rare >= 0 && rare < RarityLoader.RarityCount);

	/// <summary>An array's length, or zero when it is not there to be measured.</summary>
	private static int Length(Array? array) => array?.Length ?? 0;
}
