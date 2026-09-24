using System.Runtime.CompilerServices;

namespace Testaria;

/// <summary>
/// Whether a completed load pass is underneath this code, and the fail-fast
/// check that says so out loud.
/// <para/>
/// The analyzer catches loader-dependent surface at compile time, but it can
/// only see what it can name. A helper reaching the game through reflection,
/// a cross-mod <c>Mod.Call</c>, or a type the list has never heard of still
/// answers with an empty collection rather than an error. This is the runtime
/// half: one call, at the top of anything that needs the game, that turns
/// "silently wrong" into "loudly absent".
/// <para/>
/// Deliberately a flag the game sets rather than a probe of the game's own
/// statics, because the core carries no tModLoader reference by design
/// (PLAN.md section 4.4.3). The Testaria mod raises it once the load pass has
/// finished and lowers it on unload, so a reload cannot leave it lying.
/// </summary>
public static class GameState
{
	// Volatile because the flag is raised on the load thread and read from the
	// update thread, and a stale read here would be a false "no game" in the
	// middle of a run rather than a missed optimization.
	private static volatile bool loaded;

	/// <summary>
	/// True between the end of a load pass and the start of an unload.
	/// <para/>
	/// False in a <c>dotnet test</c> host, always, which is the whole point.
	/// </summary>
	public static bool IsLoaded => loaded;

	/// <summary>
	/// Records that the load pass has finished. Called by the Testaria mod;
	/// nothing else has any business raising it.
	/// </summary>
	public static void MarkLoaded() => loaded = true;

	/// <summary>
	/// Records that the game is going away. Must run on unload, or a reload
	/// leaves the flag raised over a game that is no longer there.
	/// </summary>
	public static void MarkUnloaded() => loaded = false;

	/// <summary>
	/// Throws unless a load pass has completed.
	/// </summary>
	/// <param name="what">
	/// What is being asked for, named in the message. Worth passing: "the
	/// caller needed something" is a much weaker diagnostic than "the caller
	/// needed ContentSamples".
	/// </param>
	/// <param name="member">The calling member, filled in by the compiler.</param>
	/// <exception cref="LoaderStateException">No load pass has completed.</exception>
	public static void Require(string? what = null, [CallerMemberName] string? member = null)
	{
		if (loaded)
			return;

		string subject = what is null ? "loader state" : what;
		string caller = member is null ? "this code" : $"'{member}'";

		throw new LoaderStateException(
			$"{caller} needs {subject}, which only a completed load pass provides, and no game is loaded. "
			+ "Outside the game that state is default-initialized rather than absent, so continuing would "
			+ "produce an answer that looks right and is not. Move this to a [LoadedTest] or [GameTest] "
			+ "in a test mod, or stub the dependency out.");
	}
}

/// <summary>
/// Thrown when code that needs a loaded game runs without one.
/// <para/>
/// Its own type rather than a bare <see cref="InvalidOperationException"/> so
/// that a suite can tell "this test is in the wrong tier" apart from "the code
/// under test is broken". They call for entirely different fixes.
/// </summary>
public sealed class LoaderStateException : InvalidOperationException
{
	/// <summary>Reports code running outside the game that needs to be inside it.</summary>
	public LoaderStateException(string message) : base(message) { }

	/// <summary>Reports code running outside the game that needs to be inside it.</summary>
	public LoaderStateException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Reports code running outside the game that needs to be inside it.</summary>
	public LoaderStateException() : base("This code needs a loaded game, and no game is loaded.") { }
}
