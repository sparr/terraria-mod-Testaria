namespace Testaria;

/// <summary>
/// Where test results are written.
/// <para/>
/// Always under the tModLoader save directory. <c>ModUploadRules.md</c> rule 2
/// forbids a published mod from touching anything outside the save and config
/// directories, and designing to that from the start keeps Workshop
/// publication available even if it is never used. A CI harness copies the
/// results out afterwards rather than the mod writing wherever it likes.
/// </summary>
public static class ResultsLocation
{
	/// <summary>Subdirectory of the save path that results live in.</summary>
	public const string DirectoryName = "Testaria";

	/// <summary>The results directory for a given save path.</summary>
	public static string Directory(string saveDirectory)
	{
		ArgumentException.ThrowIfNullOrEmpty(saveDirectory);

		return Path.Combine(saveDirectory, DirectoryName);
	}

	/// <summary>
	/// The JUnit XML path for a run. The run name is sanitized, since it comes
	/// from a mod name or a command line and can contain anything.
	/// </summary>
	public static string ForRun(string saveDirectory, string runName, string extension = ".xml")
	{
		ArgumentException.ThrowIfNullOrEmpty(extension);

		if (!extension.StartsWith('.'))
			throw new ArgumentException($"Extension must start with a dot, got '{extension}'.", nameof(extension));

		return Path.Combine(Directory(saveDirectory), PortableFileName.MakeSafe(runName) + extension);
	}
}
