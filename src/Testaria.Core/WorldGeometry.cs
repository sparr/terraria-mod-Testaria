namespace Testaria;

/// <summary>
/// Where each <see cref="Band"/> begins and ends in a particular world.
/// <para/>
/// Boundaries are supplied rather than computed, because the real values live
/// in <c>Main.worldSurface</c>, <c>Main.rockLayer</c>, and
/// <c>Main.UnderworldLayer</c> and are derived from the world's height. The
/// core takes them as input so that it never has to guess at a game constant
/// it cannot see, which is the same discipline PLAN.md section 2.4 applies to
/// every other unmeasured number.
/// </summary>
public sealed record WorldGeometry
{
	private static readonly Band[] TopToBottom =
		[Band.Space, Band.Surface, Band.Underground, Band.Cavern, Band.Underworld];

	/// <summary>Creates a geometry, validating that the boundaries descend in order.</summary>
	/// <param name="maxTilesX">World width in tiles, <c>Main.maxTilesX</c>.</param>
	/// <param name="maxTilesY">World height in tiles, <c>Main.maxTilesY</c>.</param>
	/// <param name="surfaceTop">First row of <see cref="Band.Surface"/>.</param>
	/// <param name="undergroundTop">First row of <see cref="Band.Underground"/>, <c>Main.worldSurface</c>.</param>
	/// <param name="cavernTop">First row of <see cref="Band.Cavern"/>, <c>Main.rockLayer</c>.</param>
	/// <param name="underworldTop">First row of <see cref="Band.Underworld"/>, <c>Main.UnderworldLayer</c>.</param>
	public WorldGeometry(int maxTilesX, int maxTilesY, int surfaceTop, int undergroundTop, int cavernTop, int underworldTop)
	{
		if (maxTilesX <= 0 || maxTilesY <= 0)
			throw new ArgumentException($"World dimensions must be positive, got {maxTilesX}x{maxTilesY}.");

		int[] boundaries = [0, surfaceTop, undergroundTop, cavernTop, underworldTop, maxTilesY];
		for (int i = 1; i < boundaries.Length; i++) {
			if (boundaries[i] < boundaries[i - 1]) {
				throw new ArgumentException(
					"Band boundaries must descend in order " +
					$"(0 <= surfaceTop <= undergroundTop <= cavernTop <= underworldTop <= maxTilesY), got " +
					$"[{string.Join(", ", boundaries)}].");
			}
		}

		MaxTilesX = maxTilesX;
		MaxTilesY = maxTilesY;
		SurfaceTop = surfaceTop;
		UndergroundTop = undergroundTop;
		CavernTop = cavernTop;
		UnderworldTop = underworldTop;
	}

	/// <summary>World width in tiles.</summary>
	public int MaxTilesX { get; }

	/// <summary>World height in tiles.</summary>
	public int MaxTilesY { get; }

	/// <summary>First row of <see cref="Band.Surface"/>.</summary>
	public int SurfaceTop { get; }

	/// <summary>First row of <see cref="Band.Underground"/>.</summary>
	public int UndergroundTop { get; }

	/// <summary>First row of <see cref="Band.Cavern"/>.</summary>
	public int CavernTop { get; }

	/// <summary>First row of <see cref="Band.Underworld"/>.</summary>
	public int UnderworldTop { get; }

	/// <summary>The rectangle covering a single band, full world width.</summary>
	public TileRect BandBounds(Band band)
	{
		(int top, int bottom) = band switch {
			Band.Space => (0, SurfaceTop),
			Band.Surface => (SurfaceTop, UndergroundTop),
			Band.Underground => (UndergroundTop, CavernTop),
			Band.Cavern => (CavernTop, UnderworldTop),
			Band.Underworld => (UnderworldTop, MaxTilesY),
			_ => throw new ArgumentException($"Expected exactly one band, got {band}.", nameof(band)),
		};

		return new TileRect(0, top, MaxTilesX, bottom - top);
	}

	/// <summary>
	/// Completes a band set to a contiguous run from its topmost to its
	/// bottommost member.
	/// <para/>
	/// A column from the surface to the cavern physically passes through the
	/// underground whether or not the author listed it, so filling the gap is
	/// the honest answer rather than either rejecting the request or silently
	/// leaving a hole in the span.
	/// </summary>
	public static Band Fill(Band bands)
	{
		if (bands == Band.None)
			return Band.None;

		int first = Array.FindIndex(TopToBottom, b => bands.HasFlag(b));
		int last = Array.FindLastIndex(TopToBottom, b => bands.HasFlag(b));

		if (first < 0)
			throw new ArgumentException($"No known bands in {bands}.", nameof(bands));

		Band filled = Band.None;
		for (int i = first; i <= last; i++)
			filled |= TopToBottom[i];

		return filled;
	}

	/// <summary>
	/// The rectangle spanning a band set, after
	/// <see cref="Fill(Band)"/> completes it.
	/// </summary>
	public TileRect Span(Band bands)
	{
		Band filled = Fill(bands);
		if (filled == Band.None)
			return default;

		TileRect span = default;
		foreach (Band band in TopToBottom) {
			if (filled.HasFlag(band))
				span = TileRect.Union(span, BandBounds(band));
		}

		return span;
	}

	/// <summary>The band containing a given tile row.</summary>
	public Band BandAt(int y)
	{
		foreach (Band band in TopToBottom) {
			TileRect bounds = BandBounds(band);
			if (y >= bounds.Top && y < bounds.Bottom)
				return band;
		}

		return Band.None;
	}
}
