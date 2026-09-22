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
	/// different radii (tile framing recurses one tile, lighting tens,
	/// SceneMetrics biome scanning more still), so this is a floor rather than
	/// a sufficient answer for biome-sensitive tests.
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
	/// </summary>
	public int QuarantineTicks { get; init; } = 60;

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
	/// </summary>
	public double ColumnRegionFraction { get; init; } = 0.25;
}
