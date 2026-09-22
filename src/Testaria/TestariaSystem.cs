using System.Reflection;
using Terraria;
using Terraria.ModLoader;

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
	public static TestSession? Start(string runName)
	{
		if (IsRunning)
			return null;

		List<Assembly> assemblies = [];
		foreach (Mod mod in ModLoader.Mods) {
			if (mod.Code is Assembly code)
				assemblies.Add(code);
		}

		// Without a world, Tier 2 and above cannot be honoured, and the runner
		// reports them as skipped rather than running them somewhere undefined.
		TestTier maxTier = Main.maxTilesX > 0 ? TestTier.World : TestTier.Loaded;

		session = TestSession.Create(assemblies, runName, maxTier);

		return session;
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
		if (!Main.dedServ)
			return;

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
		]);
	}

	/// <summary>
	/// How many times the game's update loop has reached us. The decisive
	/// diagnostic for whether a dedicated server is simulating at all.
	/// </summary>
	public static long Ticks { get; private set; }

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

	/// <inheritdoc />
	public override void Unload()
	{
		// Static state that outlives a reload keeps the old assembly alive.
		session = null;
	}

	private void Report(TestSession finished)
	{
		Mod.Logger.Info($"Testaria run '{finished.RunName}': {finished.Summarize()}");

		if (finished.ResultsPath is string path)
			Mod.Logger.Info($"Testaria results written to {path}");

		if (finished.ResultsError is string error)
			Mod.Logger.Warn($"Testaria could not write results: {error}");
	}
}
