namespace Testaria.Tests;

/// <summary>
/// The runtime half of the Tier 0 boundary.
/// <para/>
/// These tests run in exactly the host the boundary exists to protect, a bare
/// <c>dotnet test</c> process with no game anywhere near it, so the default
/// they assert on is the one every unit test will see.
/// </summary>
public class GameStateTests : IDisposable
{
	// The flag is process-global, because the game it describes is. Restoring
	// it is what keeps one test here from teaching the next a lie.
	private readonly bool original = GameState.IsLoaded;

	public void Dispose()
	{
		if (original)
			GameState.MarkLoaded();
		else
			GameState.MarkUnloaded();

		GC.SuppressFinalize(this);
	}

	[Fact]
	public void No_game_is_loaded_in_a_test_host()
		=> XAssert.False(GameState.IsLoaded);

	[Fact]
	public void Require_throws_when_no_load_pass_has_run()
		=> XAssert.Throws<LoaderStateException>(() => GameState.Require("ContentSamples"));

	[Fact]
	public void The_message_names_the_subject_and_the_caller()
	{
		LoaderStateException thrown = XAssert.Throws<LoaderStateException>(
			() => GameState.Require("ContentSamples"));

		XAssert.Contains("ContentSamples", thrown.Message);
		// CallerMemberName, so the report says which helper reached for the
		// game rather than just that something did.
		XAssert.Contains(nameof(The_message_names_the_subject_and_the_caller), thrown.Message);
	}

	[Fact]
	public void The_message_says_what_to_do_about_it()
	{
		LoaderStateException thrown = XAssert.Throws<LoaderStateException>(() => GameState.Require());

		// An exception that only says "no" leaves an author guessing at a
		// boundary they may not know exists.
		XAssert.Contains("[LoadedTest]", thrown.Message);
	}

	[Fact]
	public void Require_is_silent_once_the_load_pass_has_finished()
	{
		GameState.MarkLoaded();

		GameState.Require("ContentSamples");

		XAssert.True(GameState.IsLoaded);
	}

	[Fact]
	public void Unloading_puts_the_guard_back_up()
	{
		GameState.MarkLoaded();
		GameState.MarkUnloaded();

		// The reload case: an assembly load context outlives its game, and a
		// flag left raised over a game that has gone would make the next run's
		// failures unreadable.
		XAssert.Throws<LoaderStateException>(() => GameState.Require());
	}

	[Fact]
	public void A_loader_state_failure_is_its_own_kind()
		// Distinguishable from the code under test being broken, which is a
		// different problem with a different fix.
		=> XAssert.IsAssignableFrom<InvalidOperationException>(new LoaderStateException());
}
