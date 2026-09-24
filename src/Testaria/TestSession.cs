using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;

namespace Testaria;

/// <summary>
/// One run of a test suite inside a live game.
/// <para/>
/// Owns the arena, the runner, and where the results land. Stepped by
/// <see cref="TestariaSystem"/> from the game's own update loop rather than
/// running a loop of its own, because the tick loop is not ours to block.
/// </summary>
public sealed class TestSession
{
	private readonly TestRunner runner;

	private TestSession(TestRunner runner, RunPacing pacing, string runName, int discovered)
	{
		this.runner = runner;
		Pacing = pacing;
		RunName = runName;
		Discovered = discovered;
	}

	/// <summary>
	/// How fast this run may simulate, and whether the world is frozen right
	/// now. Owned by the session so it dies with the run.
	/// </summary>
	public RunPacing Pacing { get; }

	/// <summary>Keeps a bounded run to the rate it was asked for.</summary>
	public TickRateGovernor Governor { get; } = new();

	/// <summary>
	/// Launch parameter choosing the run's pacing without a console command,
	/// which is how a CI harness asks for it. Takes <c>max</c> for unbounded
	/// or a number of ticks per second for bounded.
	/// </summary>
	public const string SpeedFlag = "-testariaspeed";

	/// <summary>How many discovered tests the filter held back.</summary>
	public int FilteredOut => runner.FilteredOut;

	/// <summary>How many tests this run will actually attempt.</summary>
	public int Selected => Discovered - runner.FilteredOut;

	/// <summary>
	/// Launch parameter by which a harness promises this process has a world
	/// to itself, so tests marked <c>[FreshWorld]</c> can be honoured.
	/// </summary>
	public const string FreshWorldFlag = "-testariafreshworld";

	/// <summary>
	/// Launch parameter shifting every test's seed at once, for rerunning a
	/// suite against different rolls without editing it.
	/// </summary>
	public const string SeedFlag = "-testariaseed";

	/// <summary>The run seed every test's own seed is derived from.</summary>
	public int RunSeed { get; private set; }

	/// <summary>Name recorded on the run and used for the results file.</summary>
	public string RunName { get; }

	/// <summary>How many runnable tests discovery found.</summary>
	public int Discovered { get; }

	/// <summary>
	/// True once every test has finished and nothing is left to watch.
	/// <para/>
	/// The second half matters only for a measured run, which keeps watching
	/// released boxes for a while after the last test. Saying it had finished
	/// would stop the game stepping the session, and the watching would never
	/// end: the report is written when this turns true, so it has to mean
	/// "there is genuinely nothing left to do".
	/// </summary>
	public bool IsFinished => runner.State == RunnerState.Finished && ArenaMetrics.Pending == 0;

	/// <summary>Results so far, complete once <see cref="IsFinished"/>.</summary>
	public TestRunResult Result => runner.Result;

	/// <summary>The context of the running test, for whoever needs to watch it.</summary>
	public ITestContext? CurrentContext => runner.CurrentContext;

	/// <summary>Where the JUnit XML was written, once the run finished.</summary>
	public string? ResultsPath { get; private set; }

	/// <summary>
	/// Why the results could not be written, if they could not. Surfaced
	/// rather than logged, so whoever asked for the run hears about it.
	/// </summary>
	public string? ResultsError { get; private set; }

	/// <summary>
	/// Prepares a run over the given mods' assemblies.
	/// </summary>
	/// <param name="assemblies">Assemblies to scan. Typically every loaded mod's own code.</param>
	/// <param name="runName">Name for the run and its results file.</param>
	/// <param name="maxTier">
	/// Highest tier this environment can honour. A world must be loaded for
	/// anything above <see cref="TestTier.Loaded"/>.
	/// </param>
	/// <param name="filter">Narrows the run; null or <see cref="TestFilter.All"/> runs everything.</param>
	public static TestSession Create(IEnumerable<Assembly> assemblies, string runName, TestTier maxTier, TestFilter? filter = null)
	{
		ArgumentNullException.ThrowIfNull(assemblies);

		List<Type> types = [];
		foreach (Assembly assembly in assemblies)
			types.AddRange(AssemblyManager.GetLoadableTypes(assembly));

		DiscoveryResult discovery = TestDiscovery.Discover(types);

		int runSeed = ReadRunSeed();

		var pacing = new RunPacing();

		// The arena needs a loaded world to know where its bands are, so it is
		// only built when one exists. Without it, box-needing tests report an
		// error rather than running somewhere undefined.
		Arena? arena = null;

		if (maxTier >= TestTier.World && Main.maxTilesX > 0) {
			BlankWorldLayout? blank = BlankWorldSystem.Layout;

			// In a blank world the boundaries were chosen rather than derived,
			// so the layout is exact and the guessed space fraction in
			// TerrariaWorldGeometry does not come into it.
			WorldGeometry geometry = blank?.Geometry ?? TerrariaWorldGeometry.Current();

			arena = new Arena(geometry, new ArenaOptions {
				Reserved = blank?.Reserved ?? [],
				// A measured run holds boxes back longer than it otherwise
				// would, so that watching a released box measures the box
				// rather than its next tenant. Measured the hard way: with the
				// ordinary sixty tick quarantine, seven boxes appeared never to
				// go quiet, and what they were actually showing was the next
				// test building in them.
				QuarantineTicks = ArenaMetrics.Enabled
					? ArenaMetrics.ObservationTicks + 10
					: new ArenaOptions().QuarantineTicks,
			});
		}

		// The context wants the name of the test it belongs to, and the runner
		// is the thing that knows it. The runner needs the options to exist
		// first, so the reference is filled in a line after it is captured,
		// which is safe because the factory only runs once a test has begun.
		TestRunner? started = null;

		var runner = new TestRunner(discovery, new TestRunnerOptions {
			RunName = runName,
			MaxTier = maxTier,
			Arena = arena,
			Filter = filter ?? TestFilter.All,
			// Only when the harness says it has given this process a world of
			// its own. Claiming otherwise would let a [FreshWorld] test run in
			// a world shared with everything else and report a pass.
			SupportsFreshWorld = Program.LaunchParameters.ContainsKey(FreshWorldFlag),
			CreateContext = lease => new TestContext(lease, pacing, started?.CurrentTestName),
			Pacing = pacing,
			// Pinned for every tier, not just the ones with a world. A Tier 1
			// test reading a drop table or a recipe can roll too.
			Random = new TerrariaRandomControl(),
			RunSeed = runSeed,
		});

		started = runner;

		ArenaMetrics.Clear();

		var session = new TestSession(runner, pacing, runName, discovery.Tests.Count) { RunSeed = runSeed };
		session.ApplyLaunchPacing();

		return session;
	}

	/// <summary>
	/// Sets the run's pacing, retargeting the governor with it so a bounded
	/// rate starts measuring from now rather than inheriting a stale schedule.
	/// </summary>
	public void SetSpeed(PacingMode mode, double targetTicksPerSecond = 60)
	{
		Pacing.SetMode(mode, targetTicksPerSecond);

		if (mode == PacingMode.Bounded)
			Governor.Retarget(targetTicksPerSecond);
		else
			Governor.Reset();
	}

	/// <summary>
	/// Reads <c>-testariaseed</c>, defaulting to zero.
	/// <para/>
	/// Zero rather than the world seed or the clock, so that the default run
	/// of a suite draws the same rolls on every machine and in every world. A
	/// suite that only passes at one run seed depends on luck, and changing
	/// this flag is how that gets found rather than discovered by a user.
	/// </summary>
	private static int ReadRunSeed()
		=> Program.LaunchParameters.TryGetValue(SeedFlag, out string? value)
			&& int.TryParse(value?.Trim(), out int seed)
				? seed
				: 0;

	/// <summary>
	/// Reads <c>-testariaspeed</c> so a harness can fast forward without a
	/// console command. Unparseable values are ignored rather than fatal: a
	/// mistyped speed should not stop the suite running at all.
	/// </summary>
	private void ApplyLaunchPacing()
	{
		if (!Program.LaunchParameters.TryGetValue(SpeedFlag, out string? value))
			return;

		if (ParseSpeed(value) is (PacingMode mode, double rate))
			SetSpeed(mode, rate);
	}

	/// <summary>
	/// Parses a speed: <c>max</c> or <c>unbounded</c> for flat out,
	/// <c>realtime</c> or <c>60</c> for the game's own rate, any other
	/// positive number for that many ticks per second.
	/// </summary>
	public static (PacingMode Mode, double Rate)? ParseSpeed(string? value)
	{
		string text = (value ?? string.Empty).Trim();

		if (text.Length == 0)
			return null;

		if (text.Equals("max", StringComparison.OrdinalIgnoreCase) || text.Equals("unbounded", StringComparison.OrdinalIgnoreCase))
			return (PacingMode.Unbounded, 0);

		if (text.Equals("realtime", StringComparison.OrdinalIgnoreCase) || text.Equals("normal", StringComparison.OrdinalIgnoreCase))
			return (PacingMode.Realtime, 60);

		if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double rate) && rate > 0)
			return (PacingMode.Bounded, rate);

		return null;
	}

	/// <summary>Advances the run by one tick.</summary>
	/// <returns>True while there is more to do.</returns>
	public bool Step()
	{
		bool more = runner.Step();

		ArenaMetrics.Observe();

		// A measured run outlives its last test: released boxes are watched
		// for a while to see how long they take to go quiet, and the harness
		// stops the process the moment the report appears. So the report is
		// written last, after the watching is done.
		if (!more && ArenaMetrics.Pending > 0)
			return true;

		if (!more && ResultsPath is null && ResultsError is null) {
			MetricsPath = WriteMetrics();
			ResultsPath = WriteResults();
		}

		return more;
	}

	/// <summary>Where the measurements were written, when a run was measured.</summary>
	public string? MetricsPath { get; private set; }

	private string? WriteMetrics()
	{
		if (!ArenaMetrics.Enabled || ArenaMetrics.Count == 0)
			return null;

		try {
			string path = ResultsLocation.ForRun(Main.SavePath, RunName + "-arena", ".tsv");

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, ArenaMetrics.ToTsv());

			return path;
		}
		catch (Exception ex) {
			ResultsError = $"measurements: {ex.GetType().Name}: {ex.Message}";

			return null;
		}
	}

	/// <summary>A one-line summary suitable for a console or chat reply.</summary>
	public string Summarize()
	{
		TestRunResult result = Result;

		string held = FilteredOut > 0 ? $", {FilteredOut} filtered out" : string.Empty;

		return $"{result.Passed} passed, {result.Failures} failed, {result.Errors} errored, {result.Skipped} skipped{held} " +
			$"in {result.Duration.TotalSeconds:0.00}s";
	}

	private string? WriteResults()
	{
		try {
			// Under the save path and nowhere else: ModUploadRules rule 2
			// forbids a published mod writing outside it.
			string path = ResultsLocation.ForRun(Main.SavePath, RunName);

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JUnitXmlWriter.ToXml(Result));

			return path;
		}
		catch (Exception ex) {
			// A run whose results cannot be written still ran, so the failure
			// is reported alongside the outcome rather than replacing it.
			ResultsError = $"{ex.GetType().Name}: {ex.Message}";

			return null;
		}
	}
}
