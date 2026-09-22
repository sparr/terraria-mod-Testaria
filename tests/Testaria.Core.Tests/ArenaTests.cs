namespace Testaria.Tests;

public class ArenaTests
{
	private static WorldGeometry Small() => new(4200, 1200, 87, 250, 400, 1000);

	private static Arena NewArena(ArenaOptions? options = null) => new(Small(), options);

	private static void AssertNoOverlaps(Arena arena)
	{
		List<BoxLease> leases = arena.Active.ToList();

		for (int i = 0; i < leases.Count; i++) {
			for (int j = i + 1; j < leases.Count; j++) {
				XAssert.False(
					leases[i].Bounds.Intersects(leases[j].Bounds),
					$"leases {leases[i].Id} at {leases[i].Bounds} and {leases[j].Id} at {leases[j].Bounds} overlap");
			}
		}
	}

	[Fact]
	public void A_lease_interior_is_its_bounds_less_the_gutter_on_every_side()
	{
		Arena arena = NewArena(new ArenaOptions { Gutter = 8 });
		BoxLease lease = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		XAssert.Equal(lease.Bounds.Deflate(8), lease.Interior);
		XAssert.True(lease.Bounds.Contains(lease.Interior));
	}

	[Fact]
	public void Requests_are_rounded_up_to_a_size_class()
	{
		Arena arena = NewArena();
		BoxLease lease = arena.TryLease(BoxRequest.Banded(Band.Cavern, 50, 33))!;

		// 50 rounds to the 96 class, 33 to the 64 class.
		XAssert.Equal(96, lease.Interior.Width);
		XAssert.Equal(64, lease.Interior.Height);
	}

	[Fact]
	public void A_request_larger_than_every_size_class_throws_with_a_usable_message()
	{
		Arena arena = NewArena();

		var ex = XAssert.Throws<ArgumentOutOfRangeException>(
			() => arena.TryLease(BoxRequest.Banded(Band.Cavern, 5000, 32)));

		XAssert.Contains("fresh world", ex.Message);
	}

	[Fact]
	public void A_box_taller_than_its_band_throws_rather_than_silently_overflowing()
	{
		// The Surface band here is 163 rows tall. A 256 tall box plus gutters
		// does not fit, and quietly spilling into the Underground would put
		// the test in a different spawn pool than it asked for.
		Arena arena = NewArena();

		var ex = XAssert.Throws<ArgumentOutOfRangeException>(
			() => arena.TryLease(BoxRequest.Banded(Band.Surface, 48, 256)));

		XAssert.Contains("Surface", ex.Message);
	}

	[Fact]
	public void Concurrent_leases_never_overlap()
	{
		Arena arena = NewArena();

		for (int i = 0; i < 20; i++)
			XAssert.NotNull(arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32)));

		XAssert.Equal(20, arena.Active.Count);
		AssertNoOverlaps(arena);
	}

	[Fact]
	public void Boxes_in_different_bands_sit_in_different_rows_and_do_not_overlap()
	{
		Arena arena = NewArena();

		BoxLease surface = arena.TryLease(BoxRequest.Banded(Band.Surface, 48, 32))!;
		BoxLease cavern = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		XAssert.NotEqual(surface.Bounds.Top, cavern.Bounds.Top);
		XAssert.False(surface.Bounds.Intersects(cavern.Bounds));
	}

	[Fact]
	public void A_released_box_is_not_immediately_reusable()
	{
		// Queued liquid, lighting, and despawn timers outlive teardown, so
		// handing the slot straight back would give the next tenant someone
		// else's leftovers.
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 60 });

		BoxLease first = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
		arena.Release(first);

		BoxLease second = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		XAssert.NotEqual(first.Bounds, second.Bounds);
		XAssert.Equal(1, arena.Stats.Quarantined);
	}

	[Fact]
	public void A_box_becomes_reusable_once_quarantine_elapses()
	{
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 60 });

		BoxLease first = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
		TileRect bounds = first.Bounds;
		arena.Release(first);

		for (int i = 0; i < 60; i++)
			arena.Tick();

		BoxLease second = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		XAssert.Equal(bounds, second.Bounds);
		XAssert.Equal(1, arena.Stats.SlotsCarved);
	}

	[Fact]
	public void Quarantine_is_not_over_one_tick_early()
	{
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 60 });

		arena.Release(arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!);

		for (int i = 0; i < 59; i++)
			arena.Tick();

		XAssert.Equal(1, arena.Stats.Quarantined);
		XAssert.Equal(0, arena.Stats.Free);

		arena.Tick();

		XAssert.Equal(0, arena.Stats.Quarantined);
		XAssert.Equal(1, arena.Stats.Free);
	}

	[Fact]
	public void Zero_quarantine_makes_a_box_immediately_reusable()
	{
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 0 });

		BoxLease first = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
		arena.Release(first);

		XAssert.Equal(first.Bounds, arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!.Bounds);
	}

	[Fact]
	public void Arena_size_tracks_peak_concurrency_not_test_count()
	{
		// The property the whole lease-and-recycle design exists for: a suite
		// of any length runs in an arena sized by how many boxes are live at
		// once. Sequentially, that is one.
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 5 });

		for (int test = 0; test < 500; test++) {
			BoxLease lease = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
			XAssert.NotNull(lease);
			arena.Release(lease);

			for (int t = 0; t < 5; t++)
				arena.Tick();
		}

		XAssert.Equal(1, arena.Stats.SlotsCarved);
	}

	[Fact]
	public void A_concurrent_peak_is_carved_once_and_then_reused_forever()
	{
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 1 });

		for (int round = 0; round < 50; round++) {
			List<BoxLease> held = [];
			for (int i = 0; i < 8; i++)
				held.Add(arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!);

			AssertNoOverlaps(arena);

			foreach (BoxLease lease in held)
				arena.Release(lease);

			arena.Tick();
		}

		// Peak concurrency was 8, so 8 slots, no matter that 400 leases ran.
		XAssert.Equal(8, arena.Stats.SlotsCarved);
	}

	[Fact]
	public void A_retained_box_is_never_reused()
	{
		// Preserving a failed test's wreckage is the point, so the slot must
		// not come back even after quarantine would have elapsed.
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 1 });

		BoxLease failed = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
		arena.Release(failed, keepForInspection: true);

		for (int i = 0; i < 100; i++)
			arena.Tick();

		BoxLease next = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		XAssert.NotEqual(failed.Bounds, next.Bounds);
		XAssert.Equal(1, arena.Stats.Retained);
		XAssert.Equal(2, arena.Stats.SlotsCarved);
	}

	[Fact]
	public void Exhaustion_returns_null_rather_than_handing_out_overlapping_ground()
	{
		Arena arena = NewArena(new ArenaOptions { WidthClasses = [384], HeightClasses = [32] });

		int leased = 0;
		while (arena.TryLease(BoxRequest.Banded(Band.Cavern, 384, 32)) is not null)
			leased++;

		XAssert.True(leased > 0);
		AssertNoOverlaps(arena);
		XAssert.Null(arena.TryLease(BoxRequest.Banded(Band.Cavern, 384, 32)));
	}

	[Fact]
	public void Releasing_a_lease_twice_throws()
	{
		Arena arena = NewArena();
		BoxLease lease = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		arena.Release(lease);

		XAssert.Throws<InvalidOperationException>(() => arena.Release(lease));
	}

	[Fact]
	public void A_spanning_box_covers_every_band_in_its_filled_range()
	{
		Arena arena = NewArena();
		WorldGeometry geo = Small();

		BoxLease column = arena.TryLease(BoxRequest.Spanning(Band.Surface | Band.Cavern, 48))!;

		XAssert.Equal(BoxKind.Spanning, column.Kind);
		XAssert.Equal(Band.Surface | Band.Underground | Band.Cavern, column.Bands);
		XAssert.Equal(geo.SurfaceTop, column.Bounds.Top);
		XAssert.Equal(geo.UnderworldTop, column.Bounds.Bottom);
	}

	[Fact]
	public void A_spanning_box_contains_the_boundary_its_test_exists_to_exercise()
	{
		Arena arena = NewArena();
		WorldGeometry geo = Small();

		BoxLease column = arena.TryLease(BoxRequest.Spanning(Band.Surface | Band.Underground, 48))!;

		// The underground boundary must fall inside the usable interior, not
		// be trimmed off by the gutter, or the test cannot observe it.
		XAssert.True(column.Interior.Top < geo.UndergroundTop);
		XAssert.True(column.Interior.Bottom > geo.UndergroundTop);
	}

	[Fact]
	public void Spanning_boxes_live_in_the_column_region_and_banded_boxes_do_not()
	{
		Arena arena = NewArena();

		BoxLease banded = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
		BoxLease column = arena.TryLease(BoxRequest.Spanning(Band.All, 48))!;

		XAssert.True(banded.Bounds.Right <= arena.ColumnRegionStart);
		XAssert.True(column.Bounds.Left >= arena.ColumnRegionStart);
		XAssert.False(banded.Bounds.Intersects(column.Bounds));
	}

	[Fact]
	public void A_column_never_overlaps_a_banded_box_in_any_band_it_crosses()
	{
		// The reason for partitioning the world rather than letting columns
		// punch through the banded rows.
		Arena arena = NewArena();

		for (int i = 0; i < 6; i++) {
			arena.TryLease(BoxRequest.Banded(Band.Surface, 48, 32));
			arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32));
		}

		for (int i = 0; i < 4; i++)
			arena.TryLease(BoxRequest.Spanning(Band.All, 48));

		AssertNoOverlaps(arena);
	}

	[Fact]
	public void Columns_and_banded_boxes_recycle_independently()
	{
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 0 });

		BoxLease column = arena.TryLease(BoxRequest.Spanning(Band.All, 48))!;
		BoxLease banded = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		arena.Release(column);
		arena.Release(banded);

		XAssert.Equal(column.Bounds, arena.TryLease(BoxRequest.Spanning(Band.All, 48))!.Bounds);
		XAssert.Equal(banded.Bounds, arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!.Bounds);
		XAssert.Equal(2, arena.Stats.SlotsCarved);
	}

	[Fact]
	public void Different_size_classes_do_not_share_slots()
	{
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 0 });

		BoxLease small = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
		arena.Release(small);

		BoxLease large = arena.TryLease(BoxRequest.Banded(Band.Cavern, 192, 32))!;

		XAssert.NotEqual(small.Bounds, large.Bounds);
		XAssert.Equal(2, arena.Stats.SlotsCarved);
	}

	[Fact]
	public void Stats_account_for_every_carved_slot()
	{
		Arena arena = NewArena(new ArenaOptions { QuarantineTicks = 10 });

		BoxLease held = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
		arena.Release(arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!);
		arena.Release(arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!, keepForInspection: true);

		ArenaStats stats = arena.Stats;

		XAssert.Equal(3, stats.SlotsCarved);
		XAssert.Equal(stats.SlotsCarved, stats.Active + stats.Quarantined + stats.Retained + stats.Free);
		XAssert.Equal(1, stats.Active);
		XAssert.NotNull(held);
	}

	[Theory]
	[InlineData(-1, 0.25)]
	[InlineData(8, 0.0)]
	[InlineData(8, 1.0)]
	public void Invalid_options_are_rejected(int gutter, double columnFraction)
		=> XAssert.Throws<ArgumentException>(
			() => new Arena(Small(), new ArenaOptions { Gutter = gutter, ColumnRegionFraction = columnFraction }));

	[Fact]
	public void Size_classes_must_ascend_strictly()
		=> XAssert.Throws<ArgumentException>(
			() => new Arena(Small(), new ArenaOptions { WidthClasses = [96, 48] }));

	[Fact]
	public void Empty_size_classes_are_rejected()
		=> XAssert.Throws<ArgumentException>(
			() => new Arena(Small(), new ArenaOptions { HeightClasses = [] }));

	[Fact]
	public void A_banded_request_rejects_a_multi_band_set()
		=> XAssert.Throws<ArgumentException>(() => BoxRequest.Banded(Band.Surface | Band.Cavern, 48, 32));

	[Fact]
	public void Box_dimensions_must_be_positive()
	{
		XAssert.Throws<ArgumentOutOfRangeException>(() => BoxRequest.Banded(Band.Cavern, 0, 32));
		XAssert.Throws<ArgumentOutOfRangeException>(() => BoxRequest.Spanning(Band.All, -1));
	}

	[Fact]
	public void A_lease_renders_as_the_coordinates_a_failure_report_carries()
	{
		Arena arena = NewArena();
		BoxLease lease = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		XAssert.Equal(lease.Interior.ToString(), lease.ToString());
	}

	[Fact]
	public void A_retained_box_is_never_handed_out_again()
	{
		// Somebody kept that wreckage because they mean to go and look at it.
		// Handing the ground to a later test would destroy the evidence the
		// retention existed for.
		var arena = new Arena(Small(), new ArenaOptions { QuarantineTicks = 0 });
		BoxRequest request = BoxRequest.Banded(Band.Cavern, 16, 16);

		BoxLease kept = arena.TryLease(request)!;
		arena.Release(kept, keepForInspection: true);

		for (int i = 0; i < 20; i++) {
			BoxLease? next = arena.TryLease(request);

			XAssert.NotNull(next);
			XAssert.NotEqual(kept.Interior, next!.Interior);
		}

		XAssert.Equal(1, arena.Stats.Retained);
	}

	[Fact]
	public void An_arena_filled_with_retained_boxes_refuses_rather_than_recycling()
	{
		// Refusing is the correct answer. The run reports the tests it could
		// not run and fails; it does not quietly carry on over the top of the
		// evidence.
		var arena = new Arena(Small(), new ArenaOptions { QuarantineTicks = 0 });
		BoxRequest request = BoxRequest.Banded(Band.Cavern, 16, 16);

		int retained = 0;

		while (arena.TryLease(request) is BoxLease lease) {
			arena.Release(lease, keepForInspection: true);
			retained++;
		}

		XAssert.True(retained > 0, "the arena should have fitted at least one box");
		XAssert.Equal(retained, arena.Stats.Retained);
		XAssert.Null(arena.TryLease(request));
	}
}
