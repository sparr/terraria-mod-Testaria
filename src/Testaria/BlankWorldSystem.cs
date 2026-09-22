using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace Testaria;

/// <summary>
/// Replaces world generation with a blank, deterministic substrate when asked.
/// <para/>
/// Most of what can wander into a test's box is not an entity: liquid flows,
/// sand falls, grass and corruption spread. No warden can see those, and each
/// one is a race. Generating a world that contains none of them removes the
/// whole class at the root, which is a stronger guarantee than watching for
/// them.
/// <para/>
/// Off unless the <c>-testariablank</c> launch parameter is present, so an
/// ordinary game is never affected by having this mod installed.
/// </summary>
public sealed class BlankWorldSystem : ModSystem
{
	/// <summary>Launch parameter that turns blank generation on.</summary>
	public const string LaunchFlag = "-testariablank";

	/// <summary>Whether this run was asked for a blank world.</summary>
	public static bool Enabled => Program.LaunchParameters.ContainsKey(LaunchFlag);

	/// <summary>
	/// The layout the world was built to, or null if it was generated
	/// normally. The arena reads its reserved ground from here.
	/// </summary>
	public static BlankWorldLayout? Layout { get; internal set; }

	/// <inheritdoc />
	public override void ModifyWorldGenTasks(List<GenPass> tasks)
	{
		if (!Enabled)
			return;

		// Every vanilla pass goes, including Terrain. That pass is normally
		// what assigns Main.worldSurface and Main.rockLayer, so the
		// replacement has to set them itself or the arena has no idea where
		// its bands are.
		tasks.Clear();
		tasks.Add(new BlankWorldPass());
	}

	/// <inheritdoc />
	public override void Unload() => Layout = null;
}

/// <summary>Writes the substrate described by a <see cref="BlankWorldLayout"/>.</summary>
internal sealed class BlankWorldPass : GenPass
{
	public BlankWorldPass() : base("Testaria Blank World", 100.0) { }

	protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
	{
		progress.Message = "Testaria: blank world";

		BlankWorldLayout layout = BlankWorldLayout.For(Main.maxTilesX, Main.maxTilesY);

		// Layer boundaries are set rather than derived, which is the whole
		// point: in an ordinary world these fall out of terrain generation and
		// the space fraction has to be guessed.
		Main.worldSurface = layout.Geometry.UndergroundTop;
		Main.rockLayer = layout.Geometry.CavernTop;

		for (int x = 0; x < Main.maxTilesX; x++) {
			for (int y = 0; y < Main.maxTilesY; y++) {
				Tile tile = Main.tile[x, y];

				tile.ClearEverything();

				// Stone below the surface, air above. Pure air would leave
				// entities no floor and make any behaviour test meaningless.
				if (y >= layout.SurfaceLevel)
					tile.ResetToType(TileID.Stone);
			}

			if ((x & 63) == 0)
				progress.Set(x / (double)Main.maxTilesX);
		}

		Main.spawnTileX = layout.SpawnTileX;
		Main.spawnTileY = layout.SpawnTileY;

		BlankWorldSystem.Layout = layout;

		progress.Set(1.0);
	}
}
