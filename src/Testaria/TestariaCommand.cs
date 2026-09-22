using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// Starts a test run.
/// <para/>
/// <see cref="CommandType.Console"/> is the one that matters: it is how a
/// headless <c>-server</c> process is driven from stdin, which is the whole
/// CI story. Chat is offered too, for running a suite from inside a session
/// while developing.
/// </summary>
public sealed class TestariaCommand : ModCommand
{
	/// <inheritdoc />
	public override string Command => "testaria";

	/// <inheritdoc />
	public override CommandType Type => CommandType.Console | CommandType.Chat;

	/// <summary>
	/// Arguments keep their case. ModCommand lowercases them by default, which
	/// silently turned a run named TestariaSelfTest into testariaselftest and
	/// so into a results file a case-sensitive filesystem could not be found by.
	/// </summary>
	public override bool IsCaseSensitive => true;

	/// <inheritdoc />
	public override string Usage => "/testaria run [name]";

	/// <inheritdoc />
	public override string Description => "Runs the discovered Testaria tests and writes a JUnit report.";

	/// <inheritdoc />
	public override void Action(CommandCaller caller, string input, string[] args)
	{
		if (args.Length == 0 || !args[0].Equals("run", StringComparison.OrdinalIgnoreCase)) {
			caller.Reply(Usage, Color.Yellow);
			return;
		}

		if (TestariaSystem.IsRunning) {
			caller.Reply("A Testaria run is already in progress.", Color.Yellow);
			return;
		}

		string runName = args.Length > 1 ? args[1] : "Testaria";
		TestSession? session = TestariaSystem.Start(runName);

		if (session is null) {
			caller.Reply("A Testaria run is already in progress.", Color.Yellow);
			return;
		}

		if (session.Discovered == 0) {
			// Saying nothing was found beats a silent green run, which reads
			// identically to a passing suite.
			caller.Reply("Testaria found no tests. Is the test mod enabled?", Color.Yellow);
			return;
		}

		caller.Reply($"Testaria running {session.Discovered} test(s) as '{runName}'. Results land under {ResultsLocation.Directory(Main.SavePath)}.", Color.White);
	}
}
