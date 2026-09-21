namespace Testaria.Tests;

public class TileRectTests
{
	[Fact]
	public void Right_and_bottom_are_exclusive()
	{
		var rect = new TileRect(10, 20, 5, 7);

		XAssert.Equal(10, rect.Left);
		XAssert.Equal(20, rect.Top);
		XAssert.Equal(15, rect.Right);
		XAssert.Equal(27, rect.Bottom);
		XAssert.Equal(35, rect.Area);
	}

	[Fact]
	public void Contains_includes_the_top_left_and_excludes_the_bottom_right()
	{
		var rect = new TileRect(10, 20, 5, 5);

		XAssert.True(rect.Contains(10, 20));
		XAssert.True(rect.Contains(14, 24));
		XAssert.False(rect.Contains(15, 24));
		XAssert.False(rect.Contains(14, 25));
		XAssert.False(rect.Contains(9, 20));
	}

	[Fact]
	public void Contains_rect_requires_full_containment()
	{
		var outer = new TileRect(0, 0, 10, 10);

		XAssert.True(outer.Contains(new TileRect(2, 2, 3, 3)));
		XAssert.True(outer.Contains(outer));
		XAssert.False(outer.Contains(new TileRect(8, 8, 5, 5)));
	}

	[Fact]
	public void Edge_adjacent_rectangles_do_not_intersect()
	{
		// This is what lets boxes be packed with an exact gutter: a box ending
		// at x=100 and one starting at x=100 are touching, not overlapping.
		var left = new TileRect(0, 0, 100, 10);
		var right = new TileRect(100, 0, 100, 10);

		XAssert.False(left.Intersects(right));
		XAssert.False(right.Intersects(left));
	}

	[Fact]
	public void Overlapping_rectangles_intersect_both_ways()
	{
		var a = new TileRect(0, 0, 10, 10);
		var b = new TileRect(9, 9, 10, 10);

		XAssert.True(a.Intersects(b));
		XAssert.True(b.Intersects(a));
	}

	[Fact]
	public void Inflate_grows_on_every_side()
	{
		TileRect inflated = new TileRect(10, 10, 5, 5).Inflate(2);

		XAssert.Equal(new TileRect(8, 8, 9, 9), inflated);
	}

	[Fact]
	public void Deflate_is_the_inverse_of_inflate()
	{
		var original = new TileRect(10, 10, 20, 20);

		XAssert.Equal(original, original.Inflate(3).Deflate(3));
	}

	[Fact]
	public void Deflate_clamps_at_empty_rather_than_inverting()
	{
		// A gutter wider than the box must not produce a negative rectangle
		// that silently passes containment checks.
		TileRect crushed = new TileRect(10, 10, 4, 4).Deflate(5);

		XAssert.True(crushed.IsEmpty);
		XAssert.Equal(0, crushed.Width);
		XAssert.Equal(0, crushed.Height);
	}

	[Fact]
	public void IsEmpty_is_true_for_zero_and_negative_extents()
	{
		XAssert.True(new TileRect(0, 0, 0, 5).IsEmpty);
		XAssert.True(new TileRect(0, 0, 5, 0).IsEmpty);
		XAssert.True(default(TileRect).IsEmpty);
		XAssert.False(new TileRect(0, 0, 1, 1).IsEmpty);
	}

	[Fact]
	public void Union_covers_both_inputs()
	{
		TileRect union = TileRect.Union(new TileRect(0, 0, 5, 5), new TileRect(10, 10, 5, 5));

		XAssert.Equal(new TileRect(0, 0, 15, 15), union);
	}

	[Fact]
	public void Union_with_an_empty_rectangle_returns_the_other()
	{
		var rect = new TileRect(3, 4, 5, 6);

		XAssert.Equal(rect, TileRect.Union(rect, default));
		XAssert.Equal(rect, TileRect.Union(default, rect));
	}

	[Fact]
	public void ToString_matches_the_format_carried_into_a_report()
	{
		// TestResult.Box is documented as "x,y,width,height"; a mismatch here
		// would silently produce unparseable coordinates in failure reports.
		XAssert.Equal("100,200,80,48", new TileRect(100, 200, 80, 48).ToString());
	}
}
