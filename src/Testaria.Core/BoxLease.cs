namespace Testaria;

/// <summary>
/// A box checked out from the <see cref="Arena"/> for the duration of one test.
/// <para/>
/// Leases are returned and the underlying slot reused, so a suite of any size
/// runs in an arena sized by peak concurrency rather than by test count.
/// </summary>
public sealed class BoxLease
{
	internal BoxLease(int id, TileRect bounds, TileRect interior, Band bands, BoxKind kind)
	{
		Id = id;
		Bounds = bounds;
		Interior = interior;
		Bands = bands;
		Kind = kind;
	}

	/// <summary>Identifies this lease for the arena's own bookkeeping.</summary>
	public int Id { get; }

	/// <summary>
	/// The box's outer extent, gutter included. Two live leases never have
	/// overlapping bounds.
	/// </summary>
	public TileRect Bounds { get; }

	/// <summary>
	/// The usable area, being <see cref="Bounds"/> less the gutter. A test
	/// spawns inside this; anything found outside it is an escape.
	/// </summary>
	public TileRect Interior { get; }

	/// <summary>The band or bands this box occupies, gaps already filled.</summary>
	public Band Bands { get; }

	/// <summary>Whether this is a banded box or a spanning column.</summary>
	public BoxKind Kind { get; }

	/// <summary>Coordinates in the form <see cref="TestResult.Box"/> carries.</summary>
	public override string ToString() => Interior.ToString();
}

/// <summary>A snapshot of arena occupancy, for diagnostics and tests.</summary>
public readonly record struct ArenaStats
{
	/// <summary>Distinct slots the arena has ever carved out of the world.</summary>
	public required int SlotsCarved { get; init; }

	/// <summary>Slots currently leased out.</summary>
	public required int Active { get; init; }

	/// <summary>Slots released but not yet re-leasable.</summary>
	public required int Quarantined { get; init; }

	/// <summary>Slots held back for inspection after a failure.</summary>
	public required int Retained { get; init; }

	/// <summary>Slots available for immediate reuse.</summary>
	public required int Free { get; init; }
}
