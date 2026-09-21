namespace Testaria;

/// <summary>
/// Hands out boxes for tests to run in, and takes them back.
/// <para/>
/// A test suite grows without bound; the world does not. So boxes are leased
/// and returned rather than assigned once up front, which means the arena is
/// sized by peak concurrency rather than by test count. Running tests
/// sequentially, the whole arena is about two boxes: one live, one in
/// quarantine.
/// <para/>
/// The world is partitioned horizontally into a banded region, where boxes
/// pack into rows one per <see cref="Band"/>, and a column region reserved for
/// spanning boxes. Letting columns punch down through the banded rows would
/// force a column to acquire an aligned slot in every row it crosses at once,
/// which is hold-and-wait and deadlocks the moment two columns allocate
/// concurrently. Partitioning trades some idle space for not having that
/// problem at all.
/// <para/>
/// This type is not thread safe. Terraria's tick loop is single threaded and
/// the scheduler runs on it.
/// </summary>
public sealed class Arena
{
	private readonly WorldGeometry geo;
	private readonly List<Slot> allSlots = [];
	private readonly Dictionary<SlotKey, List<Slot>> byKey = [];
	private readonly Dictionary<int, Slot> leasedSlots = [];
	private readonly Dictionary<int, BoxLease> activeLeases = [];
	private readonly Dictionary<Band, int> bandCursor = [];
	private readonly int columnRegionStart;
	private int columnCursor;
	private int nextLeaseId = 1;

	/// <summary>Creates an arena over a world.</summary>
	public Arena(WorldGeometry geometry, ArenaOptions? options = null)
	{
		geo = geometry ?? throw new ArgumentNullException(nameof(geometry));
		Options = options ?? new ArenaOptions();

		Validate(Options);

		columnRegionStart = geo.MaxTilesX - (int)(geo.MaxTilesX * Options.ColumnRegionFraction);
		columnCursor = columnRegionStart;
	}

	/// <summary>The tunables this arena was built with.</summary>
	public ArenaOptions Options { get; }

	/// <summary>First tile column belonging to the spanning region.</summary>
	public int ColumnRegionStart => columnRegionStart;

	/// <summary>Currently leased boxes.</summary>
	public IReadOnlyCollection<BoxLease> Active => activeLeases.Values;

	/// <summary>A snapshot of occupancy.</summary>
	public ArenaStats Stats => new() {
		SlotsCarved = allSlots.Count,
		Active = allSlots.Count(s => s.State == SlotState.Leased),
		Quarantined = allSlots.Count(s => s.State == SlotState.Quarantined),
		Retained = allSlots.Count(s => s.State == SlotState.Retained),
		Free = allSlots.Count(s => s.State == SlotState.Free),
	};

	/// <summary>
	/// Leases a box, reusing a free slot of the same class when one exists and
	/// carving a new one otherwise.
	/// </summary>
	/// <returns>
	/// The lease, or null when the arena has no room left. Null means the
	/// caller should wait for a lease to come back rather than proceed, since
	/// the alternative is two tests sharing ground.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The request is larger than the largest size class, or taller than the
	/// band it asked for. Both are author errors that no amount of waiting
	/// fixes, so they throw rather than returning null.
	/// </exception>
	public BoxLease? TryLease(BoxRequest request)
	{
		BoxKind kind = request.Kind;
		int widthClass = RoundUp(request.Width, Options.WidthClasses, "width");
		int heightClass = kind == BoxKind.Banded ? RoundUp(request.Height, Options.HeightClasses, "height") : 0;
		var key = new SlotKey(request.Bands, widthClass, heightClass, kind);

		Slot? slot = FindFree(key) ?? Carve(key);
		if (slot is null)
			return null;

		int id = nextLeaseId++;
		var lease = new BoxLease(id, slot.Bounds, slot.Interior, key.Bands, kind);

		slot.State = SlotState.Leased;
		leasedSlots[id] = slot;
		activeLeases[id] = lease;

		return lease;
	}

	/// <summary>
	/// Returns a box. It enters quarantine rather than becoming immediately
	/// available, because queued liquid, lighting propagation, and despawn
	/// timers all outlive teardown.
	/// </summary>
	/// <param name="lease">The lease to return.</param>
	/// <param name="keepForInspection">
	/// When true the box is retained rather than recycled, so a failing test's
	/// wreckage survives for an author to fly out and look at. Retained boxes
	/// consume arena capacity permanently.
	/// </param>
	public void Release(BoxLease lease, bool keepForInspection = false)
	{
		ArgumentNullException.ThrowIfNull(lease);

		if (!leasedSlots.Remove(lease.Id, out Slot? slot))
			throw new InvalidOperationException($"Lease {lease.Id} is not active; it was already released or never issued by this arena.");

		activeLeases.Remove(lease.Id);

		if (keepForInspection) {
			slot.State = SlotState.Retained;
			return;
		}

		if (Options.QuarantineTicks <= 0) {
			slot.State = SlotState.Free;
			return;
		}

		slot.State = SlotState.Quarantined;
		slot.QuarantineRemaining = Options.QuarantineTicks;
	}

	/// <summary>
	/// Advances quarantine by one game tick. A box whose quarantine has
	/// elapsed becomes available again.
	/// </summary>
	public void Tick()
	{
		foreach (Slot slot in allSlots) {
			if (slot.State != SlotState.Quarantined)
				continue;

			if (--slot.QuarantineRemaining <= 0)
				slot.State = SlotState.Free;
		}
	}

	private Slot? FindFree(SlotKey key)
	{
		if (!byKey.TryGetValue(key, out List<Slot>? candidates))
			return null;

		foreach (Slot slot in candidates) {
			if (slot.State == SlotState.Free)
				return slot;
		}

		return null;
	}

	private Slot? Carve(SlotKey key)
	{
		int outerWidth = key.Width + (Options.Gutter * 2);
		TileRect bounds;

		if (key.Kind == BoxKind.Banded) {
			TileRect band = geo.BandBounds(key.Bands);
			int outerHeight = key.Height + (Options.Gutter * 2);

			if (outerHeight > band.Height) {
				throw new ArgumentOutOfRangeException(
					nameof(key),
					$"A {key.Height} tall box plus {Options.Gutter} tile gutters needs {outerHeight} rows, " +
					$"but band {key.Bands} is only {band.Height} rows tall in this world. " +
					"Ask for a shorter box, a different band, or a fresh world.");
			}

			int x = bandCursor.GetValueOrDefault(key.Bands);
			if (x + outerWidth > columnRegionStart)
				return null;

			bandCursor[key.Bands] = x + outerWidth;
			bounds = new TileRect(x, band.Top, outerWidth, outerHeight);
		}
		else {
			TileRect span = geo.Span(key.Bands);

			if (span.Height <= Options.Gutter * 2) {
				throw new ArgumentOutOfRangeException(
					nameof(key),
					$"Bands {key.Bands} span only {span.Height} rows, which leaves no interior " +
					$"once {Options.Gutter} tile gutters are applied.");
			}

			if (columnCursor + outerWidth > geo.MaxTilesX)
				return null;

			bounds = new TileRect(columnCursor, span.Top, outerWidth, span.Height);
			columnCursor += outerWidth;
		}

		var slot = new Slot {
			Key = key,
			Bounds = bounds,
			Interior = bounds.Deflate(Options.Gutter),
		};

		allSlots.Add(slot);

		if (!byKey.TryGetValue(key, out List<Slot>? list))
			byKey[key] = list = [];

		list.Add(slot);

		return slot;
	}

	private static int RoundUp(int requested, IReadOnlyList<int> classes, string dimension)
	{
		foreach (int candidate in classes) {
			if (candidate >= requested)
				return candidate;
		}

		throw new ArgumentOutOfRangeException(
			nameof(requested),
			requested,
			$"Requested {dimension} {requested} exceeds the largest size class {classes[^1]}. " +
			"Size classes exist so that a released box is interchangeable with a later request; " +
			"a box larger than any class would fragment the arena. Use a fresh world instead.");
	}

	private static void Validate(ArenaOptions options)
	{
		if (options.Gutter < 0)
			throw new ArgumentException($"Gutter must not be negative, got {options.Gutter}.", nameof(options));

		if (options.ColumnRegionFraction is <= 0 or >= 1)
			throw new ArgumentException($"ColumnRegionFraction must lie strictly between 0 and 1, got {options.ColumnRegionFraction}.", nameof(options));

		Ascending(options.WidthClasses, nameof(options.WidthClasses));
		Ascending(options.HeightClasses, nameof(options.HeightClasses));

		static void Ascending(IReadOnlyList<int> classes, string name)
		{
			if (classes.Count == 0)
				throw new ArgumentException($"{name} must not be empty.", nameof(options));

			for (int i = 0; i < classes.Count; i++) {
				if (classes[i] <= 0)
					throw new ArgumentException($"{name} must be positive, got {classes[i]}.", nameof(options));

				if (i > 0 && classes[i] <= classes[i - 1])
					throw new ArgumentException($"{name} must ascend strictly, got {classes[i - 1]} then {classes[i]}.", nameof(options));
			}
		}
	}

	private enum SlotState
	{
		Free,
		Leased,
		Quarantined,
		Retained,
	}

	private readonly record struct SlotKey(Band Bands, int Width, int Height, BoxKind Kind);

	private sealed class Slot
	{
		public required SlotKey Key { get; init; }
		public required TileRect Bounds { get; init; }
		public required TileRect Interior { get; init; }
		public SlotState State { get; set; } = SlotState.Free;
		public int QuarantineRemaining { get; set; }
	}
}
