using System.Collections;
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
