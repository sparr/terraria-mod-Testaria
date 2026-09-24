namespace Testaria.Tool;

/// <summary>
/// Puts a mod's configuration where tModLoader will read it, before anything
/// starts.
/// <para/>
/// Configuration is a first-class tModLoader feature and a run had no way to
/// touch it, so a suite whose subject behaves differently under a setting
/// could only test the default. Worse, a mod can be unusable in a run because
/// of one: SilkyUI enables a blur effect by default, and on a machine whose
/// build could not compile the shader the client dies at its first frame
/// rather than running any test.
/// <para/>
/// Seeding a file rather than driving a UI, for the same reason
/// <c>enabled.json</c> is written by hand: it is what the game reads, and
/// nothing about it needs Steam, a menu, or a person.
/// </summary>
public static class ModConfigs
{
	/// <summary>Where a client-scoped config lives, under a save directory.</summary>
    public const string ClientDirectoryName = "ModConfigs";

	/// <summary>Where a server-scoped config lives, under a save directory.</summary>
	public static readonly string ServerDirectoryName = Path.Combine("ModConfigs", "Server");

	/// <summary>
	/// The name tModLoader expects a config file to have: the mod's internal
	/// name, an underscore, the config class name, and <c>.json</c>.
	/// </summary>
	public static string FileName(string mod, string config)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(mod);
		ArgumentException.ThrowIfNullOrWhiteSpace(config);

		return $"{mod}_{config}.json";
	}

	/// <summary>
	/// Checks that a path looks like a config file tModLoader would read, and
	/// says what is wrong if it does not.
	/// <para/>
	/// The file's own name is the contract, so a misnamed file is silently
	/// ignored by the game and the run proceeds with the default. Refusing it
	/// here is the difference between a clear error and a test that fails for
	/// a reason nobody can see.
	/// </summary>
	/// <returns>Null if it is usable, or the problem.</returns>
	public static string? Validate(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);

		if (!File.Exists(path))
			return $"No config file at '{path}'.";

		string name = Path.GetFileName(path);

		if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			return $"'{name}' is not a .json file. tModLoader reads configs by file name.";

		// One underscore separating a mod name from a config class name. Both
		// halves have to be there or the game will not match it to anything.
		int underscore = name.IndexOf('_', StringComparison.Ordinal);

		if (underscore <= 0 || underscore == name.Length - ".json".Length - 1)
			return $"'{name}' should be named <ModName>_<ConfigClassName>.json, which is how tModLoader matches a "
				+ "config file to the config it belongs to.";

		return null;
	}

	/// <summary>
	/// Copies configs into a save directory, in both the client and the server
	/// location.
	/// <para/>
	/// Both, rather than asking which scope each one is: the game reads a
	/// <c>ClientSide</c> config from one and a <c>ServerSide</c> config from
	/// the other, and ignores a file it cannot match. So putting each in both
	/// is harmless and removes a question the caller should not have to answer
	/// about somebody else's mod.
	/// </summary>
	public static void InstallInto(string saveDirectory, IEnumerable<string> configs)
	{
		ArgumentException.ThrowIfNullOrEmpty(saveDirectory);
		ArgumentNullException.ThrowIfNull(configs);

		string client = Path.Combine(saveDirectory, ClientDirectoryName);
		string server = Path.Combine(saveDirectory, ServerDirectoryName);

		foreach (string source in configs) {
			if (Validate(source) is string problem)
				throw new HarnessException(problem);

			Directory.CreateDirectory(client);
			Directory.CreateDirectory(server);

			string name = Path.GetFileName(source);

			File.Copy(source, Path.Combine(client, name), overwrite: true);
			File.Copy(source, Path.Combine(server, name), overwrite: true);
		}
	}
}
