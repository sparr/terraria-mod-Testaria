namespace Testaria.Tests;

public class TileCoordinatesTests
{
	[Fact]
	public void A_tile_is_sixteen_pixels()
		=> XAssert.Equal(16, TileCoordinates.TileSize);

	[Theory]
	[InlineData(0, 0f)]
	[InlineData(1, 16f)]
	[InlineData(100, 1600f)]
	public void Tile_to_world_multiplies_by_the_tile_size(int tile, float world)
		=> XAssert.Equal(world, TileCoordinates.ToWorld(tile));

	[Theory]
	[InlineData(0f, 0)]
	[InlineData(15.9f, 0)]
	[InlineData(16f, 1)]
	[InlineData(1600f, 100)]
	public void World_to_tile_divides_by_the_tile_size(float world, int tile)
		=> XAssert.Equal(tile, TileCoordinates.ToTile(world));

	[Fact]
	public void World_to_tile_floors_rather_than_truncating_toward_zero()
	{
		// Terraria itself generally writes (int)(x / 16f), which maps both -8
		// and +8 to tile 0. That never bites in a valid world, but a framework
		// deciding whether something left its box needs the answer right on
		// both sides of the origin.
		XAssert.Equal(-1, TileCoordinates.ToTile(-8f));
		XAssert.Equal(-1, TileCoordinates.ToTile(-0.1f));
		XAssert.Equal(-2, TileCoordinates.ToTile(-17f));
	}

	[Fact]
	public void Round_tripping_a_tile_through_world_coordinates_is_lossless()
	{
		for (int tile = 0; tile < 500; tile++)
			XAssert.Equal(tile, TileCoordinates.ToTile(TileCoordinates.ToWorld(tile)));
	}

	[Fact]
	public void The_centre_of_a_tile_is_half_a_tile_in()
	{
		XAssert.Equal(8f, TileCoordinates.CenterOfTile(0));
		XAssert.Equal(24f, TileCoordinates.CenterOfTile(1));
	}

	[Fact]
	public void A_tile_centre_converts_back_to_that_same_tile()
	{
		for (int tile = 0; tile < 200; tile++)
			XAssert.Equal(tile, TileCoordinates.ToTile(TileCoordinates.CenterOfTile(tile)));
	}
}

public class BoxSpaceTests
{
	private static readonly TileRect Box = new(100, 200, 80, 48);

	[Fact]
	public void The_centre_of_a_box_is_inside_it()
	{
		WorldPoint centre = BoxSpace.Center(Box);

		XAssert.True(BoxSpace.Contains(Box, centre));
		XAssert.Equal(TileCoordinates.ToWorld(100) + (80 * 16 / 2f), centre.X);
		XAssert.Equal(TileCoordinates.ToWorld(200) + (48 * 16 / 2f), centre.Y);
	}

	[Fact]
	public void An_empty_box_has_no_centre()
		=> XAssert.Throws<ArgumentException>(() => BoxSpace.Center(default));

	[Fact]
	public void Offsets_are_relative_to_the_box_not_the_world()
	{
		// A test says "eight tiles in, three down" without knowing where the
		// arena actually placed its box.
		WorldPoint at = BoxSpace.At(Box, 8, 3);

		XAssert.Equal(TileCoordinates.CenterOfTile(108), at.X);
		XAssert.Equal(TileCoordinates.CenterOfTile(203), at.Y);
	}

	[Fact]
	public void The_origin_offset_is_the_first_tile_of_the_box()
	{
		WorldPoint at = BoxSpace.At(Box, 0, 0);

		XAssert.Equal(100, TileCoordinates.ToTile(at.X));
		XAssert.Equal(200, TileCoordinates.ToTile(at.Y));
	}

	[Theory]
	[InlineData(-1, 0)]
	[InlineData(0, -1)]
	[InlineData(80, 0)]
	[InlineData(0, 48)]
	public void An_offset_outside_the_box_is_rejected(int x, int y)
		=> XAssert.Throws<ArgumentOutOfRangeException>(() => BoxSpace.At(Box, x, y));

	[Fact]
	public void Every_in_range_offset_lands_inside_the_box()
	{
		for (int x = 0; x < Box.Width; x++) {
			for (int y = 0; y < Box.Height; y++)
				XAssert.True(BoxSpace.Contains(Box, BoxSpace.At(Box, x, y)), $"offset {x},{y} landed outside");
		}
	}

	[Fact]
	public void Positions_outside_the_box_are_not_contained()
	{
		XAssert.False(BoxSpace.Contains(Box, TileCoordinates.CenterOfTile(99, 200)));
		XAssert.False(BoxSpace.Contains(Box, TileCoordinates.CenterOfTile(180, 200)));
		XAssert.False(BoxSpace.Contains(Box, TileCoordinates.CenterOfTile(100, 248)));
	}

	[Fact]
	public void Clamping_brings_an_escapee_back_inside()
	{
		// What the warden does to an entity that drifted out: report the
		// escape, then bring it home so it stops disturbing a neighbour.
		WorldPoint escaped = TileCoordinates.CenterOfTile(500, 900);
		WorldPoint home = BoxSpace.Clamp(Box, escaped);

		XAssert.True(BoxSpace.Contains(Box, home));
	}

	[Fact]
	public void Clamping_lands_on_the_last_tile_inside_not_the_first_outside()
	{
		// Off by one here would put the entity in the gutter, which is the
		// neighbour's problem rather than this box's.
		WorldPoint home = BoxSpace.Clamp(Box, new WorldPoint(999_999f, 999_999f));

		XAssert.Equal(Box.Right - 1, TileCoordinates.ToTile(home.X));
		XAssert.Equal(Box.Bottom - 1, TileCoordinates.ToTile(home.Y));
	}

	[Fact]
	public void Clamping_a_position_already_inside_leaves_it_alone()
	{
		WorldPoint inside = BoxSpace.At(Box, 5, 5);

		XAssert.Equal(inside, BoxSpace.Clamp(Box, inside));
	}

	[Fact]
	public void Clamping_handles_the_low_edge_too()
	{
		WorldPoint home = BoxSpace.Clamp(Box, new WorldPoint(-5000f, -5000f));

		XAssert.Equal(Box.Left, TileCoordinates.ToTile(home.X));
		XAssert.Equal(Box.Top, TileCoordinates.ToTile(home.Y));
	}

	[Fact]
	public void Clamping_into_an_empty_box_is_rejected()
		=> XAssert.Throws<ArgumentException>(() => BoxSpace.Clamp(default, default));

	[Fact]
	public void A_world_point_renders_for_diagnostics()
		=> XAssert.Equal("1608,3208", new WorldPoint(1608f, 3208f).ToString());
}
