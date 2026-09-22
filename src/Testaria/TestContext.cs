using Terraria;
using Terraria.DataStructures;

namespace Testaria;

/// <summary>
/// What a running gameplay test can see and do inside its box.
/// <para/>
/// Every position a test gives is box-relative, so a test never has to know
/// where in the world the arena actually placed it, and cannot accidentally
/// reach outside by naming an absolute coordinate.
/// </summary>
public sealed class TestContext : ITickingContext, IDisposable
{
	private readonly List<SpawnedNpc> spawned = [];

	internal TestContext(BoxLease? lease)
	{
		Lease = lease;
		Interior = lease?.Interior ?? default;
		Bounds = lease?.Bounds ?? default;
		Bands = lease?.Bands ?? Band.None;
	}

	/// <summary>The lease backing this context, if the test asked for a box.</summary>
	public BoxLease? Lease { get; }

	/// <inheritdoc />
	public TileRect Interior { get; }

	/// <inheritdoc />
	public TileRect Bounds { get; }

	/// <inheritdoc />
	public Band Bands { get; }

	/// <inheritdoc />
	public int ElapsedTicks { get; private set; }

	/// <summary>The world position at the centre of the box.</summary>
	public WorldPoint Center => BoxSpace.Center(Interior);

	/// <summary>The world position of a box-relative tile.</summary>
	public WorldPoint At(int offsetX, int offsetY) => BoxSpace.At(Interior, offsetX, offsetY);

	/// <summary>
	/// Spawns an NPC at a box-relative position and records it, so that
	/// teardown can remove it whether or not the test remembered to.
	/// </summary>
	public NPC SpawnNPC(int type, int offsetX, int offsetY)
		=> SpawnNPC(type, At(offsetX, offsetY));

	/// <summary>Spawns an NPC at a world position, which must lie inside the box.</summary>
	public NPC SpawnNPC(int type, WorldPoint at)
	{
		if (!BoxSpace.Contains(Interior, at))
			throw new ArgumentOutOfRangeException(nameof(at), at, $"Position lies outside this test's box {Interior}.");

		int index = NPC.NewNPC(new EntitySource_DebugCommand("Testaria"), (int)at.X, (int)at.Y, type);
		NPC npc = Main.npc[index];

		spawned.Add(new SpawnedNpc(index, type));

		// Without this the game reclaims it within about a second: CheckActive
		// despawns anything out of range of a player, and a headless server has
		// no players, so everything is out of range.
		TestOwnership.OwnNpc(index);

		return npc;
	}

	/// <summary>Places a tile at a box-relative position.</summary>
	public void PlaceTile(int offsetX, int offsetY, int type)
	{
		(int x, int y) = Absolute(offsetX, offsetY);

		WorldGen.PlaceTile(x, y, type, mute: true, forced: true);
	}

	/// <summary>Removes the tile at a box-relative position, dropping nothing.</summary>
	public void ClearTile(int offsetX, int offsetY)
	{
		(int x, int y) = Absolute(offsetX, offsetY);

		WorldGen.KillTile(x, y, fail: false, effectOnly: false, noItem: true);
	}

	/// <summary>Reads the tile at a box-relative position.</summary>
	public Tile GetTile(int offsetX, int offsetY)
	{
		(int x, int y) = Absolute(offsetX, offsetY);

		return Main.tile[x, y];
	}

	/// <inheritdoc />
	public void Tick() => ElapsedTicks++;

	/// <summary>
	/// Removes everything this test spawned. Called by the runner when the
	/// test ends, pass or fail, while the box is still leased to it.
	/// <para/>
	/// Entities are matched on both slot and type, because Terraria reuses
	/// entity slots: deactivating purely by index could kill an unrelated NPC
	/// that happened to inherit the slot. That check is a mitigation rather
	/// than a guarantee, and the ownership warden in PLAN.md section 2.4 is
	/// the real answer.
	/// </summary>
	public void Dispose()
	{
		foreach (SpawnedNpc record in spawned) {
			NPC npc = Main.npc[record.Index];

			TestOwnership.ReleaseNpc(record.Index);

			if (npc.active && npc.type == record.Type)
				npc.active = false;
		}

		spawned.Clear();
	}

	private (int X, int Y) Absolute(int offsetX, int offsetY)
	{
		if (offsetX < 0 || offsetY < 0 || offsetX >= Interior.Width || offsetY >= Interior.Height)
			throw new ArgumentOutOfRangeException(nameof(offsetX), $"Offset ({offsetX}, {offsetY}) lies outside this test's box {Interior}.");

		return (Interior.Left + offsetX, Interior.Top + offsetY);
	}

	private readonly record struct SpawnedNpc(int Index, int Type);
}
