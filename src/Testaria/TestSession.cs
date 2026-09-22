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

	private TestSession(TestRunner runner, string runName, int discovered)
	{
		this.runner = runner;
		RunName = runName;
		Discovered = discovered;
	}

	/// <summary>How many discovered tests the filter held back.</summary>
	public int FilteredOut => runner.FilteredOut;

	/// <summary>How many tests this run will actually attempt.</summary>
	public int Selected => Discovered - runner.FilteredOut;

	/// <summary>
	/// Launch parameter by which a harness promises this process has a world
	/// to itself, so tests marked <c>[FreshWorld]</c> can be honoured.
	/// </summary>
	public const string FreshWorldFlag = "-testariafreshworld";

	/// <summary>Name recorded on the run and used for the results file.</summary>
	public string RunName { get; }

	/// <summary>How many runnable tests discovery found.</summary>
	public int Discovered { get; }

	/// <summary>True once every test has finished.</summary>
	public bool IsFinished => runner.State == RunnerState.Finished;

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

			arena = new Arena(geometry, new ArenaOptions { Reserved = blank?.Reserved ?? [] });
		}

		var runner = new TestRunner(discovery, new TestRunnerOptions {
			RunName = runName,
			MaxTier = maxTier,
			Arena = arena,
			Filter = filter ?? TestFilter.All,
			// Only when the harness says it has given this process a world of
			// its own. Claiming otherwise would let a [FreshWorld] test run in
			// a world shared with everything else and report a pass.
			SupportsFreshWorld = Program.LaunchParameters.ContainsKey(FreshWorldFlag),
			CreateContext = lease => new TestContext(lease),
		});

		return new TestSession(runner, runName, discovery.Tests.Count);
	}

	/// <summary>Advances the run by one tick.</summary>
	/// <returns>True while there is more to do.</returns>
	public bool Step()
	{
		bool more = runner.Step();

		if (!more && ResultsPath is null && ResultsError is null)
			ResultsPath = WriteResults();

		return more;
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
