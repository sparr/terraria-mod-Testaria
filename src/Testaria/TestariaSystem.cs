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
	/// reached from the public <c>Netplay.UpdateInMainThread</c>, which the
	/// loop calls every iteration. Setting the flag after that runs is
	/// therefore the one point where the value survives to the next
	/// iteration's check.
	/// <para/>
	/// Scoped as tightly as possible: dedicated server only, and only while a
	/// run is actually in progress, so a normal server is never affected.
	/// </summary>
	public override void Load()
	{
		if (!Main.dedServ)
			return;

		try {
			MethodInfo update = typeof(Netplay).GetMethod(
				nameof(Netplay.UpdateInMainThread),
				BindingFlags.Public | BindingFlags.Static)
				?? throw new InvalidOperationException("Netplay.UpdateInMainThread not found.");

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

	/// <inheritdoc />
	public override void PostUpdateEverything()
	{
		if (session is null || session.IsFinished)
			return;

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
