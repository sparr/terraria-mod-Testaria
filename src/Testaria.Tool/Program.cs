using System.Diagnostics;

namespace Testaria.Tool;

/// <summary>
/// The <c>testaria</c> command.
/// <para/>
/// What the repository's shell scripts do, in a form that runs anywhere the
/// .NET SDK does and that someone who is not working on Testaria itself can
/// install with <c>dotnet tool install</c>.
/// </summary>
public static class Program
{
	/// <summary>Exit code for a run whose tests all passed or were skipped.</summary>
	public const int Success = 0;

	/// <summary>Exit code for a run that found a failure, an error, or a blocked test.</summary>
	public const int TestsFailed = 1;

	/// <summary>Exit code for a run that could not happen at all.</summary>
	public const int HarnessFailed = 2;

	/// <summary>Entry point.</summary>
	public static int Main(string[] args)
	{
		ParseResult parsed = CommandLine.Parse(args);

		if (parsed.WantsHelp) {
			Console.WriteLine(CommandLine.Usage);

			return Success;
		}

		if (parsed.Error is string error) {
			Console.Error.WriteLine(error);
			Console.Error.WriteLine();
			Console.Error.WriteLine(CommandLine.Usage);

			return HarnessFailed;
		}

		try {
			return Execute(parsed.Options!);
		}
		catch (HarnessException harness) {
			Console.Error.WriteLine(harness.Message);

			return HarnessFailed;
		}
		catch (FileNotFoundException missing) {
			Console.Error.WriteLine(missing.Message);

			return HarnessFailed;
		}
	}

	private static int Execute(RunOptions options)
	{
		TextWriter progress = options.Quiet ? TextWriter.Null : Console.Out;

		string tml = Installation.FindTml(options.TmlPath)
			?? throw new HarnessException(
				"No tModLoader install found. Pass --tml, or set " + Installation.PathVariable + ". Looked in:\n"
				+ string.Join("\n", Installation.Candidates(options.TmlPath).Select(path => "  " + path)));

		foreach (string project in options.Projects)
			Build(project, progress);

		string? modsDirectory = Installation.FindModsDirectory(options.ModsDirectory);

		using ScratchSave scratch = ScratchSave.Create(options.KeepScratch);

		IReadOnlyList<string> enabled = scratch.Install(options.Mods, modsDirectory);

		// Before anything starts, so the game reads them on its first load
		// pass rather than being reconfigured halfway through a run.
		ModConfigs.InstallInto(scratch.Root, options.Configs);

		progress.WriteLine($"tml:      {tml}");
		progress.WriteLine($"mods:     {string.Join(" ", enabled)}");
		progress.WriteLine($"scratch:  {scratch.Root}");

		(string command, string resultsPath) = ServerArguments.Command(options, scratch);

		// A display only when there are clients to draw into it: a server-only
		// run needs none, and starting Xvfb for one would be a dependency
		// nobody asked for.
		using VirtualDisplay? display = options.Clients > 0 ? VirtualDisplay.Provide() : null;

		List<string> clientSaves = [];

		for (int i = 0; i < options.Clients; i++)
			clientSaves.Add(scratch.PrepareClient(i, enabled, tml, options.Configs));

		if (display is not null)
			progress.WriteLine($"display:  {display.Name}");

		var harness = new ServerHarness(tml, scratch, progress, options.Verbose);

		try {
			harness
				.Run(
					ServerArguments.For(options, scratch, enabled),
					command,
					resultsPath,
					TimeSpan.FromSeconds(options.TimeoutSeconds),
					TimeSpan.FromSeconds(options.JoinTimeoutSeconds),
					clientSaves,
					display?.Name);
		}
		finally {
			// Before the scratch directory goes, and on the failing path above
			// all: the logs are the only account of what the game did, and a
			// run that dies with the clients unaccounted for is exactly the
			// run whose client log somebody needs. Measured: a tier 3 timeout
			// in CI named a client log that had already been deleted by the
			// time anybody read the report.
			if (options.ResultsOut is string reportPath)
				CopyLogsBeside(reportPath, scratch, clientSaves, progress);
		}

		// Every mod that was asked for has to have loaded. A mod that failed
		// to load is disabled by tModLoader and the run carries on without it,
		// reporting on a game that is not the one anybody asked for.
		string[] missing = [..enabled.Where(mod => !harness.LoadedMods.Contains(mod, StringComparer.OrdinalIgnoreCase))];

		if (harness.LoadedMods.Count > 0 && missing.Length > 0) {
			throw new HarnessException(
				$"These mods were enabled but did not load: {string.Join(", ", missing)}. "
				+ $"The game loaded: {string.Join(", ", harness.LoadedMods)}. "
				+ $"Its log is at {scratch.LogPath}, and the reason is usually in the first few lines.");
		}

		if (options.ResultsOut is string destination) {
			Copy(resultsPath, destination, progress);

			// Beside the report, under the same name, so a CI job collecting
			// one collects the other.
			if (options.Measure && File.Exists(ServerArguments.MetricsPath(options, scratch))) {
				Copy(
					ServerArguments.MetricsPath(options, scratch),
					Path.ChangeExtension(destination, null) + "-arena.tsv",
					progress);
			}
		}

		if (options.KeepScratch)
			progress.WriteLine($"scratch kept at {scratch.Root}");

		if (options.List) {
			// A catalogue is not a verdict: nothing ran, so there is nothing
			// to pass or fail.
			Console.WriteLine(File.ReadAllText(resultsPath));

			return Success;
		}

		return Report(RunReport.Read(resultsPath), options.Require);
	}

	private static int Report(RunReport report, int require)
	{
		Console.WriteLine(report.Summarize());

		foreach (Problem problem in report.Problems) {
			Console.WriteLine($"  {problem.Kind.ToUpperInvariant()} {problem.ClassName}.{problem.Name}:");

			// Every line of the message. Assertion messages put the expected
			// and actual values on later lines, so printing only the first
			// reliably prints the least useful sentence in the report.
			foreach (string line in problem.Message.Split('\n'))
				Console.WriteLine("      " + line.TrimEnd());
		}

		if (require > 0 && report.Ran < require) {
			// A failure rather than a warning. The whole point of the demand is
			// that the run looked fine, and a warning in a log nobody reads
			// would leave it looking fine.
			Console.Error.WriteLine(
				$"Only {report.Ran} test(s) ran, and --require asked for {require}. "
				+ "A suite that skips everything reports success while proving nothing: check that the mod "
				+ "under test actually loaded.");

			return TestsFailed;
		}

		return report.IsSuccess ? Success : TestsFailed;
	}

	private static void Build(string project, TextWriter progress)
	{
		progress.WriteLine($"building: {project}");

		var build = Process.Start(new ProcessStartInfo("dotnet") {
			ArgumentList = { "build", project, "--nologo", "-v", "q", "-clp:ErrorsOnly" },
			UseShellExecute = false,
		}) ?? throw new HarnessException($"Could not start dotnet to build '{project}'.");

		build.WaitForExit();

		// Fatal rather than a warning. A run that carries on with the last
		// build's .tmod reports on code nobody has read, and looks exactly
		// like a run of the code they just wrote.
		if (build.ExitCode != 0)
			throw new HarnessException($"Build failed: {project}");
	}

	/// <summary>
	/// Puts the server and client logs beside the report, named after it, so
	/// that whatever collects one collects the others.
	/// </summary>
	private static void CopyLogsBeside(
		string reportPath,
		ScratchSave scratch,
		IReadOnlyList<string> clientSaves,
		TextWriter progress)
	{
		string stem = Path.ChangeExtension(reportPath, null);

		TryCopy(scratch.LogPath, stem + "-server.log");

		for (int i = 0; i < clientSaves.Count; i++)
			TryCopy(Path.Combine(scratch.ClientDirectory(i), "client.log"), stem + $"-client-{i}.log");

		void TryCopy(string from, string to)
		{
			// Never throws: this runs while an exception may already be on its
			// way out, and a missing log must not replace the failure that
			// explains the run with one about copying a file.
			try {
				if (File.Exists(from))
					File.Copy(from, to, overwrite: true);
			}
			catch (Exception) {
				progress.WriteLine($"could not keep {from}");
			}
		}
	}

	private static void Copy(string from, string to, TextWriter progress)
	{
		if (Path.GetDirectoryName(Path.GetFullPath(to)) is string directory)
			Directory.CreateDirectory(directory);

		File.Copy(from, to, overwrite: true);
		progress.WriteLine($"results:  {to}");
	}
}
