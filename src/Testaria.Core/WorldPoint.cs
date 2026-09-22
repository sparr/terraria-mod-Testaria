using System.Globalization;

namespace Testaria;

/// <summary>
/// A position in world coordinates, which is to say pixels.
/// <para/>
/// Terraria keeps two coordinate systems in constant use: tile coordinates
/// index the grid, and world coordinates are pixels, equal to tile coordinates
/// times sixteen. Confusing them is the single most common source of bugs in
/// Terraria adjacent code, so the two have distinct types here
/// (<see cref="TileRect"/> for tiles, this for world) and conversion has to be
/// written out rather than happening by accident.
/// </summary>
public readonly record struct WorldPoint(float X, float Y)
{
	/// <summary>Renders as "x,y" for diagnostics.</summary>
	public override string ToString()
		=> string.Create(CultureInfo.InvariantCulture, $"{X:0.##},{Y:0.##}");
}
