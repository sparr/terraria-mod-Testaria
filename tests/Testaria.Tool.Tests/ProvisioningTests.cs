namespace Testaria.Tool.Tests;

/// <summary>
/// Setting a run up: the scratch save directory, the mods in it, and the
/// command line the server is started with.
/// </summary>
public class ProvisioningTests : IDisposable
{
	private readonly string sourceMods = Path.Combine(Path.GetTempPath(), "testaria-tool-tests-" + Path.GetRandomFileName());

	public ProvisioningTests()
	{
		Directory.CreateDirectory(sourceMods);

		foreach (string name in new[] { "Testaria", "MyMod", "MyModTests" })
			File.WriteAllText(Path.Combine(sourceMods, name + ".tmod"), "pretend this is a mod");
	}

	public void Dispose()
	{
		if (Directory.Exists(sourceMods))
			Directory.Delete(sourceMods, recursive: true);

		GC.SuppressFinalize(this);
	}

	private static string EnabledJson(ScratchSave scratch)
		=> File.ReadAllText(Path.Combine(scratch.ModsDirectory, "enabled.json")).Trim();

	[Fact]
	public void A_scratch_save_has_the_directories_a_server_expects()
	{
		using ScratchSave scratch = ScratchSave.Create();

		XAssert.True(Directory.Exists(scratch.ModsDirectory));
		XAssert.True(Directory.Exists(scratch.WorldsDirectory));
	}

	[Fact]
	public void A_scratch_save_is_gone_afterwards()
	{
		string root;

		using (ScratchSave scratch = ScratchSave.Create())
			root = scratch.Root;

		// Not a matter of tidiness: a run leaves a generated world behind, and
		// a suite that ran a hundred times would otherwise leave a hundred.
		XAssert.False(Directory.Exists(root));
	}

	[Fact]
	public void A_kept_scratch_save_stays_put()
	{
		string root;

		using (ScratchSave scratch = ScratchSave.Create(keep: true))
			root = scratch.Root;

		XAssert.True(Directory.Exists(root));
		Directory.Delete(root, recursive: true);
	}

	[Fact]
	public void Mods_are_installed_by_name_and_enabled_in_order()
	{
		using ScratchSave scratch = ScratchSave.Create();

		IReadOnlyList<string> enabled = scratch.Install(["Testaria", "MyMod", "MyModTests"], sourceMods);

		XAssert.Equal(["Testaria", "MyMod", "MyModTests"], enabled);
		XAssert.True(File.Exists(Path.Combine(scratch.ModsDirectory, "MyMod.tmod")));
		XAssert.Equal("[\"Testaria\",\"MyMod\",\"MyModTests\"]", EnabledJson(scratch));
	}

	[Fact]
	public void The_runtime_is_enabled_even_when_nobody_asked_for_it()
	{
		using ScratchSave scratch = ScratchSave.Create();

		IReadOnlyList<string> enabled = scratch.Install(["MyModTests"], sourceMods);

		// Without it there is no runner, and the run would sit there until the
		// timeout with no explanation. Prepended rather than appended, since
		// anything referencing it must load after it.
		XAssert.Equal(["Testaria", "MyModTests"], enabled);
	}

	[Fact]
	public void A_mod_named_twice_is_enabled_once()
	{
		using ScratchSave scratch = ScratchSave.Create();

		XAssert.Equal(["Testaria", "MyMod"], scratch.Install(["Testaria", "MyMod", "MyMod"], sourceMods));
	}

	[Fact]
	public void A_mod_can_be_given_as_a_path()
	{
		using ScratchSave scratch = ScratchSave.Create();

		// The case that matters for a mod that has just been built somewhere
		// other than the shared mods directory, which is most CI jobs.
		IReadOnlyList<string> enabled = scratch.Install([Path.Combine(sourceMods, "MyMod.tmod")], sourceMods);

		XAssert.Equal(["Testaria", "MyMod"], enabled);
	}

	[Fact]
	public void A_missing_mod_says_where_it_looked()
	{
		using ScratchSave scratch = ScratchSave.Create();

		FileNotFoundException thrown = XAssert.Throws<FileNotFoundException>(
			() => scratch.Install(["Absent"], sourceMods));

		XAssert.Contains("Absent", thrown.Message);
		XAssert.Contains(sourceMods, thrown.Message);
	}

	[Fact]
	public void A_missing_mods_directory_suggests_what_to_do()
	{
		using ScratchSave scratch = ScratchSave.Create();

		FileNotFoundException thrown = XAssert.Throws<FileNotFoundException>(
			() => scratch.Install(["MyMod"], modsDirectory: null));

		XAssert.Contains("--mods-dir", thrown.Message);
	}

	[Fact]
	public void The_server_is_told_to_keep_to_the_scratch_directory()
	{
		using ScratchSave scratch = ScratchSave.Create();

		IReadOnlyList<string> arguments = ServerArguments.For(new RunOptions(), scratch);

		XAssert.Contains("-server", arguments);
		XAssert.Contains("-nosteam", arguments);
		// The one flag that keeps a run away from a real installation.
		XAssert.Equal(scratch.Root, Next(arguments, "-tmlsavedirectory"));
		XAssert.Equal(scratch.WorldPath, Next(arguments, "-world"));
	}

	[Fact]
	public void A_world_is_generated_rather_than_waited_for()
	{
		using ScratchSave scratch = ScratchSave.Create();

		IReadOnlyList<string> arguments = ServerArguments.For(new RunOptions { WorldSeed = 7 }, scratch);

		XAssert.Equal("1", Next(arguments, "-autocreate"));
		XAssert.Equal("7", Next(arguments, "-seed"));
	}

	[Fact]
	public void Optional_flags_are_absent_unless_asked_for()
	{
		using ScratchSave scratch = ScratchSave.Create();

		IReadOnlyList<string> arguments = ServerArguments.For(new RunOptions(), scratch);

		XAssert.DoesNotContain("-testariablank", arguments);
		XAssert.DoesNotContain("-testariafreshworld", arguments);
		XAssert.DoesNotContain("-testariaspeed", arguments);
		XAssert.DoesNotContain("-testariaseed", arguments);
		XAssert.DoesNotContain(RequiredMods.Flag, arguments);
	}

	[Fact]
	public void The_run_is_told_which_mods_it_must_find()
	{
		using ScratchSave scratch = ScratchSave.Create();

		IReadOnlyList<string> arguments =
			ServerArguments.For(new RunOptions(), scratch, ["Testaria", "ExampleMod", "ExampleModTests"]);

		// The whole point: without this the game cannot tell "this suite has
		// no tests" from "this suite's subject failed to load", and reports
		// the second as a clean run.
		XAssert.Equal("Testaria,ExampleMod,ExampleModTests", Next(arguments, RequiredMods.Flag));
	}

	[Fact]
	public void No_mods_means_no_demand()
	{
		using ScratchSave scratch = ScratchSave.Create();

		XAssert.DoesNotContain(RequiredMods.Flag, ServerArguments.For(new RunOptions(), scratch, []));
	}

	[Fact]
	public void Optional_flags_are_passed_when_they_are()
	{
		using ScratchSave scratch = ScratchSave.Create();

		IReadOnlyList<string> arguments = ServerArguments.For(
			new RunOptions { BlankWorld = true, FreshWorld = true, Speed = "max", RunSeed = 7 },
			scratch);

		XAssert.Contains("-testariablank", arguments);
		// The one whose absence is silent: without it every [FreshWorld] test
		// is skipped and the run still reports green.
		XAssert.Contains("-testariafreshworld", arguments);
		XAssert.Equal("max", Next(arguments, "-testariaspeed"));
		XAssert.Equal("7", Next(arguments, "-testariaseed"));
	}

	[Fact]
	public void Measuring_is_asked_for_only_when_wanted()
	{
		using ScratchSave scratch = ScratchSave.Create();

		XAssert.DoesNotContain("-testariameasure", ServerArguments.For(new RunOptions(), scratch));
		XAssert.Contains("-testariameasure", ServerArguments.For(new RunOptions { Measure = true }, scratch));
	}

	[Fact]
	public void The_measurements_land_beside_the_report()
	{
		using ScratchSave scratch = ScratchSave.Create();

		// Same directory, same name, different extension, so a CI job
		// collecting one collects the other.
		XAssert.Equal(
			Path.Combine(scratch.Root, "Testaria", "Suite-arena.tsv"),
			ServerArguments.MetricsPath(new RunOptions { Name = "Suite" }, scratch));
	}

	[Fact]
	public void The_console_command_carries_the_run_name_and_filter()
	{
		using ScratchSave scratch = ScratchSave.Create();

		(string command, string results) = ServerArguments.Command(
			new RunOptions { Name = "Suite", Filter = "Zombie" },
			scratch);

		XAssert.Equal("testaria run Suite Zombie", command);
		XAssert.Equal(Path.Combine(scratch.Root, "Testaria", "Suite.xml"), results);
	}

	[Fact]
	public void Listing_asks_for_a_catalogue_instead()
	{
		using ScratchSave scratch = ScratchSave.Create();

		(string command, string results) = ServerArguments.Command(new RunOptions { List = true }, scratch);

		XAssert.Equal("testaria list", command);
		XAssert.EndsWith("tests.tsv", results);
	}

	private static string? Next(IReadOnlyList<string> arguments, string flag)
	{
		int index = arguments.ToList().IndexOf(flag);

		return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
	}
}
