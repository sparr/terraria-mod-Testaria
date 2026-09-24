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
	public override string Usage =>
		"/testaria run [name] [filter] | /testaria list [filter] | /testaria status | " +
		"/testaria speed realtime|max|<ticks per second> | /testaria pause | /testaria step [n] | /testaria resume";

	/// <inheritdoc />
	public override string Description => "Runs the discovered Testaria tests and writes a JUnit report.";

	/// <summary>
	/// Parses a filter, reporting a bad pattern to whoever asked rather than
	/// letting it surface as a command failure. A filter that cannot be
	/// understood must not quietly become one that matches nothing.
	/// </summary>
	private static bool TryParseFilter(CommandCaller caller, string? pattern, out TestFilter filter)
	{
		try {
			filter = TestFilter.Parse(pattern);
			return true;
		}
		catch (ArgumentException ex) {
			caller.Reply(ex.Message, Color.Yellow);
			filter = TestFilter.All;

			return false;
		}
	}

	/// <inheritdoc />
	public override void Action(CommandCaller caller, string input, string[] args)
	{
		if (args.Length > 0 && args[0].Equals("list", StringComparison.OrdinalIgnoreCase)) {
			if (!TryParseFilter(caller, args.Length > 1 ? args[1] : null, out TestFilter listFilter))
				return;

			(string? path, string summary) = TestariaSystem.Catalog(listFilter);

			caller.Reply($"Testaria discovered {summary}.", Color.White);

			if (path is not null)
				caller.Reply($"Catalogue written to {path}.", Color.White);

			return;
		}

		if (args.Length > 0 && args[0].Equals("speed", StringComparison.OrdinalIgnoreCase)) {
			Speed(caller, args.Length > 1 ? args[1] : null);
			return;
		}

		if (args.Length > 0 && IsPacingVerb(args[0])) {
			Pacing(caller, args[0], args.Length > 1 ? args[1] : null);
			return;
		}

		if (args.Length > 0 && args[0].Equals("status", StringComparison.OrdinalIgnoreCase)) {
			foreach (string line in TestariaSystem.Diagnose().Split('\n'))
				caller.Reply(line, Color.White);

			return;
		}

		if (args.Length == 0 || !args[0].Equals("run", StringComparison.OrdinalIgnoreCase)) {
			caller.Reply(Usage, Color.Yellow);
			return;
		}

		if (TestariaSystem.IsRunning) {
			caller.Reply("A Testaria run is already in progress.", Color.Yellow);
			return;
		}

		string runName = args.Length > 1 ? args[1] : "Testaria";

		if (!TryParseFilter(caller, args.Length > 2 ? args[2] : null, out TestFilter filter))
			return;

		TestSession? session = TestariaSystem.Start(runName, filter);

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

		if (session.Selected == 0) {
			caller.Reply($"Testaria matched no tests against '{filter}' out of {session.Discovered} discovered.", Color.Yellow);
			return;
		}

		string scope = filter.IsNarrowing
			? $"{session.Selected} of {session.Discovered} test(s) matching '{filter}'"
			: $"{session.Discovered} test(s)";

		caller.Reply($"Testaria running {scope} as '{runName}'. Results land under {ResultsLocation.Directory(Main.SavePath)}.", Color.White);
	}

	private static bool IsPacingVerb(string verb)
		=> verb.Equals("pause", StringComparison.OrdinalIgnoreCase)
		|| verb.Equals("resume", StringComparison.OrdinalIgnoreCase)
		|| verb.Equals("step", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Sets how fast the run may simulate. Applies to the run as a whole;
	/// individual tests marked <c>[RealTime]</c> still run at 60 tps.
	/// </summary>
	private static void Speed(CommandCaller caller, string? value)
	{
		if (TestariaSystem.Current is not { IsFinished: false } run) {
			caller.Reply("No run is in progress, so there is no pacing to change.", Color.Yellow);
			return;
		}

		if (value is null) {
			caller.Reply(TestariaSystem.DescribePacing(), Color.White);
			return;
		}

		if (TestSession.ParseSpeed(value) is not (PacingMode mode, double rate)) {
			caller.Reply($"'{value}' is not a speed. Use realtime, max, or a positive number of ticks per second.", Color.Yellow);
			return;
		}

		run.SetSpeed(mode, rate);
		caller.Reply(TestariaSystem.DescribePacing(), Color.White);
	}

	/// <summary>
	/// Stops, steps, and restarts the world by hand, which is how a run is
	/// inspected while it is stuck on something.
	/// </summary>
	private static void Pacing(CommandCaller caller, string verb, string? argument)
	{
		if (TestariaSystem.Current is not { IsFinished: false } run) {
			caller.Reply("No run is in progress, so there is no world to hold still.", Color.Yellow);
			return;
		}

		if (verb.Equals("pause", StringComparison.OrdinalIgnoreCase)) {
			run.Pacing.Pause();
		}
		else if (verb.Equals("resume", StringComparison.OrdinalIgnoreCase)) {
			run.Pacing.Resume();
		}
		else {
			int ticks = 1;

			if (argument is not null && (!int.TryParse(argument, out ticks) || ticks < 1)) {
				caller.Reply($"'{argument}' is not a tick count. Step takes a positive number, or nothing for one tick.", Color.Yellow);
				return;
			}

			run.Pacing.GrantSteps(ticks);
		}

		caller.Reply(TestariaSystem.DescribePacing(), Color.White);
	}
}
