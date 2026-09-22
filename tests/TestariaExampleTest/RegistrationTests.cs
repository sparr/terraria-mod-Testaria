using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// That every kind of content the subject declares actually reaches the game.
/// <para/>
/// Tier 1 by construction: none of this exists until a load pass has run.
/// Registration is also the cheapest useful thing to test about a content mod,
/// and the failure it catches, content silently not loading, is among the most
/// common.
/// </summary>
public class RegistrationTests
{
	[LoadedTest]
	public void The_subject_is_loaded()
		=> Assert.Equal(Subject.Name, Subject.Require().Name);

	[LoadedTest]
	public void It_registers_items()
		=> AssertRegistered(Subject.Content<ModItem>(), i => i.Type, ItemID.Count, "item");

	[LoadedTest]
	public void It_registers_npcs()
		=> AssertRegistered(Subject.Content<ModNPC>(), n => n.Type, NPCID.Count, "NPC");

	[LoadedTest]
	public void It_registers_projectiles()
		=> AssertRegistered(Subject.Content<ModProjectile>(), p => p.Type, ProjectileID.Count, "projectile");

	[LoadedTest]
	public void It_registers_buffs()
		=> AssertRegistered(Subject.Content<ModBuff>(), b => b.Type, BuffID.Count, "buff");

	[LoadedTest]
	public void It_registers_tiles()
		=> AssertRegistered(Subject.Content<ModTile>(), t => t.Type, TileID.Count, "tile");

	[LoadedTest]
	public void It_registers_walls()
		=> AssertRegistered(Subject.Content<ModWall>(), w => w.Type, WallID.Count, "wall");

	[LoadedTest]
	public void It_registers_mounts()
		=> Assert.NotEmpty(Subject.Content<ModMount>());

	[LoadedTest]
	public void It_registers_prefixes()
		=> Assert.NotEmpty(Subject.Content<ModPrefix>());

	[LoadedTest]
	public void It_registers_rarities()
		=> Assert.NotEmpty(Subject.Content<ModRarity>());

	[LoadedTest]
	public void It_registers_biomes()
		=> Assert.NotEmpty(Subject.Content<ModBiome>());

	[LoadedTest]
	public void It_registers_tile_entities()
		=> Assert.NotEmpty(Subject.Content<ModTileEntity>());

	[LoadedTest]
	public void Every_registered_id_is_distinct()
	{
		// Two pieces of content sharing an id means one of them is
		// unreachable, and nothing else would report it.
		List<int> types = [.. Subject.Content<ModItem>().Select(i => i.Type)];

		Assert.Equal(types.Count, types.Distinct().Count());
	}

	/// <summary>
	/// Shared shape: the category is non-empty, and every id sits above the
	/// vanilla range, since modded ids are assigned after vanilla's at load.
	/// An id at or below the count would mean the lookup found vanilla content.
	/// </summary>
	private static void AssertRegistered<T>(IReadOnlyList<T> content, Func<T, int> type, int vanillaCount, string kind)
		where T : IModType
	{
		Assert.NotEmpty(content);

		foreach (T item in content)
			Assert.True(type(item) >= vanillaCount, $"{kind} '{item.Name}' has id {type(item)}, which is inside the vanilla range ending at {vanillaCount}");
	}
}
