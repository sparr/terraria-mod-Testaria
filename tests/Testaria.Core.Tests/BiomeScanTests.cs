namespace Testaria.Tests;

/// <summary>
/// The biome scan's dimensions, pinned so that a change to the arithmetic has
/// to be deliberate.
/// <para/>
/// These are transcribed from the decompiled game rather than derived, so the
/// test's job is not to check the multiplication. It is to state the numbers
/// in one place where a future version of Terraria that changes them will be
/// noticed, and to document the gap between them and the gutter.
/// </summary>
public class BiomeScanTests
{
	[Fact]
	public void The_scanned_rectangle_is_the_size_the_game_computes()
	{
		// 1920/16 + 50 - 1 and 1200/16 + 50 - 1.
		XAssert.Equal(169, BiomeScan.Width);
		XAssert.Equal(124, BiomeScan.Height);
	}

	[Fact]
	public void The_scan_reaches_much_further_than_a_gutter_does()
	{
		XAssert.Equal(84, BiomeScan.HorizontalReach);
		XAssert.Equal(62, BiomeScan.VerticalReach);

		// The point of the whole type: the default gutter covers tile framing
		// and liquid, and is an order of magnitude short of biome isolation.
		// Two boxes a gutter apart are not biome-isolated from each other.
		XAssert.True(BiomeScan.HorizontalReach > new ArenaOptions().Gutter * 10);
	}

	[Fact]
	public void A_biome_needs_three_hundred_tiles_before_it_counts()
		// Which is why the reach above is survivable for most tests: a stray
		// block does nothing, and a test has to mean it.
		=> XAssert.Equal(300, BiomeScan.BiomeTileThreshold);
}
