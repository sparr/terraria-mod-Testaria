using Terraria;

namespace Testaria;

/// <summary>
/// Reads the live world's band boundaries out of <see cref="Main"/>.
/// <para/>
/// This adapter is the only place that knows how Terraria spells its layer
/// boundaries. <see cref="WorldGeometry"/> takes them as plain numbers
/// precisely so that the core never has to reference the game.
/// </summary>
public static class TerrariaWorldGeometry
{
	/// <summary>
	/// Fraction of <see cref="Main.worldSurface"/> above which the world counts
	/// as space.
	/// <para/>
	/// Kept here as an alias so that game-facing code has it to hand, but owned
	/// by <see cref="WorldGeometry.SpaceFraction"/>: a blank world has to
	/// compute its own space boundary from the same number, and that layout is
	/// built in the core where <see cref="Main"/> cannot be seen.
	/// </summary>
	public const double SpaceFraction = WorldGeometry.SpaceFraction;

	/// <summary>
	/// Snapshots the current world's geometry.
	/// <para/>
	/// Call this after a world is loaded. The values change between worlds, and
	/// a spanning test's box height depends on them, which is why the arena
	/// must be rebuilt per world rather than cached across them.
	/// </summary>
	public static WorldGeometry Current() => WorldGeometry.FromTerrariaValues(
		Main.maxTilesX,
		Main.maxTilesY,
		Main.worldSurface,
		Main.rockLayer,
		Main.UnderworldLayer,
		SpaceFraction);
}
