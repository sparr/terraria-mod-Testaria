using System.Globalization;

namespace Testaria;

/// <summary>
/// A rectangle in tile coordinates.
/// <para/>
/// Tile coordinates, never world coordinates. Terraria uses both (world
/// coordinates are tile coordinates times 16) and mixing them up is the single
/// most common source of bugs in Terraria adjacent code, so this type is tile
/// only and conversion is the caller's problem.
/// <para/>
/// Y increases downward, as everywhere else in Terraria, so
/// <see cref="Top"/> is numerically smaller than <see cref="Bottom"/>.
/// </summary>
public readonly record struct TileRect(int X, int Y, int Width, int Height)
{
	/// <summary>Leftmost tile column, inclusive.</summary>
	public int Left => X;

	/// <summary>Topmost tile row, inclusive.</summary>
	public int Top => Y;

	/// <summary>One past the rightmost tile column, exclusive.</summary>
	public int Right => X + Width;

	/// <summary>One past the bottommost tile row, exclusive.</summary>
	public int Bottom => Y + Height;

	/// <summary>Number of tiles covered.</summary>
	public int Area => Width * Height;

	/// <summary>True when the rectangle covers no tiles.</summary>
	public bool IsEmpty => Width <= 0 || Height <= 0;

	/// <summary>True when the given tile lies inside the rectangle.</summary>
	public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

	/// <summary>True when <paramref name="other"/> lies wholly inside this rectangle.</summary>
	public bool Contains(TileRect other)
		=> other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom;

	/// <summary>
	/// True when the two rectangles share at least one tile. Edge-adjacent
	/// rectangles do not intersect, since the right and bottom edges are
	/// exclusive.
	/// </summary>
	public bool Intersects(TileRect other)
		=> Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

	/// <summary>
	/// Grows the rectangle by <paramref name="amount"/> tiles on every side.
	/// This is how a gutter is applied around a box.
	/// </summary>
	public TileRect Inflate(int amount)
		=> new(X - amount, Y - amount, Width + (amount * 2), Height + (amount * 2));

	/// <summary>
	/// Shrinks the rectangle by <paramref name="amount"/> tiles on every side.
	/// This is how a box's usable interior is derived from its outer bounds.
	/// Clamped at empty rather than inverting.
	/// </summary>
	public TileRect Deflate(int amount)
	{
		int width = Math.Max(0, Width - (amount * 2));
		int height = Math.Max(0, Height - (amount * 2));

		return new TileRect(X + amount, Y + amount, width, height);
	}

	/// <summary>The smallest rectangle covering both inputs.</summary>
	public static TileRect Union(TileRect a, TileRect b)
	{
		if (a.IsEmpty)
			return b;

		if (b.IsEmpty)
			return a;

		int left = Math.Min(a.Left, b.Left);
		int top = Math.Min(a.Top, b.Top);

		return new TileRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
	}

	/// <summary>
	/// Renders as "x,y,width,height", the format
	/// <see cref="TestResult.Box"/> carries into a report.
	/// </summary>
	public override string ToString()
		=> string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Width},{Height}");
}
