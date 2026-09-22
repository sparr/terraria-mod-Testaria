namespace Testaria;

/// <summary>
/// Positions within a box, in world coordinates.
/// <para/>
/// Pure geometry, kept out of the game-facing layer so that the arithmetic
/// most likely to be wrong is the arithmetic most easily tested.
/// </summary>
public static class BoxSpace
{
	/// <summary>The world position at the centre of a box.</summary>
	public static WorldPoint Center(TileRect interior)
	{
		if (interior.IsEmpty)
			throw new ArgumentException("An empty box has no centre.", nameof(interior));

		return new WorldPoint(
			TileCoordinates.ToWorld(interior.Left) + (interior.Width * TileCoordinates.TileSize / 2f),
			TileCoordinates.ToWorld(interior.Top) + (interior.Height * TileCoordinates.TileSize / 2f));
	}

	/// <summary>
	/// The world position of a tile given relative to the box's top-left
	/// corner, so a test can say "eight tiles in, three down" without knowing
	/// where in the world it was actually placed.
	/// </summary>
	public static WorldPoint At(TileRect interior, int offsetX, int offsetY)
	{
		if (offsetX < 0 || offsetY < 0 || offsetX >= interior.Width || offsetY >= interior.Height) {
			throw new ArgumentOutOfRangeException(
				nameof(offsetX),
				$"Offset ({offsetX}, {offsetY}) lies outside a {interior.Width}x{interior.Height} box.");
		}

		return TileCoordinates.CenterOfTile(interior.Left + offsetX, interior.Top + offsetY);
	}

	/// <summary>Whether a world position lies inside the box.</summary>
	public static bool Contains(TileRect interior, WorldPoint position)
		=> interior.Contains(TileCoordinates.ToTile(position.X), TileCoordinates.ToTile(position.Y));

	/// <summary>
	/// Pulls a world position back inside the box.
	/// <para/>
	/// This is what the warden uses on an entity that has drifted out: the
	/// escape is reported as a failure, and the entity is brought home so it
	/// cannot go on disturbing a neighbour while the report is written.
	/// </summary>
	public static WorldPoint Clamp(TileRect interior, WorldPoint position)
	{
		if (interior.IsEmpty)
			throw new ArgumentException("Cannot clamp into an empty box.", nameof(interior));

		float minX = TileCoordinates.ToWorld(interior.Left);
		float minY = TileCoordinates.ToWorld(interior.Top);

		// One pixel short of the far edge, so that a clamped position converts
		// back to the last tile inside the box rather than the first outside.
		float maxX = TileCoordinates.ToWorld(interior.Right) - 1;
		float maxY = TileCoordinates.ToWorld(interior.Bottom) - 1;

		return new WorldPoint(Math.Clamp(position.X, minX, maxX), Math.Clamp(position.Y, minY, maxY));
	}
}
