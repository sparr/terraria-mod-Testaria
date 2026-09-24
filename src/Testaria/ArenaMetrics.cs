using System.Diagnostics;
using System.Globalization;
using System.Text;
using Terraria;

namespace Testaria;

/// <summary>
/// Measures what boxes actually cost and what tests actually use, so that the
/// arena's constants can be calibrated against a real corpus instead of being
/// argued about.
/// <para/>
/// PLAN.md risk 3 names five numbers that must be measured rather than
/// guessed. One of them, the biome scan radius, was readable in the game's own
/// source. The rest are properties of a test suite, not of Terraria, and there
/// was no suite to measure until there was: this writes a row per boxed test so
/// the answers come from a thousand of them rather than from intuition.
/// <para/>
/// Off unless asked for, because a run should not pay for instrumentation
/// nobody is reading.
/// </summary>
public static class ArenaMetrics
{
	/// <summary>Launch parameter turning measurement on.</summary>
	public const string Flag = "-testariameasure";

	/// <summary>
	/// Ticks a released box is watched for before its row is written. Three
	/// seconds is far longer than anything teardown is expected to leave
	/// behind, which is the point: the question is how long quiet takes, so the
	/// window has to be able to answer "longer than you thought".
	/// </summary>
	public const int ObservationTicks = 180;

	private static readonly List<Row> Rows = [];
	private static readonly List<Watch> Watching = [];

	/// <summary>True when this process was asked to measure.</summary>
	public static bool Enabled => Program.LaunchParameters.ContainsKey(Flag);

	/// <summary>One boxed test's costs and footprint.</summary>
	private sealed record Row
	{
		public required string Test { get; init; }

		public required int GrantedWidth { get; init; }

		public required int GrantedHeight { get; init; }

		public required double SnapshotMs { get; init; }

		public double RestoreMs { get; set; }

		public int RestoredTiles { get; set; }

		public int UsedWidth { get; set; }

		public int UsedHeight { get; set; }

		public int RoamedWidth { get; set; }

		public int RoamedHeight { get; set; }

		public int TicksToQuiet { get; set; } = -1;

		/// <summary>What was still disturbing the box the last time it was looked at.</summary>
		public string Disturbance { get; set; } = string.Empty;
	}

	private sealed class Watch
	{
		public required Row Row { get; init; }

		public required BoxSnapshot Snapshot { get; init; }

		public int Elapsed { get; set; }

		public int LastDisturbance { get; set; }
	}

	/// <summary>Forgets everything, at the start of a run.</summary>
	public static void Clear()
	{
		Rows.Clear();
		Watching.Clear();
	}

	/// <summary>
	/// Takes the snapshot a box will be restored from, timing the cost.
	/// </summary>
	/// <param name="interior">The box's interior.</param>
	/// <param name="test">The test the box is for, for the row.</param>
	public static (BoxSnapshot Snapshot, object? Handle) Begin(TileRect interior, string? test)
	{
		var timer = Stopwatch.StartNew();
		var snapshot = new BoxSnapshot(interior);
		timer.Stop();

		if (!Enabled)
			return (snapshot, null);

		var row = new Row {
			Test = test ?? "<unknown>",
			GrantedWidth = interior.Width,
			GrantedHeight = interior.Height,
			SnapshotMs = timer.Elapsed.TotalMilliseconds,
		};

		Rows.Add(row);

		return (snapshot, row);
	}

	/// <summary>
	/// Records how far a test's own entities ranged, in tiles.
	/// <para/>
	/// The measurement that actually bears on box size. Ground changes turn out
	/// to be a poor proxy: most tests change no tiles at all, while an NPC left
	/// to its own devices covers ground without touching any of it, and it is
	/// the roaming that decides how big a box has to be.
	/// </summary>
	public static void Roamed(object? handle, int width, int height)
	{
		if (handle is not Row row)
			return;

		row.RoamedWidth = Math.Max(row.RoamedWidth, width);
		row.RoamedHeight = Math.Max(row.RoamedHeight, height);
	}

	/// <summary>
	/// Records what the test used and what putting the box back cost, and
	/// starts watching the box for how long it takes to go quiet.
	/// </summary>
	public static void Restored(object? handle, BoxSnapshot snapshot, TileRect? used, double restoreMs, int restoredTiles)
	{
		if (handle is not Row row)
			return;

		row.RestoreMs = restoreMs;
		row.RestoredTiles = restoredTiles;
		row.UsedWidth = used?.Width ?? 0;
		row.UsedHeight = used?.Height ?? 0;

		Watching.Add(new Watch { Row = row, Snapshot = snapshot });
	}

	/// <summary>
	/// Looks at every released box that is still being watched, and notes the
	/// last tick at which it was anything other than quiet.
	/// <para/>
	/// Quiet means what re-leasing would need it to mean: nothing active
	/// standing in it, and the ground still as teardown left it. A box that
	/// changes on its own after teardown is one that liquid is still flowing
	/// through, or that something is still falling into.
	/// </summary>
	public static void Observe()
	{
		if (!Enabled || Watching.Count == 0)
			return;

		for (int i = Watching.Count - 1; i >= 0; i--) {
			Watch watch = Watching[i];

			watch.Elapsed++;

			if (Disturbance(watch.Snapshot) is string reason) {
				watch.LastDisturbance = watch.Elapsed;
				watch.Row.Disturbance = reason;
			}

			if (watch.Elapsed < ObservationTicks)
				continue;

			watch.Row.TicksToQuiet = watch.LastDisturbance;
			Watching.RemoveAt(i);
		}
	}

	/// <summary>
	/// What is keeping a released box from being re-leasable, or null when
	/// nothing is.
	/// <para/>
	/// Naming it rather than answering yes or no, because "this box never went
	/// quiet" is not a diagnosis. Whether it is an NPC that outlived teardown
	/// or a tile that keeps changing decides entirely what to do about it.
	/// </summary>
	private static string? Disturbance(BoxSnapshot snapshot)
	{
		TileRect interior = snapshot.Interior;

		for (int i = 0; i < Main.npc.Length; i++) {
			if (Main.npc[i].active && Inside(interior, Main.npc[i].Center))
				return $"npc {i} type {Main.npc[i].type}";
		}

		for (int i = 0; i < Main.maxProjectiles; i++) {
			if (Main.projectile[i].active && Inside(interior, Main.projectile[i].Center))
				return $"projectile {i} type {Main.projectile[i].type}";
		}

		for (int i = 0; i < Main.maxItems; i++) {
			if (Main.item[i].active && Inside(interior, Main.item[i].Center))
				return $"item {i} type {Main.item[i].type}";
		}

		for (int i = 0; i < Main.maxPlayers; i++) {
			if (Main.player[i].active && Inside(interior, Main.player[i].Center))
				return $"player {i}";
		}

		return snapshot.ChangedBounds() is TileRect changed ? $"tiles {changed}" : null;
	}

	private static bool Inside(TileRect interior, Microsoft.Xna.Framework.Vector2 position)
		=> BoxSpace.Contains(interior, new WorldPoint(position.X, position.Y));

	/// <summary>Renders every row as a tab separated table.</summary>
	public static string ToTsv()
	{
		var text = new StringBuilder();

		text.AppendLine(string.Join('\t',
			"test", "grantedW", "grantedH",
			"usedW", "usedH", "roamedW", "roamedH",
			"restoredTiles", "snapshotMs", "restoreMs", "ticksToQuiet", "disturbance"));

		foreach (Row row in Rows) {
			text.AppendLine(string.Join('\t',
				row.Test,
				N(row.GrantedWidth), N(row.GrantedHeight),
				N(row.UsedWidth), N(row.UsedHeight),
				N(row.RoamedWidth), N(row.RoamedHeight),
				N(row.RestoredTiles),
				row.SnapshotMs.ToString("F3", CultureInfo.InvariantCulture),
				row.RestoreMs.ToString("F3", CultureInfo.InvariantCulture),
				N(row.TicksToQuiet),
				row.Disturbance));
		}

		return text.ToString();

		static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>How many rows have been collected.</summary>
	public static int Count => Rows.Count;

	/// <summary>Boxes still being watched, which hold the run open a little past its last test.</summary>
	public static int Pending => Watching.Count;
}
