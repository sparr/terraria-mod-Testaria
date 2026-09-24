using Xunit;

namespace TestariaUnitTests;

/// <summary>
/// Tier 0: pure logic, no game, milliseconds.
/// <para/>
/// This is an ordinary xUnit project. Testaria does not replace xUnit here,
/// and you can use NUnit, MSTest or TUnit instead if you prefer; what matters
/// is that nothing in this project can reach loader state.
/// <para/>
/// Move a test up a tier the moment it wants <c>Main</c>, <c>ModContent</c> or
/// <c>ContentSamples</c>. Those do not fail cleanly outside a loaded game:
/// <c>ContentSamples.ItemsByType</c> is simply empty and <c>ItemID.Count</c>
/// is the vanilla total, so a test asserting on them passes while proving
/// nothing.
/// <para/>
/// If you do add a tModLoader reference to this project, the tier 0 boundary
/// analyzer that ships with <c>Testaria.Core</c> turns that mistake into
/// error TSTA001 at build time rather than a green test that proves nothing.
/// </summary>
public class ExampleUnitTests
{
	[Fact]
	public void Pure_logic_needs_no_game()
	{
		// Replace with your own: damage formulas, coordinate maths, parsers,
		// save-tag round trips, anything with no dependency on the loader.
		Assert.Equal(16, Testaria.TileCoordinates.TileSize);
	}

	[Theory]
	[InlineData(0, 0f)]
	[InlineData(1, 16f)]
	[InlineData(100, 1600f)]
	public void Tile_coordinates_convert_to_world_coordinates(int tile, float world)
		=> Assert.Equal(world, Testaria.TileCoordinates.ToWorld(tile));
}
