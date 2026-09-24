namespace Testaria.Tool;

/// <summary>What a single invocation was asked to do.</summary>
public sealed record RunOptions
{
	/// <summary>Mods to install and enable, by bare name or by path to a <c>.tmod</c>.</summary>
	public IReadOnlyList<string> Mods { get; init; } = [];

	/// <summary>Mod projects to build before the run.</summary>
	public IReadOnlyList<string> Projects { get; init; } = [];

	/// <summary>Name of the run, which is also the name of its report.</summary>
	public string Name { get; init; } = "Testaria";

	/// <summary>Regular expression narrowing the run, or null for everything.</summary>
	public string? Filter { get; init; }

	/// <summary>Catalogue the tests instead of running them.</summary>
	public bool List { get; init; }

	/// <summary>World generation seed.</summary>
	public int WorldSeed { get; init; } = 42;

	/// <summary>Run seed, shifting every test's own seed. Null leaves the game's default of zero.</summary>
	public int? RunSeed { get; init; }

	/// <summary>Fast forward: <c>max</c>, <c>realtime</c>, or a number of ticks per second.</summary>
	public string? Speed { get; init; }

	/// <summary>Replace world generation with Testaria's blank substrate.</summary>
	public bool BlankWorld { get; init; }

	/// <summary>Promise that this process has a world to itself, honouring <c>[FreshWorld]</c>.</summary>
	public bool FreshWorld { get; init; }

	/// <summary>Seconds the whole run may take before the harness gives up.</summary>
	public int TimeoutSeconds { get; init; } = 600;

	/// <summary>Where to copy the report once the run finishes.</summary>
	public string? ResultsOut { get; init; }

	/// <summary>The tModLoader install to run, or null to go looking for one.</summary>
	public string? TmlPath { get; init; }

	/// <summary>Where bare mod names are looked up, or null to go looking.</summary>
	public string? ModsDirectory { get; init; }

	/// <summary>
	/// Client processes to start and join to the server, for tier 3.
	/// <para/>
	/// Zero by default, and tier 3 tests are then reported as skipped rather
	/// than run, since a netcode test with nobody on the other end would pass
	/// while proving nothing.
	/// </summary>
	public int Clients { get; init; }

	/// <summary>
	/// Fewest tests that must actually run, as opposed to being skipped.
	/// <para/>
	/// Zero means no such demand. Worth setting in CI for a suite whose
	/// subject is another mod: if that mod stops loading, every test skips and
	/// the run reports success, which is indistinguishable from the suite
	/// passing.
	/// </summary>
	public int Require { get; init; }

	/// <summary>
	/// Measure what every boxed test costs and uses, writing a table beside the
	/// report.
	/// <para/>
	/// For calibrating the arena's constants against a real suite rather than
	/// against intuition. A measured run outlives its last test by a few
	/// seconds, because released boxes are watched to see how long they take to
	/// go quiet.
	/// </summary>
	public bool Measure { get; init; }

	/// <summary>Keep the scratch save directory instead of deleting it.</summary>
	public bool KeepScratch { get; init; }

	/// <summary>Print only the summary, not the harness's own progress.</summary>
	public bool Quiet { get; init; }

	/// <summary>Print the server's own log as it happens, for debugging a run that will not start.</summary>
	public bool Verbose { get; init; }
}

/// <summary>What the command line asked for, or why it could not be read.</summary>
public sealed record ParseResult
{
	/// <summary>The parsed options, when the command line was usable.</summary>
	public RunOptions? Options { get; init; }

	/// <summary>What was wrong, when it was not.</summary>
	public string? Error { get; init; }

	/// <summary>True when the user asked for help rather than a run.</summary>
	public bool WantsHelp { get; init; }
}

/// <summary>
/// Reads the command line.
/// <para/>
/// By hand rather than through a parsing library, for the same reason the rest
/// of this repository carries no dependencies it can avoid: a tool whose whole
/// job is to start another process should not drag a parser, and its own
/// package graph, along behind it.
/// </summary>
public static class CommandLine
{
	/// <summary>Usage, printed for <c>--help</c> and for a command line that cannot be read.</summary>
	public const string Usage = """
		testaria - run a mod's tests inside a headless tModLoader server

		usage:
		  testaria run  [options]     run the tests and report the result
		  testaria list [options]     catalogue the tests without running them

		tier 3:
		  --client [n]           Start n client processes and join them to the
		                         server, so tier 3 tests can run. Default 1 when
		                         the flag is given, 0 when it is not. Needs a
		                         display: on Linux the harness starts Xvfb, which
		                         a client requires and a server does not.

		mods:
		  --mod <name|path>      A .tmod to install and enable. Repeatable, and
		                         order is load order. A bare name is looked up in
		                         the mods directory.
		  --project <dir>        A mod project to build before the run. Repeatable.
		  --mods-dir <dir>       Where bare --mod names are found. Defaults to the
		                         Mods directory of the tModLoader save path.

		the run:
		  --name <run>           Name of the run and of its report. Default Testaria.
		  --filter <regex>       Narrow the run to matching tests.
		  --seed <n>             Run seed, shifting every test's own seed. Default 0.
		  --world-seed <n>       World generation seed. Default 42.
		  --speed <max|n>        Fast forward: max, realtime, or ticks per second.
		  --blank                Generate Testaria's blank world instead of a real one.
		  --fresh-world          Promise this process has a world to itself, so
		                         [FreshWorld] tests are honoured rather than skipped.
		  --timeout <seconds>    Give up after this long. Default 600.
		  --require <n>          Fail unless at least n tests actually ran. Guards
		                         against a suite that silently skipped everything,
		                         which otherwise reports success.

		output:
		  --results <path>       Copy the JUnit XML report here.
		  --measure              Write a table of what each boxed test cost and used,
		                         beside the report, for calibrating the arena.
		  --keep-scratch         Keep the scratch save directory and say where it is.
		  --quiet                Print only the summary.
		  --verbose              Print the server's log as it happens.

		environment:
		  --tml <dir>            The tModLoader install to run. Defaults to TML_PATH,
		                         then to the platform's usual Steam library.

		exit codes:
		  0  every test passed or was skipped
		  1  a test failed, errored, or was blocked
		  2  the harness could not run the tests at all
		""";

	/// <summary>Reads an argument list.</summary>
	public static ParseResult Parse(IReadOnlyList<string> args)
	{
		ArgumentNullException.ThrowIfNull(args);

		if (args.Count == 0)
			return new ParseResult { WantsHelp = true };

		string verb = args[0];

		if (verb is "-h" or "--help" or "help")
			return new ParseResult { WantsHelp = true };

		if (verb is not ("run" or "list"))
			return new ParseResult { Error = $"Unknown command '{verb}'." };

		var mods = new List<string>();
		var projects = new List<string>();
		var options = new RunOptions { List = verb == "list" };

		for (int i = 1; i < args.Count; i++) {
			string argument = args[i];

			// Every flag that takes a value fetches it the same way, so a
			// missing value is one error message rather than ten.
			string? Value()
			{
				if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
					return args[++i];

				return null;
			}

			ParseResult? Missing() => new() { Error = $"{argument} needs a value." };
			ParseResult? NotANumber(string? text) => new() { Error = $"{argument} needs a whole number, got '{text}'." };

			switch (argument) {
				case "-h" or "--help":
					return new ParseResult { WantsHelp = true };

				case "--mod":
					if (Value() is not string mod)
						return Missing()!;
					mods.Add(mod);
					break;

				case "--project":
					if (Value() is not string project)
						return Missing()!;
					projects.Add(project);
					break;

				case "--name":
					if (Value() is not string name)
						return Missing()!;
					options = options with { Name = name };
					break;

				case "--filter":
					if (Value() is not string filter)
						return Missing()!;
					options = options with { Filter = filter };
					break;

				case "--seed":
					string? seedText = Value();
					if (seedText is null)
						return Missing()!;
					if (!int.TryParse(seedText, out int seed))
						return NotANumber(seedText)!;
					options = options with { RunSeed = seed };
					break;

				case "--world-seed":
					string? worldSeedText = Value();
					if (worldSeedText is null)
						return Missing()!;
					if (!int.TryParse(worldSeedText, out int worldSeed))
						return NotANumber(worldSeedText)!;
					options = options with { WorldSeed = worldSeed };
					break;

				case "--speed":
					if (Value() is not string speed)
						return Missing()!;
					options = options with { Speed = speed };
					break;

				case "--timeout":
					string? timeoutText = Value();
					if (timeoutText is null)
						return Missing()!;
					if (!int.TryParse(timeoutText, out int timeout) || timeout <= 0)
						return new ParseResult { Error = $"--timeout needs a positive number of seconds, got '{timeoutText}'." };
					options = options with { TimeoutSeconds = timeout };
					break;

				case "--require":
					string? requireText = Value();
					if (requireText is null)
						return Missing()!;
					if (!int.TryParse(requireText, out int require) || require < 0)
						return new ParseResult { Error = $"--require needs a count, got '{requireText}'." };
					options = options with { Require = require };
					break;

				case "--results":
					if (Value() is not string results)
						return Missing()!;
					options = options with { ResultsOut = results };
					break;

				case "--tml":
					if (Value() is not string tml)
						return Missing()!;
					options = options with { TmlPath = tml };
					break;

				case "--mods-dir":
					if (Value() is not string modsDirectory)
						return Missing()!;
					options = options with { ModsDirectory = modsDirectory };
					break;

				case "--client":
					// The count is optional: "--client" on its own means one,
					// which is what almost every run wants.
					string? clientsText = Value();

					if (clientsText is null) {
						options = options with { Clients = 1 };
						break;
					}

					if (!int.TryParse(clientsText, out int clients) || clients < 1)
						return new ParseResult { Error = $"--client needs a positive number of clients, got '{clientsText}'." };

					options = options with { Clients = clients };
					break;

				case "--measure":
					options = options with { Measure = true };
					break;

				case "--blank":
					options = options with { BlankWorld = true };
					break;

				case "--fresh-world":
					options = options with { FreshWorld = true };
					break;

				case "--keep-scratch":
					options = options with { KeepScratch = true };
					break;

				case "--quiet":
					options = options with { Quiet = true };
					break;

				case "--verbose":
					options = options with { Verbose = true };
					break;

				default:
					return new ParseResult { Error = $"Unknown option '{argument}'." };
			}
		}

		if (mods.Count == 0)
			return new ParseResult { Error = "Nothing to run: name at least one mod with --mod." };

		return new ParseResult { Options = options with { Mods = mods, Projects = projects } };
	}
}
