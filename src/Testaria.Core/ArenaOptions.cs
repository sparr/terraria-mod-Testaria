namespace Testaria;

/// <summary>
/// Tunables for <see cref="Arena"/>.
/// <para/>
/// Every default here is a starting guess, not a measurement. PLAN.md section
/// 7 lists them among the numbers that must be calibrated against a real test
/// corpus rather than hardcoded on intuition, which is why they are options
/// rather than constants.
/// </summary>
public sealed record ArenaOptions
{
	/// <summary>
	/// Tiles of dead space around every box. Cross-boundary effects have very
	/// different radii: tile framing recurses one tile, lighting tens, and the
	/// biome scan reaches <see cref="BiomeScan.HorizontalReach"/> tiles, which
	/// is now measured rather than guessed at and is eighty-four.
	/// <para/>
	/// So this is deliberately a floor and not a sufficient answer. Covering
	/// the biome scan would mean gutters ten times this, which buys nothing
	/// while boxes run one at a time: a released box has its tiles restored
	/// and sits in quarantine before the next tenant arrives, so there is
	/// nothing left to leak. It becomes real the moment two boxes run at once,
	/// which is where that cost belongs.
	/// </summary>
	public int Gutter { get; init; } = 8;

	/// <summary>
	/// Interior widths a request is rounded up to. Size classes are what keep
	/// a released box interchangeable with a later request, so the arena does
	/// not fragment as a suite grows.
	/// </summary>
	public IReadOnlyList<int> WidthClasses { get; init; } = [48, 96, 192, 384];

	/// <summary>Interior heights a banded request is rounded up to.</summary>
	public IReadOnlyList<int> HeightClasses { get; init; } = [32, 64, 128, 256];

	/// <summary>
	/// Ticks a released box waits before it can be leased again. Queued liquid
	/// updates, lighting propagation, and despawn timers all outlive teardown,
	/// so re-leasing immediately would hand the next tenant someone else's
	/// leftovers.
	/// <para/>
	/// Twelve, measured rather than guessed (PLAN.md section 8.5f). Across 33
	/// boxed tests, 31 were quiet the tick after teardown and the slowest took
	/// two, so the old sixty was thirty times what anything needed. Twelve
	/// keeps six times the observed worst case.
	/// <para/>
	/// The honest limit on that measurement: no test in the corpus used liquid,
	/// which is the slowest of the effects this number exists to outlast. Raise
	/// it if a suite full of water starts handing boxes on wet.
	/// </summary>
	public int QuarantineTicks { get; init; } = 12;

	/// <summary>
	/// Ground the arena must never lease, holding whatever vanilla requires to
	/// exist. Declared by whoever built the world, since that is the only thing
	/// that knows where it put them.
	/// </summary>
	public IReadOnlyList<ReservedArea> Reserved { get; init; } = [];

	/// <summary>
	/// Fraction of world width reserved for spanning columns. Partitioning the
	/// world is what avoids the alternative, where a column must acquire an
	/// aligned slot in every row it crosses at once, which is hold-and-wait
	/// and deadlocks as soon as two columns allocate concurrently.
	/// <para/>
	/// Still uncalibrated, and honestly so: not one test in the measured corpus
	/// of 967 asked for a column (PLAN.md section 8.5f), so a quarter of the
	/// world is reserved for a case nothing exercises. It costs nothing today,
	/// since the arena is sized by concurrency and runs are sequential, and it
	/// cannot be calibrated until a suite exists that tests band boundaries.
	/// </summary>
	public double ColumnRegionFraction { get; init; } = 0.25;
}
