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

		progress.WriteLine($"tml:      {tml}");
		progress.WriteLine($"mods:     {string.Join(" ", enabled)}");
		progress.WriteLine($"scratch:  {scratch.Root}");

		(string command, string resultsPath) = ServerArguments.Command(options, scratch);

		new ServerHarness(tml, scratch, progress, options.Verbose)
			.Run(ServerArguments.For(options, scratch), command, resultsPath, TimeSpan.FromSeconds(options.TimeoutSeconds));

		if (options.ResultsOut is string destination)
			Copy(resultsPath, destination, progress);

		if (options.KeepScratch)
			progress.WriteLine($"scratch kept at {scratch.Root}");

		if (options.List) {
			// A catalogue is not a verdict: nothing ran, so there is nothing
			// to pass or fail.
			Console.WriteLine(File.ReadAllText(resultsPath));

			return Success;
		}

		return Report(RunReport.Read(resultsPath));
	}

	private static int Report(RunReport report)
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

	private static void Copy(string from, string to, TextWriter progress)
	{
		if (Path.GetDirectoryName(Path.GetFullPath(to)) is string directory)
			Directory.CreateDirectory(directory);

		File.Copy(from, to, overwrite: true);
		progress.WriteLine($"results:  {to}");
	}
}
