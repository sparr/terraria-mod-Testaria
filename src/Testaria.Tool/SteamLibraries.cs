using System.Text.RegularExpressions;

namespace Testaria.Tool;

/// <summary>
/// The Steam library folders on this machine, read from Steam's own index.
/// <para/>
/// Steam installs games into any number of library folders, on any number of
/// drives, and records them in <c>steamapps/libraryfolders.vdf</c> under the
/// primary install. Probing a fixed list of three or four paths finds only the
/// primary one, so a tModLoader installed anywhere else looks, to a tool that
/// only probes, exactly like a tModLoader that is not installed.
/// <para/>
/// This is a common enough trap that it was hit twice over in one afternoon:
/// once here, and once independently in a third-party mod build SDK, on the
/// same machine, for the same reason.
/// </summary>
public static partial class SteamLibraries
{
	/// <summary>The file Steam keeps its library list in, relative to a Steam root.</summary>
	public const string IndexFile = "steamapps/libraryfolders.vdf";

	/// <summary>
	/// Pulls the library paths out of a <c>libraryfolders.vdf</c>.
	/// <para/>
	/// VDF is a small nested key/value format; rather than implement it, this
	/// takes the one key it needs. Every library entry carries exactly one
	/// <c>"path"</c>, so the extraction is unambiguous even though the parse
	/// is not a parse.
	/// </summary>
	public static IReadOnlyList<string> Parse(string? vdf)
	{
		if (string.IsNullOrWhiteSpace(vdf))
			return [];

		List<string> paths = [];

		foreach (Match match in PathEntry().Matches(vdf)) {
			// VDF escapes backslashes, which matters on Windows where the
			// paths are written "D:\\SteamLibrary".
			string path = match.Groups[1].Value.Replace(@"\\", @"\");

			if (!string.IsNullOrWhiteSpace(path))
				paths.Add(path);
		}

		return paths;
	}

	/// <summary>
	/// Every library folder Steam knows about, given the Steam roots to look
	/// under. Unreadable or absent index files are simply skipped: this is a
	/// convenience over the fixed candidates, never a reason to fail.
	/// </summary>
	public static IEnumerable<string> Discover(IEnumerable<string> steamRoots)
	{
        ArgumentNullException.ThrowIfNull(steamRoots);

		HashSet<string> seen = new(StringComparer.Ordinal);

		foreach (string root in steamRoots) {
			if (string.IsNullOrWhiteSpace(root))
				continue;

			string index = Path.Combine(root, IndexFile.Replace('/', Path.DirectorySeparatorChar));
			string text;

			try {
				if (!File.Exists(index))
					continue;

				text = File.ReadAllText(index);
			}
			catch (IOException) {
				continue;
			}
			catch (UnauthorizedAccessException) {
				continue;
			}

			foreach (string library in Parse(text)) {
				if (seen.Add(library))
					yield return library;
			}
		}
	}

	/// <summary>Where an app's files sit inside a library folder.</summary>
	public static string AppDirectory(string library, string folderName)
	{
		ArgumentException.ThrowIfNullOrEmpty(library);
		ArgumentException.ThrowIfNullOrEmpty(folderName);

		return Path.Combine(library, "steamapps", "common", folderName);
	}

	[GeneratedRegex("\"path\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase)]
	private static partial Regex PathEntry();
}
