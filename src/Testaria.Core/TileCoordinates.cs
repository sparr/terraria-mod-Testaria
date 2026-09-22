namespace Testaria;

/// <summary>Converts between tile and world coordinates.</summary>
public static class TileCoordinates
{
	/// <summary>Width and height of a tile in pixels.</summary>
	public const int TileSize = 16;

	/// <summary>World coordinate of a tile's top-left corner.</summary>
	public static float ToWorld(int tile) => tile * (float)TileSize;

	/// <summary>
	/// Tile containing a world coordinate.
	/// <para/>
	/// Uses floor rather than a cast to int. Terraria itself generally writes
	/// <c>(int)(x / 16f)</c>, which truncates toward zero and so maps both
	/// -8 and +8 to tile 0. That never bites in a valid world, where
	/// coordinates are non-negative, but a test framework checking whether
	/// something left its box needs the answer to be right on both sides of
	/// the origin rather than only one.
	/// </summary>
	public static int ToTile(float world) => (int)MathF.Floor(world / TileSize);

	/// <summary>World coordinate of a tile's centre.</summary>
	public static float CenterOfTile(int tile) => ToWorld(tile) + (TileSize / 2f);

	/// <summary>World position of the top-left corner of a tile.</summary>
	public static WorldPoint ToWorld(int tileX, int tileY) => new(ToWorld(tileX), ToWorld(tileY));

	/// <summary>World position of the centre of a tile.</summary>
	public static WorldPoint CenterOfTile(int tileX, int tileY) => new(CenterOfTile(tileX), CenterOfTile(tileY));
}
