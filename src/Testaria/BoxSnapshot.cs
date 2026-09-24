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
	private readonly int[] npcs;
	private readonly int[] projectiles;
	private readonly int[] items;

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
					tile.TileFrameX,
					tile.TileFrameY,
					tile.WallType,
					tile.LiquidAmount,
					tile.LiquidType,
					tile.TileColor,
					tile.WallColor,
					(byte)tile.Slope,
					tile.IsHalfBlock,
					tile.HasActuator,
					tile.IsActuated,
					tile.RedWire,
					tile.BlueWire,
					tile.GreenWire,
					tile.YellowWire);
			}
		}

		npcs = TypesIn(interior, Main.npc.Length, i => Main.npc[i].active, i => Main.npc[i].Center, i => Main.npc[i].type);
		projectiles = TypesIn(interior, Main.maxProjectiles, i => Main.projectile[i].active, i => Main.projectile[i].Center, i => Main.projectile[i].type);
		items = TypesIn(interior, Main.maxItems, i => Main.item[i].active, i => Main.item[i].Center, i => Main.item[i].type);
	}

	/// <summary>The rectangle this snapshot covers.</summary>
	public TileRect Interior => interior;

	/// <summary>
	/// Writes every recorded tile back, undoing whatever the box's tenant did
	/// to the ground.
	/// <para/>
	/// PLAN.md section 2.4 lists this first among the things teardown has to
	/// undo. Without it, entities are deactivated and the ground is left exactly
	/// as the last test built it, for the next tenant of that slot to inherit.
	/// <para/>
	/// Deliberately no reframing afterwards. The frames recorded here are the
	/// truth about what the box looked like, and recomputing them invents
	/// different ones: a blank world writes its ground without framing it, so
	/// a reframe after restore leaves every tile differing from the snapshot it
	/// was just restored from. Measured, that makes five boxes out of
	/// twenty-eight look as though they never went quiet.
	/// </summary>
	/// <returns>How many tiles differed and were put back.</returns>
	public int Restore()
	{
		int changed = 0;

		for (int y = 0; y < interior.Height; y++) {
			for (int x = 0; x < interior.Width; x++) {
				TileRecord record = tiles[(y * interior.Width) + x];
				Tile tile = Main.tile[interior.Left + x, interior.Top + y];

				if (Matches(tile, record))
					continue;

				changed++;
				Apply(tile, record);
			}
		}

		return changed;
	}

	/// <summary>
	/// The smallest rectangle, in box-relative tiles, covering everything that
	/// differs from this snapshot, or null when nothing does.
	/// <para/>
	/// What a test actually used, as against what it asked for. The two being
	/// far apart is what makes a default box size the wrong size.
	/// </summary>
	public TileRect? ChangedBounds()
	{
		int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;

		for (int y = 0; y < interior.Height; y++) {
			for (int x = 0; x < interior.Width; x++) {
				if (Matches(Main.tile[interior.Left + x, interior.Top + y], tiles[(y * interior.Width) + x]))
					continue;

				left = Math.Min(left, x);
				top = Math.Min(top, y);
				right = Math.Max(right, x);
				bottom = Math.Max(bottom, y);
			}
		}

		return left > right ? null : new TileRect(left, top, right - left + 1, bottom - top + 1);
	}

	private static bool Matches(Tile tile, TileRecord record)
		=> tile.HasTile == record.HasTile
			&& tile.TileType == record.Type
			&& tile.TileFrameX == record.FrameX
			&& tile.TileFrameY == record.FrameY
			&& tile.WallType == record.Wall
			&& tile.LiquidAmount == record.Liquid
			&& tile.LiquidType == record.LiquidType
			&& tile.TileColor == record.Paint
			&& tile.WallColor == record.WallPaint
			&& (byte)tile.Slope == record.Slope
			&& tile.IsHalfBlock == record.HalfBlock
			&& tile.HasActuator == record.Actuator
			&& tile.IsActuated == record.Actuated
			&& tile.RedWire == record.RedWire
			&& tile.BlueWire == record.BlueWire
			&& tile.GreenWire == record.GreenWire
			&& tile.YellowWire == record.YellowWire;

	private static void Apply(Tile tile, TileRecord record)
	{
		tile.HasTile = record.HasTile;
		tile.TileType = record.Type;
		tile.TileFrameX = record.FrameX;
		tile.TileFrameY = record.FrameY;
		tile.WallType = record.Wall;
		tile.LiquidAmount = record.Liquid;
		tile.LiquidType = record.LiquidType;
		tile.TileColor = record.Paint;
		tile.WallColor = record.WallPaint;
		tile.Slope = (Terraria.ID.SlopeType)record.Slope;
		tile.IsHalfBlock = record.HalfBlock;
		tile.HasActuator = record.Actuator;
		tile.IsActuated = record.Actuated;
		tile.RedWire = record.RedWire;
		tile.BlueWire = record.BlueWire;
		tile.GreenWire = record.GreenWire;
		tile.YellowWire = record.YellowWire;
	}

	/// <summary>
	/// The types of a kind of entity standing in the box, sorted.
	/// <para/>
	/// Types rather than a count, because a count is blind to substitution: a
	/// slime replaced by a zombie leaves the count at one.
	/// </summary>
	private static int[] TypesIn(
		TileRect interior,
		int length,
		Func<int, bool> active,
		Func<int, Microsoft.Xna.Framework.Vector2> centre,
		Func<int, int> type)
	{
		var found = new List<int>();

		for (int i = 0; i < length; i++) {
			if (!active(i))
				continue;

			Microsoft.Xna.Framework.Vector2 at = centre(i);

			if (BoxSpace.Contains(interior, new WorldPoint(at.X, at.Y)))
				found.Add(type(i));
		}

		found.Sort();

		return [.. found];
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

	private static void Compare(List<string> changes, string what, int[] before, int[] after)
	{
		if (before.SequenceEqual(after))
			return;

		changes.Add($"{what} in the box went from [{string.Join(", ", before)}] to [{string.Join(", ", after)}]");
	}

	/// <summary>
	/// Everything about one tile that a side effect could plausibly disturb.
	/// <para/>
	/// Framing, paint, slope, actuators and wiring are all in here because a
	/// change to any of them is a change to the world, and a snapshot that
	/// only watched tile type would call a repainted or rewired box
	/// untouched.
	/// </summary>
	private readonly record struct TileRecord(
		bool HasTile,
		ushort Type,
		short FrameX,
		short FrameY,
		ushort Wall,
		byte Liquid,
		int LiquidType,
		byte Paint,
		byte WallPaint,
		byte Slope,
		bool HalfBlock,
		bool Actuator,
		bool Actuated,
		bool RedWire,
		bool BlueWire,
		bool GreenWire,
		bool YellowWire)
	{
		public override string ToString()
		{
			string tile = HasTile ? $"tile {Type} frame {FrameX},{FrameY}" : "no tile";
			string extras = string.Join("", [
				Paint > 0 ? $" paint {Paint}" : "",
				Slope > 0 ? $" slope {Slope}" : "",
				HalfBlock ? " half" : "",
				Actuator ? " actuator" : "",
				Actuated ? " actuated" : "",
				RedWire || BlueWire || GreenWire || YellowWire ? " wired" : "",
			]);

			return $"{tile}, wall {Wall}{(WallPaint > 0 ? $" paint {WallPaint}" : "")}, liquid {Liquid}{extras}";
		}
	}
}
