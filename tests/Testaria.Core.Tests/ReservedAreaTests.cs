namespace Testaria.Tests;

public class ReservedAreaTests
{
	private static WorldGeometry Small() => new(4200, 1200, 87, 250, 400, 1000);

	/// <summary>A dungeon-sized block sitting in the Cavern band, where boxes also go.</summary>
	private static ReservedArea Dungeon() => new() {
		Name = "Dungeon",
		Bounds = new TileRect(0, 400, 300, 200),
		Reason = "Vanilla assumes a dungeon exists.",
	};

	private static Arena WithDungeon(ArenaOptions? options = null)
		=> new(Small(), (options ?? new ArenaOptions()) with { Reserved = [Dungeon()] });

	[Fact]
	public void Ordinary_leases_never_land_on_reserved_ground()
	{
		Arena arena = WithDungeon();
		TileRect dungeon = Dungeon().Bounds;

		for (int i = 0; i < 20; i++) {
			BoxLease lease = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;
			XAssert.False(lease.Bounds.Intersects(dungeon), $"lease at {lease.Bounds} overlaps the dungeon");
		}
	}

	[Fact]
	public void Boxes_step_over_reserved_ground_rather_than_being_refused()
	{
		// The arena has room to spare, so a test should not be denied a box
		// merely because the dungeon sits to its left.
		Arena arena = WithDungeon();
		BoxLease lease = arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!;

		XAssert.NotNull(lease);
		XAssert.True(lease.Bounds.Left >= Dungeon().Bounds.Right, $"expected to start past the dungeon, got {lease.Bounds}");
	}

	[Fact]
	public void A_band_with_no_reserved_ground_is_unaffected()
	{
		Arena arena = WithDungeon();
		BoxLease surface = arena.TryLease(BoxRequest.Banded(Band.Surface, 48, 32))!;

		// The dungeon is in the Cavern band, so the Surface row starts at zero.
		XAssert.Equal(0, surface.Bounds.Left);
	}

	[Fact]
	public void Spanning_columns_also_step_over_reserved_ground()
	{
		var tall = new ReservedArea { Name = "Spine", Bounds = new TileRect(3200, 0, 200, 1200) };
		var arena = new Arena(Small(), new ArenaOptions { Reserved = [tall] });

		for (int i = 0; i < 4; i++) {
			BoxLease? column = arena.TryLease(BoxRequest.Spanning(Band.All, 48));
			if (column is null)
				break;

			XAssert.False(column.Bounds.Intersects(tall.Bounds), $"column at {column.Bounds} overlaps reserved ground");
		}
	}

	[Fact]
	public void Reserved_ground_can_be_leased_by_name_when_a_test_asks_for_it()
	{
		// Depending on vanilla furniture should be a visible declaration, not
		// an accident of where the arena happened to place a box.
		Arena arena = WithDungeon();
		BoxLease lease = arena.TryLeaseReserved("Dungeon")!;

		XAssert.NotNull(lease);
		XAssert.Equal(Dungeon().Bounds, lease.Bounds);
	}

	[Fact]
	public void Leasing_a_reserved_area_by_name_is_case_insensitive()
		=> XAssert.NotNull(WithDungeon().TryLeaseReserved("dungeon"));

	[Fact]
	public void A_reserved_area_cannot_be_leased_twice_at_once()
	{
		Arena arena = WithDungeon();
		arena.TryLeaseReserved("Dungeon");

		XAssert.Null(arena.TryLeaseReserved("Dungeon"));
	}

	[Fact]
	public void Releasing_a_reserved_lease_makes_it_available_again()
	{
		Arena arena = WithDungeon();
		BoxLease first = arena.TryLeaseReserved("Dungeon")!;

		arena.Release(first);

		XAssert.NotNull(arena.TryLeaseReserved("Dungeon"));
	}

	[Fact]
	public void A_reserved_lease_shows_up_as_active_and_then_does_not()
	{
		Arena arena = WithDungeon();
		BoxLease lease = arena.TryLeaseReserved("Dungeon")!;

		XAssert.Contains(lease, arena.Active);

		arena.Release(lease);

		XAssert.DoesNotContain(lease, arena.Active);
	}

	[Fact]
	public void Asking_for_an_unknown_reserved_area_says_what_exists()
	{
		var ex = XAssert.Throws<ArgumentException>(() => WithDungeon().TryLeaseReserved("Temple"));

		XAssert.Contains("Dungeon", ex.Message);
	}

	[Fact]
	public void Asking_for_a_reserved_area_when_none_exist_says_so()
	{
		var arena = new Arena(Small());
		var ex = XAssert.Throws<ArgumentException>(() => arena.TryLeaseReserved("Dungeon"));

		XAssert.Contains("none", ex.Message);
	}

	[Fact]
	public void Reserved_areas_are_visible_for_diagnostics()
	{
		Arena arena = WithDungeon();

		XAssert.Single(arena.Reserved);
		XAssert.Equal("Dungeon", arena.Reserved[0].Name);
		XAssert.Contains("dungeon", arena.Reserved[0].Reason!, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void No_reserved_ground_means_the_arena_behaves_exactly_as_before()
	{
		// Reservation is a property of how the world was built, so an arena
		// told nothing reserves nothing.
		var arena = new Arena(Small());

		XAssert.Empty(arena.Reserved);
		XAssert.Equal(0, arena.TryLease(BoxRequest.Banded(Band.Cavern, 48, 32))!.Bounds.Left);
	}
}
