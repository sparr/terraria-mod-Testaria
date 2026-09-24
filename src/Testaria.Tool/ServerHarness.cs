using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Testaria.Tool;

/// <summary>
/// Starts a headless tModLoader server, drives one run through its console,
/// and stops it again.
/// <para/>
/// The server's console is the whole interface: <c>-server</c> reads commands
/// from standard input, which is how a harness asks for a run without any UI.
/// Standard input is a pipe rather than a FIFO, which is what lets this work
/// on Windows as well as everywhere else.
/// </summary>
public sealed class ServerHarness(string tmlPath, ScratchSave scratch, TextWriter progress, bool verbose)
{
	/// <summary>What the server prints once it is ready for commands.</summary>
	private static readonly string[] ReadyMarkers = ["Server started", "Listening on port"];

	private readonly StringBuilder log = new();
	private Process? server;

	/// <summary>
	/// Runs one command inside a freshly started server and waits for the file
	/// it is expected to produce.
	/// </summary>
	/// <param name="arguments">Launch parameters for the server.</param>
	/// <param name="command">The console command to send once the world is up.</param>
	/// <param name="resultsPath">The file the command is expected to write.</param>
	/// <param name="timeout">How long the whole business may take.</param>
	/// <exception cref="HarnessException">The server died, or nothing arrived in time.</exception>
	public void Run(IEnumerable<string> arguments, string command, string resultsPath, TimeSpan timeout)
	{
		ArgumentNullException.ThrowIfNull(arguments);

		var deadline = Stopwatch.StartNew();

		Start(arguments);

		WaitFor(
			() => ReadyMarkers.Any(marker => Log.Contains(marker, StringComparison.Ordinal)),
			deadline,
			timeout,
			"the server to finish starting");

		progress.WriteLine($"world ready after {deadline.Elapsed.TotalSeconds:F0}s");
		progress.WriteLine($"sending: {command}");

		server!.StandardInput.WriteLine(command);
		server.StandardInput.Flush();

		WaitFor(() => File.Exists(resultsPath), deadline, timeout, "the run to write its report");

		Stop();
	}

	/// <summary>Everything the server has said so far.</summary>
	public string Log
	{
		get {
			lock (log)
				return log.ToString();
		}
	}

	private void Start(IEnumerable<string> arguments)
	{
		var start = new ProcessStartInfo("dotnet") {
			WorkingDirectory = tmlPath,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		start.ArgumentList.Add(Path.Combine(tmlPath, "tModLoader.dll"));

		foreach (string argument in arguments)
			start.ArgumentList.Add(argument);

		// Headless means headless. FNA initialises SDL early enough that a
		// server on a machine with a desktop would otherwise be able to touch
		// it, and a test run has no business opening a window or making a
		// noise on somebody's computer.
		start.Environment["SDL_VIDEODRIVER"] = "dummy";
		start.Environment["SDL_AUDIODRIVER"] = "dummy";

		// The game ships its native libraries in a folder the dynamic loader
		// does not know about, and the launcher scripts set this for the same
		// reason.
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
			string natives = Path.Combine(tmlPath, "Libraries", "Native", "Linux");
			string existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty;

			start.Environment["LD_LIBRARY_PATH"] = existing.Length > 0 ? natives + ":" + existing : natives;
		}

		server = Process.Start(start)
			?? throw new HarnessException("Could not start dotnet to run tModLoader.");

		server.OutputDataReceived += (_, e) => Capture(e.Data);
		server.ErrorDataReceived += (_, e) => Capture(e.Data);
		server.BeginOutputReadLine();
		server.BeginErrorReadLine();

		progress.WriteLine($"server:   pid {server.Id}, log {scratch.LogPath}");
	}

	private void Capture(string? line)
	{
		if (line is null)
			return;

		lock (log)
			log.AppendLine(line);

		if (verbose)
			progress.WriteLine("  | " + line);
	}

	private void WaitFor(Func<bool> done, Stopwatch elapsed, TimeSpan timeout, string what)
	{
		while (!done()) {
			if (server!.HasExited) {
				WriteLog();
				throw new HarnessException($"The server exited while waiting for {what}. Its log is at {scratch.LogPath}.{Tail()}");
			}

			if (elapsed.Elapsed > timeout) {
				WriteLog();
				Kill();
				throw new HarnessException($"Timed out after {timeout.TotalSeconds:F0}s waiting for {what}. Its log is at {scratch.LogPath}.{Tail()}");
			}

			Thread.Sleep(250);
		}
	}

	private void Stop()
	{
		if (server is null || server.HasExited) {
			WriteLog();
			return;
		}

		try {
			server.StandardInput.WriteLine("exit");
			server.StandardInput.Flush();
		}
		catch (IOException) {
			// Already on its way out, which is the outcome this was asking for.
		}

		// A server that will not leave after being asked is killed rather than
		// left running: this tool is often the last thing a CI job does, and an
		// orphaned game process would hold the job open.
		if (!server.WaitForExit(TimeSpan.FromSeconds(30)))
			Kill();

		WriteLog();
	}

	private void Kill()
	{
		try {
			server?.Kill(entireProcessTree: true);
		}
		catch (InvalidOperationException) {
			// Exited between the check and the kill, which is fine.
		}
	}

	private void WriteLog()
	{
		try {
			File.WriteAllText(scratch.LogPath, Log);
		}
		catch (IOException) {
			// The log is a diagnostic. Failing to write it must not replace
			// the real error with a filesystem one.
		}
	}

	// The last few lines inline, because the common failure is a mod that will
	// not load, and making someone open a file to see that is unkind.
	private string Tail(int lines = 12)
	{
		string[] all = Log.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		return all.Length == 0
			? string.Empty
			: "\n" + string.Join("\n", all.TakeLast(lines).Select(line => "  | " + line.TrimEnd()));
	}
}

/// <summary>
/// Something went wrong with the harness rather than with the tests.
/// <para/>
/// Its own type because the two mean different things to whoever reads the
/// exit code: a failing test is a result, and a harness that could not start
/// the game is not.
/// </summary>
public sealed class HarnessException : Exception
{
	/// <summary>Reports a harness problem.</summary>
	public HarnessException(string message) : base(message) { }

	/// <summary>Reports a harness problem.</summary>
	public HarnessException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Reports a harness problem.</summary>
	public HarnessException() : base("The harness could not run the tests.") { }
}
