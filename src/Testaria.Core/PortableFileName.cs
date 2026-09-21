using System.Text;

namespace Testaria;

/// <summary>
/// Turns arbitrary text into a file name that is valid on every OS Terraria
/// runs on.
/// <para/>
/// Deliberately does <b>not</b> use <see cref="Path.GetInvalidFileNameChars"/>,
/// because that method answers for the current OS only. On Linux it returns
/// just two characters, the null byte and the forward slash; on Windows it
/// returns about forty, including <c>&lt; &gt; : " | ? *</c>. A runner using it
/// would happily write a file on Linux that Windows cannot open, which is
/// exactly the shape of bug that survives all the way to a CI artifact a
/// Windows developer then cannot download.
/// <para/>
/// So the Windows rules are applied everywhere: they are the strictest, and a
/// name safe under them is safe under all of them.
/// </summary>
public static class PortableFileName
{
	/// <summary>
	/// Longest component this produces. Well under the 255 byte limit common
	/// to Linux and macOS filesystems, and well under Windows' 260 character
	/// full-path limit once a save directory is prepended.
	/// </summary>
	public const int MaxComponentLength = 120;

	/// <summary>Used when sanitizing leaves nothing behind.</summary>
	public const string Fallback = "_";

	private static readonly char[] AlwaysInvalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

	private static readonly string[] ReservedDeviceNames = [
		"CON", "PRN", "AUX", "NUL",
		"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
		"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
	];

	/// <summary>
	/// Whether a name is one of the MS-DOS device names Windows still reserves.
	/// <para/>
	/// The reservation applies with any extension too, so <c>CON.xml</c> is
	/// just as unusable as <c>CON</c>, which is why only the part before the
	/// first dot is checked.
	/// </summary>
	public static bool IsReservedDeviceName(string name)
	{
		if (string.IsNullOrEmpty(name))
			return false;

		int dot = name.IndexOf('.');
		ReadOnlySpan<char> stem = dot >= 0 ? name.AsSpan(0, dot) : name.AsSpan();

		foreach (string reserved in ReservedDeviceNames) {
			if (stem.Equals(reserved.AsSpan(), StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Rewrites <paramref name="name"/> into a portable file name component.
	/// Does not accept or produce directory separators; combine with
	/// <see cref="Path.Combine(string, string)"/> afterwards.
	/// </summary>
	public static string MakeSafe(string? name, char replacement = '_')
	{
		if (Array.IndexOf(AlwaysInvalid, replacement) >= 0 || char.IsControl(replacement))
			throw new ArgumentException($"Replacement character '{replacement}' is itself unsafe.", nameof(replacement));

		if (string.IsNullOrEmpty(name))
			return Fallback;

		var builder = new StringBuilder(name.Length);

		foreach (char c in name)
			builder.Append(char.IsControl(c) || Array.IndexOf(AlwaysInvalid, c) >= 0 ? replacement : c);

		string result = Truncate(builder.ToString(), MaxComponentLength);

		// Windows silently strips trailing dots and spaces, so a name ending
		// in one would not round-trip: you would write "run." and find "run".
		result = result.TrimEnd('.', ' ').TrimStart(' ');

		if (result.Length == 0)
			return Fallback;

		return IsReservedDeviceName(result) ? replacement + result : result;
	}

	/// <summary>
	/// Makes a set of names unique <i>case-insensitively</i>, appending a
	/// counter where needed.
	/// <para/>
	/// Case insensitively because Windows and macOS default to case-insensitive
	/// filesystems while Linux does not. Two tests differing only in case would
	/// produce one file on a developer's Mac and two in Linux CI, and the
	/// resulting disagreement is miserable to diagnose.
	/// </summary>
	public static IReadOnlyList<string> MakeUnique(IEnumerable<string> names)
	{
		ArgumentNullException.ThrowIfNull(names);

		var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		List<string> unique = [];

		foreach (string name in names) {
			string candidate = MakeSafe(name);

			if (seen.TryGetValue(candidate, out int count)) {
				string next;
				do {
					next = Truncate(candidate, MaxComponentLength - 8) + "_" + (++count).ToString(System.Globalization.CultureInfo.InvariantCulture);
				}
				while (seen.ContainsKey(next));

				seen[candidate] = count;
				seen[next] = 0;
				unique.Add(next);
			}
			else {
				seen[candidate] = 0;
				unique.Add(candidate);
			}
		}

		return unique;
	}

	/// <summary>
	/// Truncates without splitting a surrogate pair, which would leave a lone
	/// surrogate in a file name and produce mojibake or an outright error
	/// depending on the filesystem.
	/// </summary>
	private static string Truncate(string value, int max)
	{
		if (value.Length <= max)
			return value;

		int cut = max;
		if (char.IsHighSurrogate(value[cut - 1]))
			cut--;

		return value[..cut];
	}
}
