namespace Testaria.Tool.Tests;

/// <summary>
/// Tier 3's setup: asking for clients, and giving one a save directory it can
/// actually start from.
/// </summary>
public class ClientTests : IDisposable
{
	private readonly string sourceMods = Path.Combine(Path.GetTempPath(), "testaria-client-tests-" + Path.GetRandomFileName());
	private readonly string install = Path.Combine(Path.GetTempPath(), "testaria-install-" + Path.GetRandomFileName());

	public ClientTests()
	{
		Directory.CreateDirectory(sourceMods);
		Directory.CreateDirectory(install);

		foreach (string name in new[] { "Testaria", "MyModTests" })
			File.WriteAllText(Path.Combine(sourceMods, name + ".tmod"), "pretend this is a mod");

		File.WriteAllText(
			Path.Combine(install, "RecentGitHubCommits.txt"),
			"abc123def456 Most recent commit message\nolder0000 An older one\n");
	}

	public void Dispose()
	{
		foreach (string directory in new[] { sourceMods, install }) {
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		}

		GC.SuppressFinalize(this);
	}

	private static RunOptions Parse(params string[] args)
	{
		ParseResult result = CommandLine.Parse(args);

		XAssert.Null(result.Error);

		return result.Options!;
	}

	[Fact]
	public void No_clients_are_started_unless_asked_for()
		// Tier 3 then reports as skipped, which is the honest answer: there is
		// nobody on the other end.
		=> XAssert.Equal(0, Parse("run", "--mod", "A").Clients);

	[Fact]
	public void One_client_is_what_the_bare_flag_means()
		=> XAssert.Equal(1, Parse("run", "--mod", "A", "--client").Clients);

	[Fact]
	public void The_bare_flag_does_not_swallow_the_next_option()
		=> XAssert.True(Parse("run", "--mod", "A", "--client", "--blank").BlankWorld);

	[Fact]
	public void A_count_can_be_given()
		=> XAssert.Equal(3, Parse("run", "--mod", "A", "--client", "3").Clients);

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("some")]
	public void A_nonsensical_count_is_refused(string count)
		=> XAssert.Contains("--client", CommandLine.Parse(["run", "--mod", "A", "--client", count]).Error!);

	[Fact]
	public void A_client_gets_its_own_save_directory_with_the_same_mods()
	{
		using ScratchSave scratch = ScratchSave.Create();
		IReadOnlyList<string> enabled = scratch.Install(["MyModTests"], sourceMods);

		string directory = scratch.PrepareClient(0, enabled, install);

		// Its own, because two processes sharing one would write over each
		// other's players and logs.
		XAssert.NotEqual(scratch.Root, directory);
		XAssert.True(File.Exists(Path.Combine(directory, "Mods", "MyModTests.tmod")));
		XAssert.True(File.Exists(Path.Combine(directory, "Mods", "Testaria.tmod")));
		XAssert.Equal(
			"[\"Testaria\",\"MyModTests\"]",
			File.ReadAllText(Path.Combine(directory, "Mods", "enabled.json")).Trim());
	}

	[Fact]
	public void Two_clients_do_not_share_a_directory()
	{
		using ScratchSave scratch = ScratchSave.Create();
		IReadOnlyList<string> enabled = scratch.Install(["MyModTests"], sourceMods);

		XAssert.NotEqual(scratch.PrepareClient(0, enabled, install), scratch.PrepareClient(1, enabled, install));
	}

	[Fact]
	public void The_seeded_config_answers_every_screen_that_would_block_a_start()
	{
		using ScratchSave scratch = ScratchSave.Create();
		string directory = scratch.PrepareClient(0, scratch.Install(["MyModTests"], sourceMods), install);

		string config = File.ReadAllText(Path.Combine(directory, "config.json"));

		// Each of these answers a screen that stops a client before it loads a
		// mod. Without the language it never loads one at all; without the
		// version it opens a welcome dialog; without the commit, a dev build
		// opens change notes.
		XAssert.Contains("\"Language\": \"en-US\"", config);
		XAssert.Contains("\"LastLaunchedTModLoaderVersion\": \"9999.0\"", config);
		XAssert.Contains("\"LastLaunchedTModLoaderAlphaSha\": \"abc123def456\"", config);
	}

	[Fact]
	public void The_commit_is_read_from_the_list_the_install_ships()
		=> XAssert.Equal("abc123def456", ClientSave.CurrentCommit(install));

	[Fact]
	public void An_install_with_no_commit_list_is_not_fatal()
		// It costs a dialog on a dev build, which is a great deal better than
		// refusing to run at all over a cosmetic screen.
		=> XAssert.Equal("unknown", ClientSave.CurrentCommit(Path.Combine(install, "nowhere")));
}
