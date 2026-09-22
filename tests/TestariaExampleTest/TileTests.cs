using System.Collections;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// Tiles, including placing the subject's own into a live world.
/// </summary>
public class TileTests
{
	[LoadedTest]
	public void Every_tile_has_a_name()
	{
		foreach (ModTile tile in Subject.Content<ModTile>())
			Assert.False(string.IsNullOrWhiteSpace(tile.Name), $"a tile with id {tile.Type} has no name");
	}

	[LoadedTest]
	public void Tile_ids_fit_the_arrays_sized_to_hold_them()
	{
		// Vanilla arrays are resized at load to fit modded content. An id past
		// the end means something did not get resized, and the symptom is an
		// index-out-of-range deep in drawing code.
		foreach (ModTile tile in Subject.Content<ModTile>()) {
			Assert.True(tile.Type < TileLoader.TileCount, $"tile '{tile.Name}' has id {tile.Type} but only {TileLoader.TileCount} tiles are registered");
			Assert.True(tile.Type < Main.tileSolid.Length, $"tile '{tile.Name}' id {tile.Type} is past the end of Main.tileSolid");
		}
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_modded_tile_can_be_placed_and_read_back(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		Assert.True(ModContent.TryFind(Subject.Name, "ExampleBlock", out ModTile block));

		box.ClearTile(4, 4);
		yield return Wait.Ticks(2);

		box.PlaceTile(4, 4, block.Type);
		yield return Wait.Ticks(2);

		Tile placed = box.GetTile(4, 4);

		Assert.True(placed.HasTile, $"tile '{block.Name}' did not appear after being placed");
		Assert.Equal(block.Type, placed.TileType);
	}

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator Clearing_a_tile_leaves_nothing_behind(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		Assert.True(ModContent.TryFind(Subject.Name, "ExampleBlock", out ModTile block));

		box.PlaceTile(6, 6, block.Type);
		yield return Wait.Ticks(2);

		box.ClearTile(6, 6);
		yield return Wait.Ticks(2);

		Assert.False(box.GetTile(6, 6).HasTile, "the tile survived being cleared");
	}
}
