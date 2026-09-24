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

	/// <summary>
	/// What the server prints when a client is genuinely ready.
	/// <para/>
	/// Not "has joined", which is the server accepting a connection: the world
	/// the client asked for keeps arriving for some time after that, and
	/// measured, a client first saw its spawn block five ticks after a suite
	/// had started running against it. Testaria's own client says when it is
	/// in the world, which is the point at which its ground has arrived.
	/// </summary>
	private const string JoinedMarker = "is in the world and ready";

	private readonly StringBuilder log = new();
	private readonly List<Process> clients = [];

	// Where each client's output was written, so a client that dies can be
	// reported with the one file that says why.
	private readonly Dictionary<int, string> clientLogs = [];
	private Process? server;

	/// <summary>
	/// Runs one command inside a freshly started server and waits for the file
	/// it is expected to produce.
	/// </summary>
	/// <param name="arguments">Launch parameters for the server.</param>
	/// <param name="command">The console command to send once the world is up.</param>
	/// <param name="resultsPath">The file the command is expected to write.</param>
	/// <param name="timeout">How long the whole business may take.</param>
	/// <param name="clientSaves">Prepared save directories, one per client to start, or none.</param>
	/// <param name="display">Display for the clients to draw into, or null where they need none.</param>
	/// <exception cref="HarnessException">The server died, or nothing arrived in time.</exception>
	public void Run(
		IEnumerable<string> arguments,
		string command,
		string resultsPath,
		TimeSpan timeout,
		IReadOnlyList<string>? clientSaves = null,
		string? display = null)
	{
		ArgumentNullException.ThrowIfNull(arguments);

		var deadline = Stopwatch.StartNew();

		Start(arguments);

		// Everything from here is inside a finally, because every one of the
		// steps below can throw and none of them cleans up after itself. Left
		// as it was, any failure orphaned a server holding port 7777 and,
		// where tier 3 was involved, a client alongside it. Measured: a gate
		// that threw left both running, and the next three gates in the same
		// `run-all.sh` failed with "Address already in use", which points at
		// the gate that noticed rather than the gate that leaked.
		try {
			WaitFor(
				() => ReadyMarkers.Any(marker => Log.Contains(marker, StringComparison.Ordinal)),
				deadline,
				timeout,
				"the server to finish starting");

			progress.WriteLine($"world ready after {deadline.Elapsed.TotalSeconds:F0}s");

			if (clientSaves is { Count: > 0 })
				JoinClients(clientSaves, display, deadline, timeout);

			progress.WriteLine($"sending: {command}");

			server!.StandardInput.WriteLine(command);
			server.StandardInput.Flush();

			WaitFor(() => File.Exists(resultsPath), deadline, timeout, "the run to write its report");
		}
		finally {
			Stop();
		}
	}

	/// <summary>What the game printed about the mods it actually loaded.</summary>
	private const string LoadedMarker = "Testaria: mods loaded: ";

	/// <summary>
	/// The mods the game said it loaded, or empty when it has not said yet.
	/// </summary>
	public IReadOnlyList<string> LoadedMods
	{
		get {
			string log = Log;
			int at = log.LastIndexOf(LoadedMarker, StringComparison.Ordinal);

			if (at < 0)
				return [];

			int start = at + LoadedMarker.Length;
			int end = log.IndexOf('\n', start);
			string line = end < 0 ? log[start..] : log[start..end];

			return [..line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
		}
	}

	/// <summary>Everything the server has said so far.</summary>
	public string Log
	{
		get {
			lock (log)
				return log.ToString();
		}
	}

	/// <summary>
	/// Starts the client processes and waits until the server says they have
	/// all arrived.
	/// <para/>
	/// Waiting on the server's own "has joined" line rather than on the
	/// clients' logs, because that is the event the tests care about: a client
	/// process that is running but has not finished the handshake is no use to
	/// a netcode test, and the server is the side that knows the difference.
	/// </summary>
	private void JoinClients(IReadOnlyList<string> saves, string? display, Stopwatch deadline, TimeSpan timeout)
	{
		foreach (string save in saves) {
			var start = new ProcessStartInfo("dotnet") {
				WorkingDirectory = tmlPath,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};

			start.ArgumentList.Add(Path.Combine(tmlPath, "tModLoader.dll"));
			start.ArgumentList.Add("-tmlsavedirectory");
			start.ArgumentList.Add(save);
			start.ArgumentList.Add("-nosteam");
			// The mod on the client side reads this and drives the join, since
			// nothing on the command line reaches Main.AutoJoin.
			start.ArgumentList.Add("-testariajoin");
			start.ArgumentList.Add("127.0.0.1:7777");

			// A client needs a real framebuffer: measured, one started with
			// SDL_VIDEODRIVER=dummy exits within five seconds. The server is
			// the opposite case and uses the dummy driver happily.
			if (!string.IsNullOrEmpty(display))
				start.Environment["DISPLAY"] = display;

			start.Environment["SDL_AUDIODRIVER"] = "dummy";

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				string natives = Path.Combine(tmlPath, "Libraries", "Native", "Linux");
				string existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty;

				start.Environment["LD_LIBRARY_PATH"] = existing.Length > 0 ? natives + ":" + existing : natives;
			}

			Process client = Process.Start(start)
				?? throw new HarnessException("Could not start a client process.");

			string logPath = Path.Combine(save, "client.log");
			var clientLog = new StringBuilder();

			void Capture(string? line)
			{
				if (line is null)
					return;

				lock (clientLog)
					clientLog.AppendLine(line);

				try {
					File.WriteAllText(logPath, clientLog.ToString());
				}
				catch (IOException) {
					// A diagnostic, not the run.
				}
			}

			client.OutputDataReceived += (_, e) => Capture(e.Data);
			client.ErrorDataReceived += (_, e) => Capture(e.Data);
			client.BeginOutputReadLine();
			client.BeginErrorReadLine();

			clients.Add(client);
			clientLogs[client.Id] = logPath;
			progress.WriteLine($"client:   pid {client.Id}, log {logPath}");
		}

		WaitFor(
			() => Occurrences(Log, JoinedMarker) >= saves.Count,
			deadline,
			timeout,
			$"{saves.Count} client(s) to join");

		progress.WriteLine($"clients joined after {deadline.Elapsed.TotalSeconds:F0}s");
	}

	private static int Occurrences(string text, string marker)
	{
		int count = 0;

		for (int i = text.IndexOf(marker, StringComparison.Ordinal); i >= 0; i = text.IndexOf(marker, i + marker.Length, StringComparison.Ordinal))
			count++;

		return count;
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

	private string ClientLogPath(Process client)
		=> clientLogs.TryGetValue(client.Id, out string? path) ? path : "the scratch directory";

	private void WaitFor(Func<bool> done, Stopwatch elapsed, TimeSpan timeout, string what)
	{
		while (!done()) {
			if (server!.HasExited) {
				WriteLog();
				throw new HarnessException($"The server exited while waiting for {what}. Its log is at {scratch.LogPath}.{Tail()}");
			}

			// A client is a whole game process, and unlike the server it
			// draws, so it meets failures the server never does: measured, a
			// missing shader asset took one down at the first frame that
			// wanted it, long after it had joined. Without this the run waits
			// out its whole timeout and blames the wait.
			if (clients.FirstOrDefault(client => client.HasExited) is Process dead) {
				WriteLog();
				Kill();

				throw new HarnessException(
					$"A client exited while waiting for {what}. Tier 3 tests cannot be answered without it. "
					+ $"Its log is at {ClientLogPath(dead)}.");
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
		// A server that has already gone still leaves its clients behind, and
		// that is the case where a client is most likely to be orphaned: the
		// server died, so nothing asked the clients to leave. So this falls
		// through to Kill rather than returning.
		if (server is null || server.HasExited) {
			Kill();
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

		if (!server.WaitForExit(TimeSpan.FromSeconds(30)))
			progress.WriteLine("the server did not exit when asked, killing it");

		// Either way, and clients always: they have no console to be asked
		// politely through, and this tool is often the last thing a CI job
		// does, where an orphaned game process would hold the job open.
		Kill();

		WriteLog();
	}

	private void Kill()
	{
		foreach (Process client in clients) {
			try {
				if (!client.HasExited)
					client.Kill(entireProcessTree: true);
			}
			catch (InvalidOperationException) {
				// Exited between the check and the kill, which is fine.
			}
		}

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
