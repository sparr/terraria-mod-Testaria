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
