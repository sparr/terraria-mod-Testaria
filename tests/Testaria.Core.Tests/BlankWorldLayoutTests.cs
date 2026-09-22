namespace Testaria.Tests;

public class BlankWorldLayoutTests
{
	private static BlankWorldLayout Small() => BlankWorldLayout.For(4200, 1200);

	[Theory]
	[InlineData(4200, 1200)]
	[InlineData(6400, 1800)]
	[InlineData(8400, 2400)]
	public void Bands_partition_the_world_for_every_standard_size(int x, int y)
	{
		WorldGeometry geo = BlankWorldLayout.For(x, y).Geometry;

		for (int row = 0; row < geo.MaxTilesY; row++)
			XAssert.NotEqual(Band.None, geo.BandAt(row));
	}

	[Fact]
	public void The_underworld_starts_where_vanilla_puts_it()
	{
		// Depth-sensitive behaviour should line up with a real world even
		// though nothing else about a blank one does.
		XAssert.Equal(1200 - 200, Small().Geometry.UnderworldTop);
	}

	[Fact]
	public void The_geometry_survives_being_read_back_out_of_the_generated_world()
	{
		// BlankWorldPass writes UndergroundTop into Main.worldSurface and
		// CavernTop into Main.rockLayer, and everything that later asks the
		// live world where its bands are goes through FromTerrariaValues on
		// exactly those two fields. If the layout and that conversion disagree
		// then the arena and the game disagree about which rows are which, and
		// the boundary that can drift is the space one: the game keeps deciding
		// what is sky with SpaceFraction of Main.worldSurface whatever a layout
		// would have preferred.
		BlankWorldLayout layout = Small();

		WorldGeometry readBack = WorldGeometry.FromTerrariaValues(
			layout.Geometry.MaxTilesX,
			layout.Geometry.MaxTilesY,
			layout.Geometry.UndergroundTop,
			layout.Geometry.CavernTop,
			layout.Geometry.UnderworldTop);

		XAssert.Equal(layout.Geometry, readBack);
	}

	[Fact]
	public void The_surface_level_is_the_top_of_the_underground_band()
	{
		BlankWorldLayout layout = Small();

		XAssert.Equal(layout.Geometry.UndergroundTop, layout.SurfaceLevel);
	}

	[Fact]
	public void Spawn_stands_on_the_ground_rather_than_inside_it()
	{
		// One row above the first solid row, or the player suffocates.
		BlankWorldLayout layout = Small();

		XAssert.Equal(layout.SurfaceLevel - 1, layout.SpawnTileY);
		XAssert.Equal(Band.Surface, layout.Geometry.BandAt(layout.SpawnTileY));
	}

	[Fact]
	public void Spawn_sits_inside_the_reserved_column()
	{
		BlankWorldLayout layout = Small();
		ReservedArea spawn = layout.Reserved.Single();

		XAssert.True(spawn.Bounds.Contains(layout.SpawnTileX, layout.SpawnTileY));
	}

	[Fact]
	public void The_reserved_column_is_full_height_and_at_one_edge()
	{
		// Contiguous reserved ground keeps the test region a simple span.
		BlankWorldLayout layout = Small();
		TileRect bounds = layout.Reserved.Single().Bounds;

		XAssert.Equal(0, bounds.Left);
		XAssert.Equal(0, bounds.Top);
		XAssert.Equal(layout.Geometry.MaxTilesY, bounds.Height);
		XAssert.Equal(BlankWorldLayout.ReservedWidth, bounds.Width);
	}

	[Fact]
	public void The_reserved_area_explains_itself()
	{
		ReservedArea spawn = Small().Reserved.Single();

		XAssert.Equal(BlankWorldLayout.SpawnAreaName, spawn.Name);
		XAssert.Contains("never leased", spawn.Reason!, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void An_arena_built_on_the_layout_leases_clear_of_the_reserved_column()
	{
		BlankWorldLayout layout = Small();
		var arena = new Arena(layout.Geometry, new ArenaOptions { Reserved = layout.Reserved });

		for (int i = 0; i < 10; i++) {
			BoxLease lease = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
			XAssert.True(lease.Bounds.Left >= BlankWorldLayout.ReservedWidth, $"lease at {lease.Bounds} is inside reserved ground");
		}
	}

	[Fact]
	public void Boxes_in_the_cavern_band_sit_below_the_surface_level()
	{
		// Which is to say, in stone rather than in mid air.
		BlankWorldLayout layout = Small();
		var arena = new Arena(layout.Geometry, new ArenaOptions { Reserved = layout.Reserved });
		BoxLease lease = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		XAssert.True(lease.Bounds.Top >= layout.SurfaceLevel);
	}

	[Fact]
	public void The_layout_is_deterministic()
	{
		// No seed, no randomness: the same size is always the same world.
		// Compared component-wise rather than as whole records, because a
		// record holding a list compares that list by reference.
		BlankWorldLayout a = BlankWorldLayout.For(4200, 1200);
		BlankWorldLayout b = BlankWorldLayout.For(4200, 1200);

		XAssert.Equal(a.Geometry, b.Geometry);
		XAssert.Equal(a.SurfaceLevel, b.SurfaceLevel);
		XAssert.Equal(a.SpawnTileX, b.SpawnTileX);
		XAssert.Equal(a.SpawnTileY, b.SpawnTileY);
		XAssert.Equal(a.Reserved.Select(r => r.Bounds), b.Reserved.Select(r => r.Bounds));
		XAssert.Equal(a.Reserved.Select(r => r.Name), b.Reserved.Select(r => r.Name));
	}

	[Theory]
	[InlineData(100, 1200)]
	[InlineData(4200, 300)]
	public void Worlds_too_small_to_be_valid_are_rejected(int x, int y)
		=> XAssert.Throws<ArgumentOutOfRangeException>(() => BlankWorldLayout.For(x, y));
}
