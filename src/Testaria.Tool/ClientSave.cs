using System.Text;

namespace Testaria.Tool;

/// <summary>
/// The configuration a client needs before it will get as far as loading a mod.
/// <para/>
/// A tModLoader client given a fresh save directory does not reach its menu. It
/// stops at "Select language", and then at "Welcome to tModLoader", and then at
/// a change-notes dialog, each waiting for a click that is never coming. None
/// of them can be suppressed by a mod, because they stand in front of mod
/// loading: the mod that would silence them has not been loaded yet when they
/// appear.
/// <para/>
/// All three are decided from the configuration file, so the harness writes one
/// before the client starts. The logs name none of them: the screen a client is
/// waiting on is visible only in its framebuffer.
/// </summary>
public static class ClientSave
{
	/// <summary>
	/// A version far enough ahead that the loader believes this install has
	/// been launched before, which is what the first-run welcome checks.
	/// </summary>
	public const string SeenVersion = "9999.0";

	/// <summary>Writes the configuration into a client's save directory.</summary>
	/// <param name="saveDirectory">The client's save directory.</param>
	/// <param name="tmlPath">The install, read for the commit the build was made from.</param>
	public static void WriteConfig(string saveDirectory, string tmlPath)
	{
		ArgumentException.ThrowIfNullOrEmpty(saveDirectory);

		var json = new StringBuilder();

		json.AppendLine("{");
		// Without this the client stops at the language chooser, which is
		// decided in Main before any mod exists.
		json.AppendLine("  \"Language\": \"en-US\",");
		json.AppendLine($"  \"LastLaunchedTModLoaderVersion\": \"{SeenVersion}\",");
		json.AppendLine($"  \"LastPreviewFreezeNotificationSeen\": \"{SeenVersion}\",");
		// ShowWhatsNew compares the build's commit against this, and a dev
		// build with no match opens a change-notes dialog over the menu.
		json.AppendLine($"  \"LastLaunchedTModLoaderAlphaSha\": \"{CurrentCommit(tmlPath)}\",");
		json.AppendLine("  \"SeenFirstLaunchModderWelcomeMessage\": true,");
		json.AppendLine("  \"WarnedFamilyShareDontShowAgain\": true,");
		json.AppendLine("  \"ShowModMenuNotifications\": false,");
		json.AppendLine("  \"ShowNewUpdatedModsInfo\": false,");
		json.AppendLine("  \"Fullscreen\": false,");
		json.AppendLine("  \"DisplayWidth\": 800,");
		json.AppendLine("  \"DisplayHeight\": 600,");
		json.AppendLine("  \"AutoSave\": false,");
		json.AppendLine("  \"VolumeSound\": 0.0,");
		json.AppendLine("  \"VolumeAmbient\": 0.0,");
		json.AppendLine("  \"VolumeMusic\": 0.0");
		json.AppendLine("}");

		File.WriteAllText(Path.Combine(saveDirectory, "config.json"), json.ToString());
	}

	/// <summary>
	/// The commit this install was built from, taken from the commit list it
	/// ships. Falls back to a placeholder, which costs only a dialog on a dev
	/// build.
	/// </summary>
	public static string CurrentCommit(string tmlPath)
	{
		string path = Path.Combine(tmlPath, "RecentGitHubCommits.txt");

		if (!File.Exists(path))
			return "unknown";

		using var reader = new StreamReader(path);
		string? first = reader.ReadLine();

		if (string.IsNullOrWhiteSpace(first))
			return "unknown";

		return first.Split(' ', 2)[0];
	}
}
