namespace Testaria.Tool.Tests;

/// <summary>
/// Seeding a mod's configuration for a run.
/// <para/>
/// The file's own name is the contract: tModLoader matches a config file to a
/// config class by it, and silently ignores one it cannot match. So a misnamed
/// file leaves the run using defaults, with nothing anywhere saying so, which
/// is why the naming is checked up front rather than trusted.
/// </summary>
public class ModConfigTests : IDisposable
{
	private readonly string work = Path.Combine(Path.GetTempPath(), "testaria-configs-" + Path.GetRandomFileName());

	public ModConfigTests() => Directory.CreateDirectory(work);

	public void Dispose() => Directory.Delete(work, recursive: true);

	private string Write(string name, string contents = "{}")
	{
		string path = Path.Combine(work, name);

		File.WriteAllText(path, contents);

		return path;
	}

	[Fact]
	public void The_file_name_is_the_mod_and_the_config_class()
		=> XAssert.Equal("SilkyUIFramework_SilkyUIClientConfig.json",
			ModConfigs.FileName("SilkyUIFramework", "SilkyUIClientConfig"));

	[Fact]
	public void A_properly_named_file_is_accepted()
		=> XAssert.Null(ModConfigs.Validate(Write("MyMod_MyConfig.json")));

	[Fact]
	public void A_file_that_is_not_there_is_refused()
		=> XAssert.Contains("No config file", ModConfigs.Validate(Path.Combine(work, "Absent_Config.json"))!);

	[Fact]
	public void A_file_that_is_not_json_is_refused()
		=> XAssert.Contains(".json", ModConfigs.Validate(Write("MyMod_MyConfig.txt"))!);

	[Theory]
	[InlineData("NoUnderscore.json")]
	[InlineData("_LeadingUnderscore.json")]
	[InlineData("TrailingUnderscore_.json")]
	public void A_misnamed_file_is_refused_rather_than_ignored(string name)
	{
		// The failure this prevents is the quiet one: the game ignores it and
		// the run tests defaults while looking configured.
		XAssert.Contains("ModName", ModConfigs.Validate(Write(name))!);
	}

	[Fact]
	public void A_config_lands_where_both_scopes_are_read_from()
	{
		string source = Write("MyMod_MyConfig.json", "{\"EnableThing\": false}");
		string save = Path.Combine(work, "save");

		ModConfigs.InstallInto(save, [source]);

		// Both, because a ClientSide config is read from one directory and a
		// ServerSide config from the other, and the caller should not have to
		// know which somebody else's mod used.
		string client = Path.Combine(save, "ModConfigs", "MyMod_MyConfig.json");
		string server = Path.Combine(save, "ModConfigs", "Server", "MyMod_MyConfig.json");

		XAssert.True(File.Exists(client), "the client-scoped copy should be there");
		XAssert.True(File.Exists(server), "the server-scoped copy should be there");
		XAssert.Equal("{\"EnableThing\": false}", File.ReadAllText(client));
	}

	[Fact]
	public void Seeding_nothing_creates_nothing()
	{
		string save = Path.Combine(work, "empty");

		ModConfigs.InstallInto(save, []);

		XAssert.False(Directory.Exists(Path.Combine(save, "ModConfigs")));
	}

	[Fact]
	public void Installing_a_misnamed_config_fails_the_run()
		=> XAssert.Throws<HarnessException>(
			() => ModConfigs.InstallInto(Path.Combine(work, "save"), [Write("Nope.json")]));

	[Fact]
	public void The_command_line_takes_configs_and_keeps_their_order()
	{
		string first = Write("A_Config.json");
		string second = Write("B_Config.json");

		ParseResult result = CommandLine.Parse(
			["run", "--mod", "M", "--config", first, "--config", second]);

		XAssert.Null(result.Error);
		XAssert.Equal([first, second], result.Options!.Configs);
	}

	[Fact]
	public void The_command_line_refuses_a_config_it_cannot_match()
	{
		ParseResult result = CommandLine.Parse(["run", "--mod", "M", "--config", Write("Nope.json")]);

		// Up front, rather than at run time: the run would otherwise start,
		// take a minute, and test the defaults.
		XAssert.NotNull(result.Error);
		XAssert.Contains("ModName", result.Error);
	}

	[Fact]
	public void No_configs_are_seeded_by_default()
		=> XAssert.Empty(CommandLine.Parse(["run", "--mod", "M"]).Options!.Configs);
}
