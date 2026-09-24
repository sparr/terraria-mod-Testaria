namespace Testaria.Tool;

/// <summary>
/// The launch parameters one run needs.
/// <para/>
/// Its own type so the command line a run would use can be asserted on without
/// starting a game. The flags here are the contract between this tool and the
/// mod, and getting one wrong is silent: a missing <c>-testariafreshworld</c>
/// turns every <c>[FreshWorld]</c> test into a skip, and the run still reports
/// green.
/// </summary>
public static class ServerArguments
{
	/// <summary>Builds the server's command line for a run.</summary>
	/// <param name="options">What the run was asked to do.</param>
	/// <param name="scratch">The throwaway save directory it runs in.</param>
	/// <param name="enabled">
	/// The mods actually installed and enabled, which the run is told to
	/// insist on. Without this a mod that fails its load pass is disabled by
	/// the game, the suite aimed at it finds nothing, and the report comes
	/// back clean; with it, the run refuses and names the mod.
	/// </param>
	public static IReadOnlyList<string> For(RunOptions options, ScratchSave scratch, IEnumerable<string>? enabled = null)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(scratch);

		List<string> arguments = [
			"-server",
			// Both SavePath and SavePathShared, so nothing the run does
			// reaches a real installation.
			"-tmlsavedirectory", scratch.Root,
			"-nosteam",
			// Generate the world rather than waiting at a menu for a choice
			// nobody is there to make. 1 is a small world, the fastest to
			// generate and large enough for any arena.
			"-autocreate", "1",
			"-world", scratch.WorldPath,
			"-worldname", "testaria",
			"-seed", options.WorldSeed.ToString(),
			"-players", "1",
			"-port", "7777",
			"-password", string.Empty,
		];

		if (options.BlankWorld)
			arguments.Add("-testariablank");

		if (options.Measure)
			arguments.Add("-testariameasure");

		arguments.AddRange(options.ExtraArgs);

		if (options.FreshWorld)
			arguments.Add("-testariafreshworld");

		if (options.Speed is string speed) {
			arguments.Add("-testariaspeed");
			arguments.Add(speed);
		}

		if (options.RunSeed is int seed) {
			arguments.Add("-testariaseed");
			arguments.Add(seed.ToString());
		}

		if (enabled is not null) {
			string names = string.Join(",", enabled);

			if (names.Length > 0) {
				arguments.Add(RequiredMods.Flag);
				arguments.Add(names);
			}
		}

		return arguments;
	}

	/// <summary>The console command that starts the run, and the file it will write.</summary>
	public static (string Command, string ResultsPath) Command(RunOptions options, ScratchSave scratch)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(scratch);

		string filter = options.Filter is null ? string.Empty : " " + options.Filter;

		return options.List
			? ($"testaria list{filter}", ResultsLocation.ForRun(scratch.Root, "tests", ".tsv"))
			: ($"testaria run {options.Name}{filter}", ResultsLocation.ForRun(scratch.Root, options.Name));
	}

	/// <summary>Where a measured run leaves its table of box costs.</summary>
	public static string MetricsPath(RunOptions options, ScratchSave scratch)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(scratch);

		return ResultsLocation.ForRun(scratch.Root, options.Name + "-arena", ".tsv");
	}
}
