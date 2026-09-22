using Terraria;

namespace Testaria;

/// <summary>
/// Everything about a box worth noticing a change in: every tile, and how
/// many entities of each kind are standing in it.
/// <para/>
/// The point is to be able to assert that something had no effect at all,
/// rather than that one particular thing it might have done did not happen.
/// A test that checks "the tile is still there" passes when the code under
/// test destroys a different tile, drains the water, or spawns something.
/// </summary>
public sealed class BoxSnapshot
{
	private readonly TileRect interior;
	private readonly TileRecord[] tiles;
	private readonly int npcs;
	private readonly int projectiles;
	private readonly int items;

	internal BoxSnapshot(TileRect interior)
	{
		this.interior = interior;
		tiles = new TileRecord[Math.Max(0, interior.Width * interior.Height)];

		for (int y = 0; y < interior.Height; y++) {
			for (int x = 0; x < interior.Width; x++) {
				Tile tile = Main.tile[interior.Left + x, interior.Top + y];

				tiles[(y * interior.Width) + x] = new TileRecord(
					tile.HasTile,
					tile.TileType,
					tile.WallType,
					tile.LiquidAmount,
					tile.LiquidType);
			}
		}

		npcs = CountIn(interior, Main.npc.Length, i => Main.npc[i].active, i => Main.npc[i].Center);
		projectiles = CountIn(interior, Main.maxProjectiles, i => Main.projectile[i].active, i => Main.projectile[i].Center);
		items = CountIn(interior, Main.maxItems, i => Main.item[i].active, i => Main.item[i].Center);
	}

	private static int CountIn(TileRect interior, int length, Func<int, bool> active, Func<int, Microsoft.Xna.Framework.Vector2> centre)
	{
		int count = 0;

		for (int i = 0; i < length; i++) {
			if (!active(i))
				continue;

			Microsoft.Xna.Framework.Vector2 at = centre(i);

			if (BoxSpace.Contains(interior, new WorldPoint(at.X, at.Y)))
				count++;
		}

		return count;
	}

	/// <summary>
	/// What changed between this snapshot and a later one, in the order found.
	/// Empty when nothing did.
	/// </summary>
	/// <param name="later">A snapshot of the same box, taken afterwards.</param>
	/// <param name="limit">
	/// How many tile differences to describe. A change that touched hundreds
	/// of tiles is no clearer for listing all of them, and the count is
	/// reported either way.
	/// </param>
	public IReadOnlyList<string> ChangesTo(BoxSnapshot later, int limit = 5)
	{
		ArgumentNullException.ThrowIfNull(later);

		if (later.interior != interior)
			return [$"the box moved, from {interior} to {later.interior}"];

		var changes = new List<string>();
		int changedTiles = 0;

		for (int i = 0; i < tiles.Length; i++) {
			if (tiles[i] == later.tiles[i])
				continue;

			changedTiles++;

			if (changes.Count < limit) {
				int x = i % interior.Width;
				int y = i / interior.Width;

				changes.Add($"tile {x},{y} was {tiles[i]} and is now {later.tiles[i]}");
			}
		}

		if (changedTiles > changes.Count)
			changes.Add($"...and {changedTiles - changes.Count} further tiles");

		Compare(changes, "NPCs", npcs, later.npcs);
		Compare(changes, "projectiles", projectiles, later.projectiles);
		Compare(changes, "items", items, later.items);

		return changes;
	}

	private static void Compare(List<string> changes, string what, int before, int after)
	{
		if (before != after)
			changes.Add($"{what} in the box went from {before} to {after}");
	}

	private readonly record struct TileRecord(bool HasTile, ushort Type, ushort Wall, byte Liquid, int LiquidType)
	{
		public override string ToString()
			=> HasTile
				? $"tile {Type}, wall {Wall}, liquid {Liquid}"
				: $"empty, wall {Wall}, liquid {Liquid}";
	}
}
