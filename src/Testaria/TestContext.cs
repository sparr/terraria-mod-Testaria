using Microsoft.Xna.Framework;
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
public sealed class TestContext : ITickingContext, IContaminationAware, ITestNotes, IDisposable
{
	private readonly List<SpawnedNpc> spawned = [];
	private readonly HashSet<int> ownedNpcs = [];
	private readonly List<string> contamination = [];
	private readonly HashSet<int> alreadyReported = [];
	private readonly List<string> escapes = [];
	private readonly List<int> spawnedPlayers = [];
	private readonly List<SpawnedProjectile> spawnedProjectiles = [];
	private readonly List<SpawnedItem> spawnedItems = [];

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
		ownedNpcs.Add(index);

		// Without this the game reclaims it within about a second: CheckActive
		// despawns anything out of range of a player, and a headless server has
		// no players, so everything is out of range.
		TestOwnership.OwnNpc(index);

		return npc;
	}

	/// <summary>
	/// Puts a player in the box, at a box-relative position.
	/// <para/>
	/// A great deal of Terraria only happens near a player: NPCs target one,
	/// biomes are measured from one, spawning and despawning are decided by
	/// distance to one. Without this, an entire category of test is
	/// unwritable, because a headless server has no players at all.
	/// <para/>
	/// The player is fabricated rather than connected. That is the same thing
	/// the game does for a joining client, <c>Main.player[i] = new Player()</c>,
	/// and vanilla keeps a dummy of its own for scene metrics, so the shape is
	/// not unusual. It is not a client: nothing is networked, drawn, or given
	/// input.
	/// </summary>
	/// <returns>The player, already active and positioned.</returns>
	public Player SpawnPlayer(int offsetX, int offsetY)
	{
		WorldPoint at = At(offsetX, offsetY);
		int slot = FreePlayerSlot();

		var player = new Player { whoAmI = slot };

		Main.player[slot] = player;
		player.active = true;
		player.name = $"TestariaPlayer{slot}";
		player.statLifeMax = 100;
		player.statLife = player.statLifeMax;
		player.statManaMax = 20;
		player.statMana = player.statManaMax;

		// position is the top-left corner, so offset by half the hitbox to put
		// the player's centre where the test asked for.
		player.position = new Vector2(at.X - (player.width / 2f), at.Y - (player.height / 2f));

		spawnedPlayers.Add(slot);

		return player;
	}

	/// <summary>
	/// A player slot nobody is using.
	/// <para/>
	/// Slot 255 is reserved: on a server <c>Main.myPlayer</c> is 255, and
	/// taking it would make the server think it is its own client.
	/// </summary>
	private static int FreePlayerSlot()
	{
		for (int i = 0; i < Main.maxPlayers && i < 255; i++) {
			if (!Main.player[i].active)
				return i;
		}

		throw new InvalidOperationException($"No free player slot: all {Main.maxPlayers} are active.");
	}

	/// <summary>
	/// Fires a projectile from a box-relative position and records it, so
	/// teardown removes it whether or not the test remembered to.
	/// <para/>
	/// A projectile with velocity will usually leave the box, which is
	/// reported as an escape rather than prevented; see
	/// <see cref="Escapes"/>. Give it no velocity to keep it still.
	/// </summary>
	/// <param name="type">The projectile type to fire.</param>
	/// <param name="offsetX">Box-relative tile column to fire from.</param>
	/// <param name="offsetY">Box-relative tile row to fire from.</param>
	/// <param name="velocity">Initial velocity in world units per tick. Default is stationary.</param>
	/// <param name="damage">Damage dealt. Zero is fine for a test that only watches movement.</param>
	/// <param name="knockback">Knockback applied on hit.</param>
	/// <param name="owner">
	/// The player slot credited with firing. Defaults to
	/// <c>Main.myPlayer</c>, which on a server is the dummy slot 255, and a
	/// great deal of projectile AI reads it. Pass a player spawned by
	/// <see cref="SpawnPlayer"/> when the projectile should belong to one.
	/// </param>
	public Projectile SpawnProjectile(
		int type,
		int offsetX,
		int offsetY,
		Vector2 velocity = default,
		int damage = 0,
		float knockback = 0f,
		int owner = -1)
	{
		WorldPoint at = At(offsetX, offsetY);

		int index = Projectile.NewProjectile(
			new EntitySource_DebugCommand("Testaria"),
			new Vector2(at.X, at.Y),
			velocity,
			type,
			damage,
			knockback,
			owner < 0 ? Main.myPlayer : owner);

		// NewProjectile returns Main.maxProjectiles when the pool is full,
		// which would index out of bounds. Worth saying plainly: a test that
		// silently got no projectile would fail somewhere far from the cause.
		if (index >= Main.maxProjectiles)
			throw new InvalidOperationException($"The projectile pool is full ({Main.maxProjectiles} slots), so type {type} could not be spawned.");

		spawnedProjectiles.Add(new SpawnedProjectile(index, type));

		return Main.projectile[index];
	}

	/// <summary>
	/// Drops an item in the world at a box-relative position, as if something
	/// had dropped it, and records it for teardown.
	/// <para/>
	/// This is a world item, not an inventory one: in 1.4.5 those are
	/// different types, and <c>Main.item</c> holds <see cref="WorldItem"/>.
	/// To give an item to a player, put it in their inventory directly.
	/// </summary>
	public WorldItem SpawnItem(int type, int offsetX, int offsetY, int stack = 1)
	{
		WorldPoint at = At(offsetX, offsetY);

		int index = Item.NewItem(
			new EntitySource_DebugCommand("Testaria"),
			new Vector2(at.X, at.Y),
			type,
			stack,
			noBroadcast: true);

		if (index >= Main.maxItems)
			throw new InvalidOperationException($"The item pool is full ({Main.maxItems} slots), so type {type} could not be dropped.");

		spawnedItems.Add(new SpawnedItem(index, type));

		return Main.item[index];
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
	public IReadOnlyList<string> Contamination => contamination;

	/// <summary>
	/// Entities of this test's own that left the box. Recorded for diagnosis
	/// but deliberately not treated as contamination: an entity leaving may be
	/// exactly what the test is observing, and dragging it back would change
	/// the behaviour under test.
	/// </summary>
	public IReadOnlyList<string> Escapes => escapes;

	/// <summary>
	/// The escapes, reported on the test's result as output rather than as a
	/// verdict. An escape is not grounds for failing the test it came from,
	/// but it is usually the explanation for whatever odd thing happens to a
	/// neighbouring box afterwards, so it has to be written down somewhere a
	/// person will see.
	/// </summary>
	IReadOnlyList<string> ITestNotes.Notes => escapes;

	/// <inheritdoc />
	public void Tick() => ElapsedTicks++;

	/// <summary>
	/// Looks for anything in the box that does not belong to this test.
	/// <para/>
	/// A blank world removes almost every source of these, which is the
	/// stronger guarantee because it cannot race. This covers what a blank
	/// world cannot: a neighbouring test's escapee, or something the game
	/// spawned on its own.
	/// <para/>
	/// Each intruder is reported once. Reporting every tick would turn one
	/// stray entity into hundreds of lines and bury the thing that mattered.
	/// <para/>
	/// Driven from <c>PreUpdateEntities</c> rather than from the runner's own
	/// tick. NPC updates, and with them <c>CheckActive</c>, run before
	/// <c>PostUpdateEverything</c>, so a watcher living there sees a world the
	/// game has already tidied. Measured: an intruder spawned into a box is
	/// gone within five ticks, and a watcher there observes none of them.
	/// </summary>
	internal void Watch()
	{
		if (Interior.IsEmpty)
			return;

		foreach (int index in BoxWatch.FindIntruders(Interior, ActiveNpcs(), ownedNpcs)) {
			if (!alreadyReported.Add(index))
				continue;

			NPC intruder = Main.npc[index];
			contamination.Add($"NPC {index} (type {intruder.type}, {intruder.FullName}) was in the box at tick {ElapsedTicks}");
		}

		foreach (int index in BoxWatch.FindEscapees(Interior, ActiveNpcs(), ownedNpcs)) {
			if (alreadyReported.Add(-index - 1))
				escapes.Add($"NPC {index} left the box at tick {ElapsedTicks}");
		}
	}

	/// <summary>
	/// Active NPCs and where they are. The pool is fixed at a couple of
	/// hundred slots, so scanning it every tick is affordable; nothing cheaper
	/// would actually answer whether something arrived.
	/// </summary>
	private static IEnumerable<(int Id, WorldPoint Position)> ActiveNpcs()
	{
		for (int i = 0; i < Main.npc.Length; i++) {
			NPC npc = Main.npc[i];

			if (npc.active)
				yield return (i, new WorldPoint(npc.Center.X, npc.Center.Y));
		}
	}

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
		foreach (int slot in spawnedPlayers) {
			// Replaced rather than merely deactivated, so no state from this
			// test survives into the slot's next occupant.
			Main.player[slot] = new Player { whoAmI = slot };
			Main.player[slot].active = false;
		}

		spawnedPlayers.Clear();
		ownedNpcs.Clear();

		foreach (SpawnedNpc record in spawned) {
			NPC npc = Main.npc[record.Index];

			TestOwnership.ReleaseNpc(record.Index);

			if (npc.active && npc.type == record.Type)
				npc.active = false;
		}

		spawned.Clear();

		foreach (SpawnedProjectile record in spawnedProjectiles) {
			Projectile projectile = Main.projectile[record.Index];

			// Deactivated rather than killed. Kill runs the projectile's death
			// behaviour, which for a good many of them means an explosion,
			// dust, sound, or spawning something else. Teardown should leave
			// no trace, not set off fireworks in the next test's box.
			if (projectile.active && projectile.type == record.Type)
				projectile.active = false;
		}

		spawnedProjectiles.Clear();

		foreach (SpawnedItem record in spawnedItems) {
			WorldItem item = Main.item[record.Index];

			if (item.active && item.type == record.Type)
				item.TurnToAir();
		}

		spawnedItems.Clear();
	}

	private (int X, int Y) Absolute(int offsetX, int offsetY)
	{
		if (offsetX < 0 || offsetY < 0 || offsetX >= Interior.Width || offsetY >= Interior.Height)
			throw new ArgumentOutOfRangeException(nameof(offsetX), $"Offset ({offsetX}, {offsetY}) lies outside this test's box {Interior}.");

		return (Interior.Left + offsetX, Interior.Top + offsetY);
	}

	private readonly record struct SpawnedNpc(int Index, int Type);

	private readonly record struct SpawnedProjectile(int Index, int Type);

	private readonly record struct SpawnedItem(int Index, int Type);
}
