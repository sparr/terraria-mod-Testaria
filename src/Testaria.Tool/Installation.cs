using System.Runtime.InteropServices;

namespace Testaria.Tool;

/// <summary>
/// Where tModLoader is, and where the mods it has built are.
/// <para/>
/// The same knowledge the repository's shell scripts carry in
/// <c>scripts/paths.sh</c>, in a form that works on Windows too. A tool that
/// only runs where bash does would not be much of a toolchain.
/// </summary>
public static class Installation
{
	/// <summary>Environment variable naming a tModLoader install.</summary>
	public const string PathVariable = "TML_PATH";

	/// <summary>Environment variable naming the directory built mods land in.</summary>
	public const string ModsVariable = "MODS_SRC";

	/// <summary>The file that tells an install apart from any other directory.</summary>
	public const string Marker = "tModLoader.dll";

	/// <summary>
	/// Finds a tModLoader install: the explicit path, then the environment
	/// variable, then the platform's usual Steam library.
	/// </summary>
	/// <param name="explicitPath">The <c>--tml</c> argument, if one was given.</param>
	/// <returns>The install directory, or null if none of the candidates holds one.</returns>
	public static string? FindTml(string? explicitPath = null)
	{
		foreach (string? candidate in Candidates(explicitPath)) {
			if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(Path.Combine(candidate, Marker)))
				return candidate;
		}

		return null;
	}

	/// <summary>Every place an install is looked for, in order, for an error message that can name them.</summary>
	public static IEnumerable<string> Candidates(string? explicitPath = null)
	{
		if (explicitPath is not null)
			yield return explicitPath;

		if (Environment.GetEnvironmentVariable(PathVariable) is string fromEnvironment)
			yield return fromEnvironment;

		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			yield return @"C:\Program Files (x86)\Steam\steamapps\common\tModLoader";
			yield return @"C:\Program Files\Steam\steamapps\common\tModLoader";
			yield break;
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			yield return Path.Combine(home, "Library/Application Support/Steam/steamapps/common/tModLoader");
			yield break;
		}

		yield return Path.Combine(home, ".local/share/Steam/steamapps/common/tModLoader");
		yield return Path.Combine(home, ".steam/steam/steamapps/common/tModLoader");
	}

	/// <summary>
	/// Finds the directory a mod build leaves its <c>.tmod</c> in.
	/// <para/>
	/// The dev save path first, because the 1.4.5 line is currently only
	/// available as a dev build, then the stable and preview paths beside it.
	/// </summary>
	public static string? FindModsDirectory(string? explicitPath = null)
	{
		foreach (string? candidate in ModsCandidates(explicitPath)) {
			if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
				return candidate;
		}

		return null;
	}

	/// <summary>Every place built mods are looked for, in order.</summary>
	public static IEnumerable<string> ModsCandidates(string? explicitPath = null)
	{
		if (explicitPath is not null)
			yield return explicitPath;

		if (Environment.GetEnvironmentVariable(ModsVariable) is string fromEnvironment)
			yield return fromEnvironment;

		foreach (string purpose in new[] { "tModLoader-dev", "tModLoader-preview", "tModLoader" })
			yield return Path.Combine(SavePath(), purpose, "Mods");
	}

	/// <summary>
	/// tModLoader's own save root for this platform, which is where its
	/// per-purpose directories live.
	/// </summary>
	public static string SavePath()
	{
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Terraria");

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			return Path.Combine(home, "Library/Application Support/Terraria");

		return Path.Combine(home, ".local/share/Terraria");
	}
}
