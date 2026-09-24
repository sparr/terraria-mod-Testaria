namespace Testaria.Tool;

/// <summary>
/// A throwaway tModLoader save directory holding exactly the mods a run needs.
/// <para/>
/// Runs happen here rather than in a real installation, so a test run cannot
/// touch anyone's worlds, players, or enabled mods. <c>-tmlsavedirectory</c>
/// points both <c>SavePath</c> and <c>SavePathShared</c> at it, so nothing the
/// run does reaches further than this folder.
/// </summary>
public sealed class ScratchSave : IDisposable
{
	/// <summary>The mod that carries the runtime, always enabled whether or not it was asked for.</summary>
	public const string Runtime = "Testaria";

	private readonly bool keep;

	private ScratchSave(string root, bool keep)
	{
		Root = root;
		this.keep = keep;
	}

	/// <summary>The save directory itself.</summary>
	public string Root { get; }

	/// <summary>Where the enabled mods live.</summary>
	public string ModsDirectory => Path.Combine(Root, "Mods");

	/// <summary>Where a generated world lands.</summary>
	public string WorldsDirectory => Path.Combine(Root, "Worlds");

	/// <summary>The world file a run generates and loads.</summary>
	public string WorldPath => Path.Combine(WorldsDirectory, "testaria.wld");

	/// <summary>The server's own log, kept for the diagnostics a failure needs.</summary>
	public string LogPath => Path.Combine(Root, "server.log");

	/// <summary>Creates an empty scratch save directory under the system temp folder.</summary>
	/// <param name="keep">Leave it behind on disposal, for inspecting a run that went wrong.</param>
	public static ScratchSave Create(bool keep = false)
	{
		string root = Path.Combine(Path.GetTempPath(), "testaria-" + Path.GetRandomFileName());
		var scratch = new ScratchSave(root, keep);

		Directory.CreateDirectory(scratch.ModsDirectory);
		Directory.CreateDirectory(scratch.WorldsDirectory);

		return scratch;
	}

	/// <summary>
	/// Installs the named mods and enables them, in the order given.
	/// <para/>
	/// The runtime mod is prepended when the caller did not name it, since a
	/// run without it has nothing to run the tests. Everything else is the
	/// caller's business, including load order.
	/// </summary>
	/// <param name="mods">Bare mod names or paths to <c>.tmod</c> files.</param>
	/// <param name="modsDirectory">Where bare names are looked up.</param>
	/// <returns>The enabled mod names, in load order.</returns>
	/// <exception cref="FileNotFoundException">A named mod could not be found.</exception>
	public IReadOnlyList<string> Install(IEnumerable<string> mods, string? modsDirectory)
	{
		ArgumentNullException.ThrowIfNull(mods);

		List<string> enabled = [];

		foreach (string mod in mods) {
			string source = Resolve(mod, modsDirectory);
			string name = Path.GetFileNameWithoutExtension(source);

			File.Copy(source, Path.Combine(ModsDirectory, name + ".tmod"), overwrite: true);

			if (!enabled.Contains(name))
				enabled.Add(name);
		}

		if (!enabled.Contains(Runtime)) {
			string source = Resolve(Runtime, modsDirectory);

			File.Copy(source, Path.Combine(ModsDirectory, Runtime + ".tmod"), overwrite: true);
			enabled.Insert(0, Runtime);
		}

		WriteEnabledJson(enabled);

		return enabled;
	}

	/// <summary>
	/// Writes <c>enabled.json</c>, which is all that enabling a mod takes.
	/// <para/>
	/// A plain list of names (<c>ModOrganizer.cs:748</c>), so no Steam, no
	/// Workshop, and no in-game UI is involved in setting a run up.
	/// </summary>
	public void WriteEnabledJson(IEnumerable<string> names)
	{
		ArgumentNullException.ThrowIfNull(names);

		// Hand-written rather than through a serializer: the file is a list of
		// strings, and mod names cannot contain a quote or a backslash, since
		// they are also folder names.
		string body = string.Join(",", names.Select(name => $"\"{name}\""));

		File.WriteAllText(Path.Combine(ModsDirectory, "enabled.json"), $"[{body}]\n");
	}

	private static string Resolve(string mod, string? modsDirectory)
	{
		if (mod.EndsWith(".tmod", StringComparison.OrdinalIgnoreCase)) {
			if (File.Exists(mod))
				return Path.GetFullPath(mod);

			throw new FileNotFoundException($"No .tmod at '{mod}'.", mod);
		}

		if (modsDirectory is null)
			throw new FileNotFoundException(
				$"Cannot look up mod '{mod}': no mods directory found. Pass --mods-dir, set {Installation.ModsVariable}, or give the path to the .tmod itself.",
				mod);

		string candidate = Path.Combine(modsDirectory, mod + ".tmod");

		if (File.Exists(candidate))
			return candidate;

		throw new FileNotFoundException(
			$"No '{mod}.tmod' in '{modsDirectory}'. Build the mod first, or pass the path to its .tmod.",
			candidate);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (keep || !Directory.Exists(Root))
			return;

		try {
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException) {
			// A leftover temp directory is untidy; failing a run that has
			// already reported its verdict because of one would be worse.
		}
	}
}
