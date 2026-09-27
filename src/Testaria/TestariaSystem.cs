using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;

namespace Testaria;

/// <summary>
/// Drives a <see cref="TestSession"/> from the game's update loop.
/// <para/>
/// <c>PostUpdateEverything</c> rather than a thread, because the whole design
/// depends on test bodies resuming at known points in the tick, and because
/// almost nothing in Terraria is safe to touch off the update thread.
/// <para/>
/// Autoloading only finds content types in a mod's own assembly
/// (<c>Mod.Internals.cs:71</c> enumerates <c>GetLoadableTypes(Code)</c>), so
/// this type has to be compiled into the Testaria mod itself rather than
/// supplied from a referenced library.
/// </summary>
public sealed class TestariaSystem : ModSystem
{
	private static TestSession? session;

	/// <summary>The run in progress, if any.</summary>
	public static TestSession? Current => session;

	/// <summary>True while a run is under way.</summary>
	public static bool IsRunning => session is { IsFinished: false };

	/// <summary>
	/// Starts a run over every loaded mod's own assembly.
	/// </summary>
	/// <returns>The session, or null if one is already running.</returns>
	public static TestSession? Start(string runName, TestFilter? filter = null)
	{
		if (IsRunning)
			return null;

		// Without a world, Tier 2 and above cannot be honoured, and the runner
		// reports them as skipped rather than running them somewhere undefined.
		TestTier maxTier = Main.maxTilesX > 0 ? TestTier.World : TestTier.Loaded;

		// Tier 3 needs a second process that has actually arrived. Counted
		// rather than promised: a harness flag saying a client was launched
		// would let a netcode test run single-player and report a pass when
		// the client had failed to connect, which is the one thing worth being
		// paranoid about here.
		if (maxTier == TestTier.World && ClientLink.ConnectedClients > 0)
			maxTier = TestTier.MultiProcess;

		ClientLink.Clear();

		// To the console, where the harness can see it. A mod that was asked
		// for and did not load is the quietest way for a run to test nothing:
		// measured, a run with Calamity enabled reported six of six tests
		// passing while Calamity had failed to load and been disabled.
		Console.WriteLine("Testaria: mods loaded: " + string.Join(" ", ModLoader.Mods.Select(m => m.Name)));

		ClearStrayNpcs();

		List<Assembly> assemblies = LoadedModAssemblies();

		session = TestSession.Create(assemblies, runName, maxTier, filter);

		return session;
	}

	/// <summary>
	/// Empties the world of NPCs before a run begins.
	/// <para/>
	/// Natural spawning is suppressed while a run is in progress, but not
	/// before one starts, and the window between a world loading and the first
	/// test is long enough for the game to put a critter somewhere. It then
	/// sits there until a box happens to be leased around it, and the test
	/// unlucky enough to get that box is failed for contamination it had
	/// nothing to do with. A worm did exactly that, intermittently.
	/// <para/>
	/// Nothing owned by a test can be caught by this, because no test has
	/// started yet.
	/// </summary>
	private static void ClearStrayNpcs()
	{
		if (Main.maxTilesX <= 0)
			return;

		for (int i = 0; i < Main.npc.Length; i++) {
			if (Main.npc[i].active)
				Main.npc[i].active = false;
		}
	}

	/// <summary>
	/// Forces the dedicated server to keep simulating while a run is active.
	/// <para/>
	/// An empty server does not tick. Its loop reads
	/// <c>Netplay.HasFullyConnectedClients</c> and only calls
	/// <c>Game.Update</c> when it is set, so with nobody connected the world
	/// is paused and <see cref="PostUpdateEverything"/> never fires. Measured:
	/// the world clock reads the same time eight seconds apart.
	/// <para/>
	/// The field's only writer is the private <c>Netplay.UpdateConnectedClients</c>,
	/// which is called from <c>Netplay.ServerLoop</c> on the server's own
	/// network thread, continuously and independently of the main loop. It is
	/// not reached from <c>Netplay.UpdateInMainThread</c>, which only pumps
	/// incoming bytes, and which the main loop calls solely on the branch it
	/// takes when nobody is connected. Setting the flag immediately after that
	/// writer runs, on that writer's own thread, is therefore the one point
	/// where the value survives to the main loop's next check.
	/// <para/>
	/// Scoped as tightly as possible: dedicated server only, and only while a
	/// run is actually in progress, so a normal server is never affected.
	/// </summary>
	public override void Load()
	{
		// A skip is not an incident, and the log is where modders look.
		//
		// Assert.Skip throws, and tModLoader's first-chance handler writes every
		// throw to the log as a Silently Caught Exception, deduplicated by the
		// text of the exception rather than by its type. A skip names its
		// subject, so each one is its own entry: measured on a seven-mod sweep,
		// 177 of the log's 206 entries were skips, in front of whatever was
		// actually wrong.
		//
		// Logging.ignoreContents is matched against the stack trace the handler
		// takes at throw time rather than against the exception, so naming the
		// throwing method suppresses exactly our skips. Not the assembly,
		// through IgnoreExceptionSource: that would also silence a failing
		// assertion, which is an incident worth logging.
		//
		// This takes the same sweep from 177 skip entries to 3, and the last
		// three needed the other half of the fix: they were second events for
		// the same exception, raised where reflection handed it back, with a
		// stack that no longer named Assert.Skip. TestInvoker removed the
		// reflection, and with it those three. The sweep now logs none, with 245
		// skips in it.
		//
		// There is no removal API and no need for one. The list ignores a
		// duplicate, so a reload adds nothing, and the pattern names a method
		// by string rather than holding anything of this assembly alive.
		Logging.IgnoreExceptionContents($"{typeof(Assert).FullName}.{nameof(Assert.Skip)}");

		// The freeze gate is what pause and single stepping are built on, and
		// it works wherever a world ticks, so it goes in before the
		// server-only pacing work below.
		Terraria.On_Main.DoUpdateInWorld += HoldTheWorldStill;

		if (!Main.dedServ)
			return;

		NetTrace.Install(Mod);

		try {
			// Hook the sole writer of the flag, not the reader's neighbour.
			//
			// Netplay.UpdateConnectedClients is the only thing that assigns
			// HasFullyConnectedClients, and it runs on the network thread from
			// Netplay.ServerLoop, continuously. Setting the flag anywhere on
			// the main thread loses a race against it: measured, the world
			// ticked exactly once in thousands of attempts. Setting it
			// immediately after that writer, on that writer's own thread, is
			// the only placement that holds.
			MethodInfo update = typeof(Netplay).GetMethod(
				"UpdateConnectedClients",
				BindingFlags.NonPublic | BindingFlags.Static)
				?? throw new InvalidOperationException("Netplay.UpdateConnectedClients not found; the server tick workaround needs updating for this tModLoader version.");

			MonoModHooks.Add(update, ForceTickWhileRunning);
			Mod.Logger.Info("Testaria: server tick hook installed.");

			// Unpacing only makes sense where DedServ owns the loop.
			MonoModHooks.Add(
				typeof(System.Threading.Thread).GetMethod("Sleep", [typeof(int)])
					?? throw new InvalidOperationException("Thread.Sleep(int) not found; fast forward needs updating for this runtime."),
				SkipPacingSleep);

			mainThreadId = Environment.CurrentManagedThreadId;
			Mod.Logger.Info("Testaria: fast forward hook installed.");
		}
		catch (Exception ex) {
			// Reported rather than swallowed: without this hook a dedicated
			// server never steps the runner, and the symptom is a run that
			// starts and then simply never finishes.
			Mod.Logger.Error($"Testaria: could not install the server tick hook, Tier 2 tests will hang. {ex}");
		}
	}

	private static long hookCalls;
	private static bool loggedFirstHook;
	private static bool loggedFirstForce;
	private static int mainThreadId;
	private static int frozenFrames;

	/// <summary>
	/// Real frames a test may leave the world frozen before the harness steps
	/// in. Thirty seconds at 60 frames a second: long enough for a human to
	/// single step through a problem from the console, short enough that a
	/// test which pauses and forgets to resume does not hang the suite.
	/// </summary>
	private const int FrozenFrameLimit = 1800;

	private delegate void OrigSleep(int milliseconds);

	/// <summary>
	/// Swallows the dedicated server's own pacing sleep so a run can simulate
	/// faster than 60 ticks per second.
	/// <para/>
	/// The server paces itself with a local in <c>Main.DedServ</c>, which no
	/// mod can reach: mods are loaded from inside that very method, so the
	/// frame running the loop is already on the stack before any hook exists.
	/// What is reachable is the <c>Thread.Sleep</c> it calls, and skipping
	/// that leaves the loop doing exactly one <c>Update</c> per iteration, so
	/// every hook keeps its normal order and ratio. Only the wall clock
	/// changes.
	/// <para/>
	/// Scoped hard, because this hook sees every sleep in the process. The
	/// pacing sleep is 15 to 16ms on the main thread; the far more numerous
	/// spin-wait sleeps inside <c>FastParallel</c> are 0 to 1ms, and must not
	/// pay for a stack walk. Measured over a run: with these gates, every
	/// walk was a hit and none were wasted.
	/// </summary>
	private static void SkipPacingSleep(OrigSleep orig, int milliseconds)
	{
		if (milliseconds >= 2 && Environment.CurrentManagedThreadId == mainThreadId) {
			try {
				bool fromPacingLoop = new System.Diagnostics.StackTrace(1, false)
					.GetFrames()
					.Take(8)
					.Any(frame => frame.GetMethod()?.Name == "DedServ");

				if (fromPacingLoop && PacingSleepFor(milliseconds) is int shortened) {
					if (shortened <= 0)
						return;

					orig(shortened);
					return;
				}
			}
			catch {
				// A stack walk that fails must never stop the server sleeping,
				// which would spin a core for the rest of the process.
			}
		}

		orig(milliseconds);
	}

	/// <summary>
	/// How long the pacing loop should actually sleep, or null to leave it
	/// alone.
	/// <para/>
	/// Shortening the sleep rather than skipping it is what makes a bounded
	/// rate work. The loop's own sleep is all or nothing at about 16ms, so
	/// skipping it hands the rate to the CPU and keeping it pins the rate at
	/// 60. Sleeping until the next tick is due gives any rate in between,
	/// while still doing exactly one update per iteration, which is what keeps
	/// every hook at its normal cadence.
	/// </summary>
	private static int? PacingSleepFor(int requested)
	{
		if (session is not { IsFinished: false } running)
			return null;

		// A frozen world should not burn a core spinning; there is nothing to
		// simulate until the test asks for a tick.
		if (running.Pacing.IsFrozen)
			return null;

		switch (running.Pacing.EffectiveMode) {
			case PacingMode.Unbounded:
				return 0;

			case PacingMode.Bounded:
				double due = running.Governor.TimeUntilDue().TotalMilliseconds;

				// Never longer than the loop already meant to sleep: this is a
				// speed-up, and must not be able to slow a run down.
				return (int)Math.Min(Math.Floor(due), requested);

			default:
				return null;
		}
	}

	private static void ForceTickWhileRunning(Action orig)
	{
		orig();

		hookCalls++;

		if (!loggedFirstHook) {
			loggedFirstHook = true;
			ModContent.GetInstance<TestariaSystem>()?.Mod.Logger.Info(
				$"Testaria: server tick hook fired (session={(session is null ? "null" : "present")}).");
		}

		if (session is not { IsFinished: false })
			return;

		Netplay.HasFullyConnectedClients = true;

		if (!loggedFirstForce) {
			loggedFirstForce = true;
			ModContent.GetInstance<TestariaSystem>()?.Mod.Logger.Info(
				$"Testaria: forcing server ticks after {hookCalls} idle iterations.");
		}
	}

	/// <summary>
	/// Skips the world update while a test has the world frozen, and counts
	/// the ticks it does let through.
	/// <para/>
	/// This is the same gate vanilla's own debug stepper uses: <c>DoUpdate</c>
	/// keeps running, only <c>DoUpdateInWorld</c> is skipped. So a frozen tick
	/// is a shape the game already produces, not a new one. In particular
	/// <c>PreUpdateEntities</c> still fires, which is what lets the watchdog
	/// below notice a test that froze the world and never thawed it.
	/// <para/>
	/// The test's own coroutine is stepped from <c>PostUpdateEverything</c>,
	/// inside the skipped call, so a frozen test does not advance either. That
	/// is deliberate: a test asked for the world to stop, and a test that kept
	/// running while the world did not would see a world that cannot change.
	/// </summary>
	private static void HoldTheWorldStill(Terraria.On_Main.orig_DoUpdateInWorld orig, Main self)
	{
		if (session is { IsFinished: false } running && running.Pacing.IsFrozen) {
			frozenFrames++;
			return;
		}

		frozenFrames = 0;

		if (session is { IsFinished: false } active) {
			active.Pacing.OnWorldTick();
			active.Governor.OnTick();
		}

		orig(self);
	}

	/// <summary>How many times the idle-loop hook has run. Diagnostic only.</summary>
	public static long HookCalls => hookCalls;

	/// <summary>
	/// Every gate between the server's loop and a running test, in one line.
	/// <para/>
	/// Assembled here rather than guessed at, because the gates are spread
	/// across Main.Update, Main.DoUpdate, and the server loop, and knowing
	/// which one is shut is the whole diagnosis.
	/// </summary>
	public static string Diagnose()
	{
		return string.Join("\n", [
			$"dedServ={Main.dedServ} netMode={Main.netMode} gameMenu={Main.gameMenu}",
			$"HasFullyConnectedClients={Netplay.HasFullyConnectedClients} (gates Game.Update in the server loop)",
			$"ShouldUpdateEntities={Main.instance.ShouldUpdateEntities()} generatingWorld={WorldGen.generatingWorld}",
			$"WorldUpdateStepper.Paused={Terraria.Testing.WorldUpdateStepper.Paused} (gates DoUpdateInWorld)",
			$"maxTilesX={Main.maxTilesX} worldSurface={(int)Main.worldSurface} rockLayer={(int)Main.rockLayer}",
			$"blankWorld={(BlankWorldSystem.Layout is null ? "no" : $"yes, {BlankWorldSystem.Layout.Reserved.Count} reserved area(s)")}",
			$"idle-hook calls={hookCalls}  PostUpdateEverything ticks={Ticks}",
			$"session={(session is null ? "none" : session.IsFinished ? "finished" : "running")}",
			$"pacing={DescribePacing()}",
		]);
	}

	/// <summary>
	/// The run's speed and freeze state in one phrase, so a paused run never
	/// looks like a hung one.
	/// </summary>
	public static string DescribePacing()
	{
		if (session is not { IsFinished: false } running)
			return "no run in progress";

		RunPacing pacing = running.Pacing;

		string speed = pacing.EffectiveMode switch {
			PacingMode.Unbounded => $"unbounded ({running.Governor.ActualTicksPerSecond:0} tps measured)",
			PacingMode.Bounded => $"bounded to {pacing.TargetTicksPerSecond:0} tps ({running.Governor.ActualTicksPerSecond:0} measured)",
			_ => "realtime, 60 tps",
		};

		if (pacing.CurrentTestWantsRealtime && pacing.Mode != PacingMode.Realtime)
			speed += " (this test opted out with [RealTime])";

		if (!pacing.IsFrozen)
			return speed;

		return pacing.PendingSteps is int owed && owed > 0
			? $"{speed}, stepping {owed} more tick(s)"
			: $"{speed}, world FROZEN for {frozenFrames} frame(s)";
	}

	/// <summary>
	/// How many times the game's update loop has reached us. The decisive
	/// diagnostic for whether a dedicated server is simulating at all.
	/// </summary>
	public static long Ticks { get; private set; }

	/// <summary>Every loaded mod's own assembly, which is where tests live.</summary>
	private static List<Assembly> LoadedModAssemblies()
	{
		List<Assembly> assemblies = [];

		foreach (Mod mod in ModLoader.Mods) {
			if (mod.Code is Assembly code)
				assemblies.Add(code);
		}

		return assemblies;
	}

	/// <summary>
	/// The names of every mod the game currently has loaded.
	/// </summary>
	public static IReadOnlyList<string> LoadedModNames()
		=> [.. ModLoader.Mods.Select(m => m.Name)];

	/// <summary>
	/// Why this run must not go ahead, or null if it may.
	/// <para/>
	/// Only one reason so far: a mod the harness said it installed is not
	/// loaded. That is worth refusing over rather than reporting afterwards,
	/// because every test in the suite is about to be skipped or absent and
	/// the resulting clean report is indistinguishable from a passing one.
	/// </summary>
	public static string? Preflight()
	{
		if (!Program.LaunchParameters.TryGetValue(RequiredMods.Flag, out string? value))
			return null;

		IReadOnlyList<string> loaded = LoadedModNames();
		IReadOnlyList<string> missing = RequiredMods.Missing(RequiredMods.Parse(value), loaded);

		return missing.Count == 0 ? null : RequiredMods.Describe(missing, loaded);
	}

	/// <summary>
	/// Writes a report saying the run was refused, so that a harness reading
	/// the report reaches the same verdict a person reading the console does.
	/// <para/>
	/// A report rather than silence: the harness waits for one, and a run that
	/// merely never produced it would time out, which is a far worse signal
	/// than a named error. Recorded as an error rather than a failure because
	/// nothing was tested.
	/// </summary>
	/// <returns>The written path, or null if it could not be written.</returns>
	public static string? RefuseRun(string runName, string reason)
	{
		ArgumentException.ThrowIfNullOrEmpty(runName);
		ArgumentException.ThrowIfNullOrEmpty(reason);

		TestRunResult result = new() {
			Name = runName,
			Suites = [
				new TestSuiteResult {
					Name = "Testaria.Preflight",
					Results = [
						new TestResult {
							ClassName = "Testaria.Preflight",
							Name = "The run was refused before it started",
							Outcome = TestOutcome.Errored,
							Message = reason,
						},
					],
				},
			],
		};

		try {
			string path = ResultsLocation.ForRun(Main.SavePath, runName);

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JUnitXmlWriter.ToXml(result));

			return path;
		}
		catch {
			return null;
		}
	}

	/// <summary>
	/// Lists the discovered tests without running any of them, so a harness
	/// can plan. Chiefly: which tests asked for a fresh world, each of which
	/// needs a process of its own to honestly get one.
	/// </summary>
	/// <returns>The written catalogue path and a one-line summary.</returns>
	public static (string? Path, string Summary) Catalog(TestFilter? filter)
	{
		filter ??= TestFilter.All;

		List<Type> types = [];
		foreach (Assembly assembly in LoadedModAssemblies())
			types.AddRange(AssemblyManager.GetLoadableTypes(assembly));

		List<TestCase> tests = [.. TestDiscovery.Discover(types).Tests.Where(filter.Matches)];

		// The catalogue is what a harness reads to decide which tests need a
		// process of their own, so it has to agree with the run about the
		// isolation switch rather than reporting only the attributes.
		bool isolate = Program.LaunchParameters.ContainsKey(TestSession.IsolateMutatorsFlag);

		try {
			string path = Path.Combine(ResultsLocation.Directory(Main.SavePath), "tests.tsv");

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, TestCatalog.ToTsv(tests, isolate));

			return (path, TestCatalog.Summarize(tests, isolate));
		}
		catch (Exception ex) {
			return (null, $"{TestCatalog.Summarize(tests, isolate)} (could not write the catalogue: {ex.GetType().Name}: {ex.Message})");
		}
	}

	/// <summary>
	/// Watches the running test's box before the game updates its entities.
	/// <para/>
	/// Deliberately earlier in the tick than the runner. Anything transient,
	/// including an intruder about to be despawned for being far from a
	/// player, exists only in this window.
	/// </summary>
	public override void PreUpdateEntities()
	{
		if (session is not { IsFinished: false } running)
			return;

		// Runs even while frozen, because the freeze gate is downstream of
		// this hook. That makes it the only place a forgotten pause can be
		// caught, since nothing else in the run is being stepped.
		if (running.Pacing.IsFrozen && frozenFrames > FrozenFrameLimit) {
			frozenFrames = 0;
			running.Pacing.Resume();

			if (running.CurrentContext is TestContext frozen) {
				frozen.AddNote(
					$"The world was left frozen for {FrozenFrameLimit} frames and has been thawed by the harness. " +
					"A test that pauses must step or resume; this one did neither.");
			}

			Mod.Logger.Warn("Testaria: a test left the world frozen and was thawed after " +
				$"{FrozenFrameLimit} frames. Its tick budget applies again from here.");
		}

		if (running.CurrentContext is TestContext context)
			context.Watch();
	}

	/// <inheritdoc />
	public override void PostUpdateEverything()
	{
		Ticks++;

		if (session is null || session.IsFinished)
			return;

		// Hold the gate open from inside the update itself.
		//
		// The server loop only calls Netplay.UpdateInMainThread on the branch
		// it takes when no clients are connected, and branches past it when it
		// does update. So once the idle hook opens the gate, the idle hook is
		// never reached again, and anything inside Update that clears the flag
		// would stop the world after exactly one tick. Measured: ticks stuck
		// at 1 while the idle hook kept firing.
		if (Main.dedServ)
			Netplay.HasFullyConnectedClients = true;

		if (!session.Step())
			Report(session);
	}

	/// <summary>
	/// Raises the flag that says a load pass has finished.
	/// <para/>
	/// Here rather than in <see cref="Load"/> because this is the first point
	/// at which the claim is true: content is registered, modded ids are
	/// assigned, <c>ContentSamples</c> is populated, and localization is in.
	/// Raising it earlier would make <see cref="GameState.Require"/> vouch for
	/// a game that is still assembling itself, which is the same silent lie
	/// the boundary exists to prevent.
	/// </summary>
	public override void PostSetupContent() => GameState.MarkLoaded();

	/// <inheritdoc />
	public override void Unload()
	{
		// Before anything else: from here on there is no game to speak of, and
		// a flag left raised across a reload would have the guard vouching for
		// a load context that is being torn down.
		GameState.MarkUnloaded();

		// Static state that outlives a reload keeps the old assembly alive.
		// The detour holds a delegate into this assembly, so it has to go too.
		Terraria.On_Main.DoUpdateInWorld -= HoldTheWorldStill;
		session = null;
		frozenFrames = 0;
		TestOwnership.Clear();

		// Each registered client query is a delegate over a test mod's
		// assembly. Keeping one across a reload would hold that assembly alive
		// and answer the next run with the previous build's code.
		ClientQuery.Clear();
		ClientLink.Reset();

		// Both sweep registries are filled by other mods' suites during their
		// load. SweepExemptions has always said in its own documentation that
		// it is cleared here and was not: the call was missing, so one run's
		// declarations carried into the next, where the mod that made them may
		// not even be loaded. SweepDeclarations matters more again, because it
		// holds a reference to an array inside a mod's assembly and would keep
		// that assembly alive across a reload.
		SweepExemptions.Clear();
		SweepDeclarations.Clear();
	}

	private void Report(TestSession finished)
	{
		Mod.Logger.Info($"Testaria run '{finished.RunName}': {finished.Summarize()}");

		if (finished.MetricsPath is string metrics)
			Mod.Logger.Info($"Testaria arena measurements written to {metrics}");

		if (finished.ResultsPath is string path)
			Mod.Logger.Info($"Testaria results written to {path}");

		if (finished.ResultsError is string error)
			Mod.Logger.Warn($"Testaria could not write results: {error}");
	}
}
