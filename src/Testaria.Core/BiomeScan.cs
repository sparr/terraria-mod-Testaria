namespace Testaria;

/// <summary>
/// How far Terraria looks around a player when it decides which biome they are
/// in, measured rather than guessed.
/// <para/>
/// This is the largest cross-boundary effect in the game and the one that sets
/// how far apart two boxes must be before a biome-sensitive test can be
/// trusted. PLAN.md section 2.4 listed it as unverifiable from a tModLoader
/// checkout, because <c>SceneMetrics.ScanAndExportToMain</c> is unpatched
/// vanilla and lives in the generated <c>src/</c> tree. It is readable in a
/// decompile, and these numbers come from
/// <c>Terraria/SceneMetrics.cs</c> in the 1.4.5.8 decompilation:
/// <code>
/// private static readonly Point AssumedConstantScreenSize = new Point(1920, 1200);
/// private static readonly int ZoneScanPadding = 25;
/// public static readonly Point ZoneScanSize = new Point(
///     AssumedConstantScreenSize.X / 16 + ZoneScanPadding * 2 - 1,
///     AssumedConstantScreenSize.Y / 16 + ZoneScanPadding * 2 - 1);
/// </code>
/// and <c>ScanTiles</c> scans <c>Utils.CenteredRectangle(TileCenter, ZoneScanSize)</c>,
/// so the rectangle is centred on the player's own tile.
/// <para/>
/// The headline: a biome scan reaches about <b>84 tiles sideways and 62 tiles
/// up and down</b>, which is an order of magnitude past the 8 tile gutter that
/// covers tile framing and liquid. A box is not biome-isolated from its
/// neighbours by geometry alone.
/// </summary>
public static class BiomeScan
{
	/// <summary>The screen size the scan assumes, in pixels, whatever the real one is.</summary>
	public static readonly (int X, int Y) AssumedScreenSize = (1920, 1200);

	/// <summary>Tiles of margin added beyond that screen, on each side.</summary>
	public const int Padding = 25;

	/// <summary>Width of the scanned rectangle, in tiles.</summary>
	public static int Width => (AssumedScreenSize.X / TileCoordinates.TileSize) + (Padding * 2) - 1;

	/// <summary>Height of the scanned rectangle, in tiles.</summary>
	public static int Height => (AssumedScreenSize.Y / TileCoordinates.TileSize) + (Padding * 2) - 1;

	/// <summary>
	/// How far the scan reaches sideways from the player, in tiles. The
	/// spacing two boxes need before one cannot colour the other's biome.
	/// </summary>
	public static int HorizontalReach => Width / 2;

	/// <summary>How far the scan reaches up and down from the player, in tiles.</summary>
	public static int VerticalReach => Height / 2;

	/// <summary>
	/// Tiles of one converted kind needed inside that rectangle before it
	/// counts as a biome, from <c>SceneMetrics.CorruptionTileThreshold</c> and
	/// its neighbours.
	/// <para/>
	/// The reason the reach above is not as alarming as it sounds for most
	/// tests: a stray block does nothing, and a test has to place three
	/// hundred of them to change what a distant player is standing in. What
	/// has no threshold at all is the singular scenery, a campfire, a heart
	/// lantern, a water candle, a music box, which counts from one.
	/// </summary>
	public const int BiomeTileThreshold = 300;
}
