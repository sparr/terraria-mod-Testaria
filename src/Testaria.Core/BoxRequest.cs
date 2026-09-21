namespace Testaria;

/// <summary>Whether a box sits inside one band or crosses several.</summary>
public enum BoxKind
{
	/// <summary>Fits within a single <see cref="Band"/>. The common case.</summary>
	Banded,

	/// <summary>
	/// Crosses band boundaries. Used by tests whose subject is the boundary
	/// itself, such as falling from the surface into the cavern.
	/// </summary>
	Spanning,
}

/// <summary>
/// What a test asks of the arena.
/// <para/>
/// A spanning request declares no height: the height of a column is dictated
/// by the world's band boundaries, not by the author, so only width is theirs
/// to choose.
/// </summary>
public readonly record struct BoxRequest
{
	private BoxRequest(Band bands, int width, int height)
	{
		Bands = bands;
		Width = width;
		Height = height;
	}

	/// <summary>The band or bands the box must occupy.</summary>
	public Band Bands { get; }

	/// <summary>Requested interior width in tiles, before size class rounding.</summary>
	public int Width { get; }

	/// <summary>
	/// Requested interior height in tiles, before size class rounding. Zero for
	/// a spanning request, whose height the world decides.
	/// </summary>
	public int Height { get; }

	/// <summary>Whether this resolves to a banded box or a spanning column.</summary>
	public BoxKind Kind => WorldGeometry.Fill(Bands) == Bands && IsSingleBand(Bands)
		? BoxKind.Banded
		: BoxKind.Spanning;

	/// <summary>A box inside a single band.</summary>
	public static BoxRequest Banded(Band band, int width, int height)
	{
		if (!IsSingleBand(band))
			throw new ArgumentException($"A banded request needs exactly one band, got {band}. Use Spanning for a column.", nameof(band));

		Require(width, nameof(width));
		Require(height, nameof(height));

		return new BoxRequest(band, width, height);
	}

	/// <summary>
	/// A column crossing several bands. The band set is completed to a
	/// contiguous run by <see cref="WorldGeometry.Fill(Band)"/>, since a column
	/// cannot physically skip a band.
	/// </summary>
	public static BoxRequest Spanning(Band bands, int width)
	{
		Band filled = WorldGeometry.Fill(bands);
		if (filled == Band.None)
			throw new ArgumentException("A spanning request needs at least one band.", nameof(bands));

		Require(width, nameof(width));

		return new BoxRequest(filled, width, 0);
	}

	private static bool IsSingleBand(Band band)
		=> band != Band.None && (band & (band - 1)) == 0;

	private static void Require(int value, string name)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(name, value, "Box dimensions must be positive.");
	}
}
