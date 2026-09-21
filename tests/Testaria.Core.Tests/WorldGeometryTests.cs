namespace Testaria.Tests;

public class WorldGeometryTests
{
	/// <summary>Representative small world: 4200x1200, underworld at maxTilesY - 200.</summary>
	private static WorldGeometry Small() => new(4200, 1200, 87, 250, 400, 1000);

	[Fact]
	public void Bands_partition_the_world_exactly_once_each()
	{
		WorldGeometry geo = Small();

		// Every row belongs to exactly one band, with no gaps and no overlaps.
		for (int y = 0; y < geo.MaxTilesY; y++)
			XAssert.NotEqual(Band.None, geo.BandAt(y));

		XAssert.Equal(Band.Space, geo.BandAt(0));
		XAssert.Equal(Band.Space, geo.BandAt(86));
		XAssert.Equal(Band.Surface, geo.BandAt(87));
		XAssert.Equal(Band.Surface, geo.BandAt(249));
		XAssert.Equal(Band.Underground, geo.BandAt(250));
		XAssert.Equal(Band.Cavern, geo.BandAt(400));
		XAssert.Equal(Band.Underworld, geo.BandAt(1000));
		XAssert.Equal(Band.Underworld, geo.BandAt(1199));
	}

	[Fact]
	public void BandAt_returns_None_outside_the_world()
	{
		WorldGeometry geo = Small();

		XAssert.Equal(Band.None, geo.BandAt(-1));
		XAssert.Equal(Band.None, geo.BandAt(geo.MaxTilesY));
	}

	[Fact]
	public void Band_bounds_are_full_width_and_abut_without_gaps()
	{
		WorldGeometry geo = Small();

		TileRect surface = geo.BandBounds(Band.Surface);
		TileRect underground = geo.BandBounds(Band.Underground);

		XAssert.Equal(0, surface.Left);
		XAssert.Equal(geo.MaxTilesX, surface.Width);
		XAssert.Equal(surface.Bottom, underground.Top);
	}

	[Fact]
	public void BandBounds_rejects_a_band_set_or_none()
	{
		WorldGeometry geo = Small();

		XAssert.Throws<ArgumentException>(() => geo.BandBounds(Band.Surface | Band.Cavern));
		XAssert.Throws<ArgumentException>(() => geo.BandBounds(Band.None));
	}

	[Fact]
	public void Constructor_rejects_out_of_order_boundaries()
	{
		// rockLayer above worldSurface is physically impossible and would
		// produce negative-height bands that quietly break every containment
		// check downstream.
		XAssert.Throws<ArgumentException>(() => new WorldGeometry(4200, 1200, 87, 400, 250, 1000));
	}

	[Fact]
	public void Constructor_rejects_non_positive_dimensions()
	{
		XAssert.Throws<ArgumentException>(() => new WorldGeometry(0, 1200, 0, 0, 0, 0));
		XAssert.Throws<ArgumentException>(() => new WorldGeometry(4200, 0, 0, 0, 0, 0));
	}

	[Fact]
	public void Constructor_accepts_degenerate_but_ordered_boundaries()
	{
		// A world with no space band at all is odd but not invalid.
		var geo = new WorldGeometry(4200, 1200, 0, 250, 400, 1000);

		XAssert.True(geo.BandBounds(Band.Space).IsEmpty);
	}

	[Fact]
	public void Fill_leaves_a_single_band_alone()
		=> XAssert.Equal(Band.Cavern, WorldGeometry.Fill(Band.Cavern));

	[Fact]
	public void Fill_closes_a_gap_because_a_column_cannot_skip_a_band()
	{
		// A column from surface to cavern physically passes through the
		// underground whether or not the author listed it.
		XAssert.Equal(
			Band.Surface | Band.Underground | Band.Cavern,
			WorldGeometry.Fill(Band.Surface | Band.Cavern));
	}

	[Fact]
	public void Fill_leaves_an_already_contiguous_run_alone()
	{
		Band run = Band.Underground | Band.Cavern;

		XAssert.Equal(run, WorldGeometry.Fill(run));
	}

	[Fact]
	public void Fill_of_none_is_none()
		=> XAssert.Equal(Band.None, WorldGeometry.Fill(Band.None));

	[Fact]
	public void Span_of_a_gapped_set_equals_span_of_the_filled_set()
	{
		WorldGeometry geo = Small();

		XAssert.Equal(
			geo.Span(Band.Surface | Band.Underground | Band.Cavern),
			geo.Span(Band.Surface | Band.Cavern));
	}

	[Fact]
	public void Span_runs_from_the_top_of_the_first_band_to_the_bottom_of_the_last()
	{
		WorldGeometry geo = Small();

		TileRect span = geo.Span(Band.Surface | Band.Cavern);

		XAssert.Equal(geo.SurfaceTop, span.Top);
		XAssert.Equal(geo.UnderworldTop, span.Bottom);
		XAssert.Equal(geo.MaxTilesX, span.Width);
	}

	[Fact]
	public void Span_of_all_bands_covers_the_whole_world()
	{
		WorldGeometry geo = Small();

		TileRect span = geo.Span(Band.All);

		XAssert.Equal(0, span.Top);
		XAssert.Equal(geo.MaxTilesY, span.Bottom);
	}

	[Fact]
	public void Span_of_none_is_empty()
		=> XAssert.True(Small().Span(Band.None).IsEmpty);

	[Fact]
	public void Span_height_tracks_world_height_which_is_why_spanning_tests_must_pin_world_size()
	{
		// PLAN.md section 2.4: band boundaries derive from Main.maxTilesY, so
		// the same spanning test gets a different box in a different world.
		var small = new WorldGeometry(4200, 1200, 87, 250, 400, 1000);
		var large = new WorldGeometry(8400, 2400, 175, 500, 800, 2200);

		XAssert.NotEqual(small.Span(Band.All).Height, large.Span(Band.All).Height);
	}
}

public class WorldGeometryFromTerrariaValuesTests
{
	// Representative values for Terraria's three world sizes. worldSurface and
	// rockLayer are doubles in the game, hence the fractional inputs.
	[Theory]
	[InlineData(4200, 1200, 249.0, 399.0, 1000)]
	[InlineData(6400, 1800, 375.0, 600.0, 1600)]
	[InlineData(8400, 2400, 500.0, 800.0, 2200)]
	public void Produces_a_geometry_whose_bands_partition_the_world(
		int maxX, int maxY, double surface, double rock, int underworld)
	{
		WorldGeometry geo = WorldGeometry.FromTerrariaValues(maxX, maxY, surface, rock, underworld);

		for (int y = 0; y < geo.MaxTilesY; y++)
			XAssert.NotEqual(Band.None, geo.BandAt(y));

		XAssert.Equal(maxX, geo.MaxTilesX);
		XAssert.Equal(underworld, geo.UnderworldTop);
	}

	[Fact]
	public void Truncates_rather_than_rounds_the_fractional_boundaries()
	{
		// Terraria compares against the raw double, so a boundary that lands
		// mid-tile belongs to the band above it.
		WorldGeometry geo = WorldGeometry.FromTerrariaValues(4200, 1200, 249.9, 399.9, 1000);

		XAssert.Equal(249, geo.UndergroundTop);
		XAssert.Equal(399, geo.CavernTop);
	}

	[Fact]
	public void Space_fraction_places_the_space_boundary_proportionally()
	{
		WorldGeometry geo = WorldGeometry.FromTerrariaValues(4200, 1200, 400.0, 600.0, 1000, spaceFraction: 0.25);

		XAssert.Equal(100, geo.SurfaceTop);
	}

	[Fact]
	public void A_space_fraction_of_zero_leaves_no_space_band()
	{
		WorldGeometry geo = WorldGeometry.FromTerrariaValues(4200, 1200, 400.0, 600.0, 1000, spaceFraction: 0);

		XAssert.True(geo.BandBounds(Band.Space).IsEmpty);
		XAssert.Equal(Band.Surface, geo.BandAt(0));
	}

	[Theory]
	[InlineData(-0.1)]
	[InlineData(1.1)]
	public void An_out_of_range_space_fraction_is_rejected(double fraction)
		=> XAssert.Throws<ArgumentOutOfRangeException>(
			() => WorldGeometry.FromTerrariaValues(4200, 1200, 400.0, 600.0, 1000, fraction));

	[Fact]
	public void Inconsistent_game_values_are_rejected_rather_than_producing_negative_bands()
	{
		// If Main is read before a world finishes loading these can be zero or
		// inconsistent; failing loudly beats an arena built on nonsense.
		XAssert.Throws<ArgumentException>(
			() => WorldGeometry.FromTerrariaValues(4200, 1200, 600.0, 400.0, 1000));
	}
}
