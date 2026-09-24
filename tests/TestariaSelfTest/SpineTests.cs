using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// The smallest tests that prove the framework's spine works end to end.
/// <para/>
/// Deliberately not interesting. Their job is to exercise discovery, the
/// runner, the arena, the scheduler, and the reporter against a live game, so
/// that when a real mod's tests fail there is one unknown to debug rather than
/// two.
/// </summary>
public class SpineTests
{
	[LoadedTest]
	public void The_loader_has_run_and_knows_about_this_mod()
	{
		// A Tier 1 assertion by construction: ModLoader.Mods is empty outside a
		// loaded game, so this passing means the load pass really completed.
		Assert.NotEmpty(ModLoader.Mods);
		Assert.Contains("Testaria", ModLoader.Mods.Select(m => m.Name));
	}

	[LoadedTest]
	public void The_core_can_tell_that_a_game_is_underneath_it()
	{
		// The runtime half of the Tier 0 boundary. GameState.IsLoaded is false
		// in every dotnet test host, asserted there; this is the other
		// direction, and the only place it can be asserted, since it is the
		// Testaria mod's own PostSetupContent that raises the flag.
		Assert.True(GameState.IsLoaded, "the load pass has finished, so the core should know it");

		// And the guard it exists to power says nothing when the game is real.
		GameState.Require("ContentSamples");
	}

	[LoadedTest]
	public void Vanilla_content_is_registered()
	{
		Assert.True(ItemID.Count > 0, "vanilla item IDs should be registered");
		Assert.NotEmpty(ContentSamples.ItemsByType);
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 120)]
	public IEnumerator A_world_is_loaded_and_the_box_is_inside_it(ITestContext ctx)
	{
		Assert.True(Main.maxTilesX > 0, "a world should be loaded");
		Assert.InRange(ctx.Interior.Left, 0, Main.maxTilesX);
		Assert.InRange(ctx.Interior.Top, 0, Main.maxTilesY);
		Assert.Equal(Band.Cavern, ctx.Bands);

		yield break;
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_tile_placed_in_the_box_can_be_read_back(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		box.ClearTile(4, 4);
		yield return Wait.Ticks(2);

		box.PlaceTile(4, 4, TileID.Stone);
		yield return Wait.Ticks(2);

		Tile tile = box.GetTile(4, 4);

		Assert.True(tile.HasTile, "the tile should exist after placing it");
		Assert.Equal(TileID.Stone, tile.TileType);
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator Ticks_actually_pass_between_yields(ITestContext ctx)
	{
		int before = ctx.ElapsedTicks;

		yield return Wait.Ticks(30);

		Assert.True(ctx.ElapsedTicks > before, $"expected ticks to advance, saw {before} then {ctx.ElapsedTicks}");
	}
}

/// <summary>
/// Spawning the things a test needs to act on, other than NPCs.
/// </summary>
public class SpawningTests
{
	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_projectile_can_be_fired_and_stays_put_without_velocity(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		Projectile projectile = box.SpawnProjectile(ProjectileID.WoodenArrowFriendly, 8, 8);

		Assert.True(projectile.active, "the projectile should be active immediately after spawning");
		Assert.Equal(ProjectileID.WoodenArrowFriendly, projectile.type);

		yield return Wait.Ticks(2);

		int tileX = TileCoordinates.ToTile(projectile.Center.X);
		int tileY = TileCoordinates.ToTile(projectile.Center.Y);

		Assert.True(ctx.Interior.Contains(tileX, tileY), $"projectile at {tileX},{tileY} is outside the box {ctx.Interior}");
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_projectile_given_velocity_actually_moves(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		Projectile projectile = box.SpawnProjectile(ProjectileID.WoodenArrowFriendly, 4, 8, new Vector2(1f, 0f));
		float startX = projectile.position.X;

		yield return Wait.Until(() => projectile.position.X != startX, "the arrow has moved");

		Assert.True(projectile.position.X > startX, "an arrow fired to the right should move right");
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator An_item_can_be_dropped_in_the_world(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		WorldItem item = box.SpawnItem(ItemID.DirtBlock, 8, 4, stack: 7);

		Assert.True(item.active, "the item should be active immediately after dropping it");
		Assert.Equal(ItemID.DirtBlock, item.type);
		Assert.Equal(7, item.stack);

		yield return Wait.Ticks(2);

		Assert.True(item.active, "the item should still exist a couple of ticks later");
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_spawn_outside_the_box_is_refused(ITestContext ctx)
	{
		// The box is the whole point. Silently spawning into a neighbour would
		// make that neighbour fail for no visible reason.
		var box = (TestContext)ctx;

		Assert.Throws<ArgumentOutOfRangeException>(() => box.SpawnProjectile(ProjectileID.WoodenArrowFriendly, 9999, 9999));
		Assert.Throws<ArgumentOutOfRangeException>(() => box.SpawnItem(ItemID.DirtBlock, -1, 0));

		yield break;
	}
}

/// <summary>
/// Snapshotting a box, so a test can assert that something changed nothing.
/// <para/>
/// Asserting that one particular effect did not happen is weaker than it
/// looks: it passes when the code under test does something else entirely.
/// </summary>
public class SnapshotTests
{
	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator An_untouched_box_reports_no_changes(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		BoxSnapshot before = box.Snapshot();

		yield return Wait.Ticks(3);

		Assert.Empty(before.ChangesTo(box.Snapshot()));
		box.AssertUnchanged(before);
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_placed_tile_is_noticed(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		box.ClearTile(6, 6);
		yield return Wait.Ticks(2);

		BoxSnapshot before = box.Snapshot();
		box.PlaceTile(6, 6, TileID.Stone);

		yield return Wait.Ticks(2);

		Assert.NotEmpty(before.ChangesTo(box.Snapshot()));
		Assert.Throws<AssertionException>(() => box.AssertUnchanged(before));
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_spawned_entity_is_noticed(ITestContext ctx)
	{
		// Tiles are the obvious thing to watch, but a side effect that spawns
		// something is just as much of a side effect.
		var box = (TestContext)ctx;
		BoxSnapshot before = box.Snapshot();

		box.SpawnNPC(NPCID.BlueSlime, 8, 8);

		IReadOnlyList<string> changes = before.ChangesTo(box.Snapshot());

		Assert.NotEmpty(changes);
		Assert.Contains("NPCs in the box", string.Join("\n", changes));

		yield break;
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_removed_tile_is_noticed(ITestContext ctx)
	{
		// The case a "is it still there" check would catch, and the one a
		// snapshot must not miss either.
		var box = (TestContext)ctx;

		box.ClearTile(6, 6);
		box.PlaceTile(6, 6, TileID.Stone);
		yield return Wait.Ticks(2);

		BoxSnapshot before = box.Snapshot();
		box.ClearTile(6, 6);
		yield return Wait.Ticks(2);

		Assert.NotEmpty(before.ChangesTo(box.Snapshot()));
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator Paint_and_wiring_are_noticed(ITestContext ctx)
	{
		// Neither changes what tile is there, so a snapshot watching only
		// tile type would call a repainted, rewired box untouched.
		var box = (TestContext)ctx;

		box.ClearTile(6, 6);
		box.PlaceTile(6, 6, TileID.Stone);
		yield return Wait.Ticks(2);

		BoxSnapshot beforePaint = box.Snapshot();
		box.GetTile(6, 6).TileColor = PaintID.RedPaint;

		Assert.NotEmpty(beforePaint.ChangesTo(box.Snapshot()));

		BoxSnapshot beforeWire = box.Snapshot();
		box.GetTile(6, 6).RedWire = true;

		Assert.NotEmpty(beforeWire.ChangesTo(box.Snapshot()));
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator One_entity_swapped_for_another_is_noticed(ITestContext ctx)
	{
		// A count would stay at one throughout and see nothing.
		var box = (TestContext)ctx;

		NPC slime = box.SpawnNPC(NPCID.BlueSlime, 8, 8);
		yield return Wait.Ticks(1);

		BoxSnapshot before = box.Snapshot();

		slime.active = false;
		box.SpawnNPC(NPCID.Zombie, 8, 8);

		IReadOnlyList<string> changes = before.ChangesTo(box.Snapshot());

		Assert.NotEmpty(changes);
		Assert.Contains("NPCs in the box", string.Join("\n", changes));
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator The_failure_says_what_changed(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		box.ClearTile(4, 4);
		yield return Wait.Ticks(2);

		BoxSnapshot before = box.Snapshot();
		box.PlaceTile(4, 4, TileID.Stone);
		yield return Wait.Ticks(2);

		AssertionException ex = Assert.Throws<AssertionException>(
			() => box.AssertUnchanged(before, "nothing should have happened"));

		Assert.Contains("nothing should have happened", ex.Message);
		Assert.Contains("tile 4,4", ex.Message);
	}
}

/// <summary>
/// An entity of the test's own leaving the box.
/// <para/>
/// Not a failure. A test may be observing exactly that, and hauling the
/// entity back would change the behaviour under test. What the framework owes
/// is a record, so that a neighbouring box behaving oddly a few tests later
/// has a visible cause.
/// </summary>
public class EscapeTests
{
	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 600)]
	public IEnumerator An_entity_that_leaves_its_box_is_noticed(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		Assert.Empty(box.Escapes);

		NPC npc = box.SpawnNPC(NPCID.BlueSlime, 4, 4);

		// Straight out of the side, rather than waiting for one to wander: the
		// test is about the watching, not about slime pathfinding.
		npc.position.X = (ctx.Bounds.Left - 10) * 16f;

		yield return Wait.Until(() => box.Escapes.Count > 0, "the slime is noticed outside the box");

		Assert.NotEmpty(box.Escapes);
		Assert.Contains("left the box", box.Escapes[0]);
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator An_entity_that_stays_put_is_not_reported(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		box.SpawnNPC(NPCID.BlueSlime, 4, 4);

		yield return Wait.Ticks(30);

		Assert.Empty(box.Escapes);
	}
}

/// <summary>
/// A test that wants a world nobody else has touched.
/// <para/>
/// Kept separate to make the mechanism visible: an ordinary run reports this
/// as skipped, because a runner sharing its world with four other tests cannot
/// honestly claim to have provided a fresh one. <c>scripts/run-fresh.sh</c>
/// gives it a process and a world of its own, and then it runs.
/// </summary>
public class FreshWorldTests
{
	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	[FreshWorld]
	public IEnumerator Gets_a_world_of_its_own(ITestContext ctx)
	{
		Assert.True(Main.maxTilesX > 0, "a world should be loaded");
		Assert.False(ctx.Interior.IsEmpty, "a fresh-world test should still get a box");

		yield break;
	}
}

/// <summary>
/// A player in the box, which a great deal of Terraria requires before it will
/// do anything at all.
/// </summary>
public class PlayerTests
{
	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_player_can_be_put_in_the_box(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		Player player = box.SpawnPlayer(8, 8);

		Assert.True(player.active, "the player should be active immediately");
		Assert.True(player.statLife > 0, "the player should be alive");

		yield return Wait.Ticks(2);

		Assert.True(player.active, "the player should still be active after a couple of ticks");
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_player_lands_inside_its_box(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		Player player = box.SpawnPlayer(8, 8);

		yield return Wait.Ticks(1);

		int tileX = TileCoordinates.ToTile(player.Center.X);
		int tileY = TileCoordinates.ToTile(player.Center.Y);

		Assert.True(ctx.Interior.Contains(tileX, tileY), $"player is at {tileX},{tileY}, outside the box {ctx.Interior}");
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 600)]
	public IEnumerator A_player_does_not_attract_a_crowd(ITestContext ctx)
	{
		// The reason natural spawning is suppressed during a run. It is driven
		// entirely by proximity to a player, so it never happened while the
		// server had none; the moment a test puts one in its box, the game
		// would start populating the area. Those arrivals are contamination,
		// and this test failing as an error is what would report them.
		var box = (TestContext)ctx;

		box.SpawnPlayer(8, 8);

		yield return Wait.Seconds(3);

		Assert.Empty(box.Contamination);
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator No_player_survives_the_test_that_made_it(ITestContext ctx)
	{
		// Every test tears its players down, so whichever test runs first sees
		// no fabricated player, and so does every test after it.
		//
		// A genuinely connected client owns a player slot too, and that one is
		// nobody's to tear down: it belongs to the other process and stays for
		// as long as it is connected. Counting it made this test fail the
		// first time the whole suite was run with a client attached, which
		// took until tier 3 grew tests outside the one class check-net.sh
		// filtered to.
		int fabricated = 0;

		for (int i = 0; i < Main.maxPlayers; i++) {
			if (Main.player[i].active && !Netplay.Clients[i].IsActive)
				fabricated++;
		}

		Assert.Equal(0, fabricated);

		yield break;
	}
}

/// <summary>
/// What having a player makes possible, which is the point of fabricating one.
/// </summary>
public class PlayerDrivenTests
{
	[GameTest(Band = Band.Cavern, Width = 96, Height = 64, Timeout = 900)]
	public IEnumerator An_npc_notices_a_player(ITestContext ctx)
	{
		// Unwritable before there was a player: an NPC on a server with nobody
		// on it has nothing to target, so any test of aggression, pathing or
		// aggro range had no subject at all.
		var box = (TestContext)ctx;

		Player player = box.SpawnPlayer(8, 20);
		NPC zombie = box.SpawnNPC(NPCID.Zombie, 24, 20);

		yield return Wait.Seconds(2);

		Assert.True(zombie.active, "the zombie should still be around");
		Assert.Equal(player.whoAmI, zombie.target);
	}

	[GameTest(Band = Band.Cavern, Width = 96, Height = 64, Timeout = 900)]
	public IEnumerator A_player_can_be_hurt_and_healed(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		Player player = box.SpawnPlayer(8, 8);

		int full = player.statLife;
		player.statLife = full / 2;

		yield return Wait.Ticks(5);

		Assert.Equal(full / 2, player.statLife);

		player.statLife = full;
		yield return Wait.Ticks(5);

		Assert.Equal(full, player.statLife);
	}
}
