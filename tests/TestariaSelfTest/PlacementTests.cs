using System.Collections;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// Watches for tile placements the mod loader announces.
/// <para/>
/// A <c>GlobalTile</c> rather than a <c>ModTile</c> so that no texture, map
/// entry or localisation key is needed: the hook fires for vanilla tiles just
/// the same, and the hook is the whole subject.
/// </summary>
public sealed class PlacementWatcher : GlobalTile
{
	/// <summary>Every placement the loader has announced since the last reset.</summary>
	public static readonly List<(int X, int Y, int Type)> Placements = [];

	public override void PlaceInWorld(int i, int j, int type, Item item)
		=> Placements.Add((i, j, type));
}

/// <summary>
/// That a test can place a tile the way a player does.
/// <para/>
/// <c>WorldGen.PlaceTile</c> puts a tile in the world and tells nobody.
/// tModLoader's placement hook is called from <c>Player</c> alone, when
/// somebody places a tile from an item, so a mod whose behaviour hangs off
/// that hook, and a great many do, is unreachable from a test that only has
/// the ordinary helper. Found against InnoVault, which creates its tile
/// entities there and so never created one for a test.
/// </summary>
public class PlacementTests
{
	private const int X = 5;
	private const int Y = 5;

	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator Placing_a_tile_the_ordinary_way_announces_nothing(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		PlacementWatcher.Placements.Clear();
		box.Restore(PlacementWatcher.Placements.Clear);

		box.ClearTile(X, Y);
		box.PlaceTile(X, Y, TileID.Glass);

		yield return Wait.Ticks(2);

		Assert.Equal(TileID.Glass, (int)box.GetTile(X, Y).TileType);
		// The documented limitation, pinned so that a change to PlaceTile
		// announces itself here rather than in somebody's flaky suite.
		Assert.Empty(PlacementWatcher.Placements);
	}

	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator Placing_a_tile_as_a_player_announces_it(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		PlacementWatcher.Placements.Clear();
		box.Restore(PlacementWatcher.Placements.Clear);

		box.ClearTile(X, Y);

		Assert.True(box.PlaceTileAsPlayer(X, Y, TileID.Glass), "the tile should have been placed");

		yield return Wait.Ticks(2);

		Assert.Equal(1, PlacementWatcher.Placements.Count);

		(int x, int y, int type) = PlacementWatcher.Placements[0];

		Assert.Equal(ctx.Interior.Left + X, x);
		Assert.Equal(ctx.Interior.Top + Y, y);
		Assert.Equal(TileID.Glass, type);
	}

	/// <summary>
	/// Placement can fail: the space may be occupied, or the tile may need
	/// support it does not have. Saying so beats announcing a placement that
	/// did not happen, which would have every listener acting on a tile that
	/// is not there.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator A_placement_that_does_not_take_is_not_announced(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		PlacementWatcher.Placements.Clear();
		box.Restore(PlacementWatcher.Placements.Clear);

		// Stone first, then try to put glass in the same space.
		box.PlaceTile(X, Y, TileID.Stone);

		yield return Wait.Ticks(1);

		if (box.PlaceTileAsPlayer(X, Y, TileID.Glass))
			Assert.Skip("This build of the game let glass replace stone, so there is no failed placement to check.");

		Assert.Empty(PlacementWatcher.Placements);
	}
}
