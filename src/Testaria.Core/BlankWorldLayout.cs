namespace Testaria;

/// <summary>
/// The shape of a blank test world: where its layer boundaries sit, where the
/// ground starts, and what ground is reserved for vanilla's sake.
/// <para/>
/// Computed here rather than in the generator so that it can be checked
/// without generating anything. The generator's job is then only to write
/// tiles where this says to.
/// <para/>
/// "Blank" means a known uniform substrate, not literal emptiness: stone below
/// the surface and air above. A world of pure air gives entities no floor,
/// which makes any test of behaviour meaningless.
/// </summary>
public sealed record BlankWorldLayout
{
	/// <summary>Width of the reserved column held for vanilla's furniture.</summary>
	public const int ReservedWidth = 200;

	/// <summary>Name of the reserved area holding the spawn point.</summary>
	public const string SpawnAreaName = "Spawn";

	private BlankWorldLayout() { }

	/// <summary>Band boundaries for this world.</summary>
	public required WorldGeometry Geometry { get; init; }

	/// <summary>Ground the arena must never lease.</summary>
	public required IReadOnlyList<ReservedArea> Reserved { get; init; }

	/// <summary>First solid row. Everything above is air, everything below is stone.</summary>
	public required int SurfaceLevel { get; init; }

	/// <summary>Spawn column.</summary>
	public required int SpawnTileX { get; init; }

	/// <summary>Spawn row, one above the ground so a player stands rather than suffocates.</summary>
	public required int SpawnTileY { get; init; }

	/// <summary>
	/// Derives the layout for a world of the given size.
	/// <para/>
	/// The surface, underground and cavern depths are chosen rather than
	/// measured, which is the point: an ordinary world's come out of terrain
	/// generation and have to be read back, with no guarantee of where they
	/// land. Here they are set, so <see cref="WorldGeometry"/> is exact.
	/// <para/>
	/// The boundary between space and surface is the exception, and is derived
	/// from the others rather than chosen. The game decides what counts as sky
	/// with <see cref="WorldGeometry.SpaceFraction"/> of <c>Main.worldSurface</c>
	/// and will go on doing so in a blank world, so that boundary is not ours
	/// to pick.
	/// </summary>
	public static BlankWorldLayout For(int maxTilesX, int maxTilesY)
	{
		if (maxTilesX <= ReservedWidth * 2)
			throw new ArgumentOutOfRangeException(nameof(maxTilesX), maxTilesX, $"A blank world needs room for the reserved column plus test ground, so more than {ReservedWidth * 2} tiles.");

		if (maxTilesY <= 400)
			throw new ArgumentOutOfRangeException(nameof(maxTilesY), maxTilesY, "A blank world needs more than 400 rows: the underworld alone takes 200.");

		int undergroundTop = maxTilesY / 4;
		int cavernTop = maxTilesY * 3 / 8;

		// Derived, not chosen. The generator writes undergroundTop into
		// Main.worldSurface, and the game goes on deciding what counts as sky
		// with its own y < Main.worldSurface * 0.35 comparison no matter what a
		// layout would prefer. Picking this boundary independently would leave
		// rows the arena leased as Band.Space that the game does not treat as
		// sky, so it is computed the way the game computes it.
		int surfaceTop = (int)(undergroundTop * WorldGeometry.SpaceFraction);

		// Matches vanilla's Main.UnderworldLayer, so depth-sensitive behaviour
		// lines up with a real world even though nothing else does.
		int underworldTop = maxTilesY - 200;

		var geometry = new WorldGeometry(maxTilesX, maxTilesY, surfaceTop, undergroundTop, cavernTop, underworldTop);

		// A single full-height column at one edge, rather than scattered
		// blocks. Contiguous reserved ground keeps the test region a simple
		// span, and the arena steps over it either way.
		var spawnArea = new ReservedArea {
			Name = SpawnAreaName,
			Bounds = new TileRect(0, 0, ReservedWidth, maxTilesY),
			Reason = "Holds the spawn point and anywhere else vanilla assumes exists. Never leased unless a test asks for it by name.",
		};

		return new BlankWorldLayout {
			Geometry = geometry,
			Reserved = [spawnArea],
			SurfaceLevel = undergroundTop,
			SpawnTileX = ReservedWidth / 2,
			SpawnTileY = undergroundTop - 1,
		};
	}
}
