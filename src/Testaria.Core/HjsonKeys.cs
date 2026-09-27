using System.Text.RegularExpressions;

namespace Testaria;

/// <summary>
/// The key paths in a localization file, and which of a translation's keys can
/// never be reached.
/// <para/>
/// This exists because of an ordering in tModLoader that makes one kind of
/// mistake completely silent. <c>LocalizationLoader.Autoload</c> registers a
/// mod's keys from its <c>en-US</c> file and no other, and
/// <c>LoadModTranslations</c> then walks every culture calling
/// <c>UpdateTextValue</c>, which, in its own comment, "can only set the value
/// of existing keys. Cannot register new keys." So a key that appears in a
/// translated file but not in the English one is never registered, the
/// translator's line is never reachable, and nothing warns at load or in play.
/// <para/>
/// Game-independent on purpose, so it lives here rather than with the sweeps: a
/// localization file is text, the question is about two sets of strings, and
/// neither needs a loaded game. That also makes it the one part of this work a
/// tier 0 test can cover directly.
/// </summary>
public static class HjsonKeys
{
	/// <summary>
	/// The dotted key paths a localization file declares.
	/// <para/>
	/// Deliberately a small parser rather than a real one, and the limits are
	/// worth stating because they decide what this may be used for. It tracks
	/// nesting by braces, treats <c>key:</c> as a declaration, ignores
	/// whole-line comments, and skips the contents of <c>'''</c> blocks. It
	/// does not evaluate values, so it is fit for comparing which keys two
	/// files declare and unfit for anything about what they say.
	/// <para/>
	/// The BOM handling is not incidental. Several files in the surveyed corpus
	/// begin with a UTF-8 byte order mark, and a first pass at this comparison
	/// that did not strip it failed to recognise the first line of every such
	/// file as an opening brace, mis-nested every key beneath it, and reported
	/// four keys as unreachable in a mod where all four were fine. Callers
	/// reading from disk want <c>utf-8-sig</c> or the equivalent; this strips a
	/// leading mark from the string as well, so that a caller who forgets is
	/// not silently given wrong answers.
	/// </summary>
	public static IReadOnlySet<string> Paths(string text)
	{
		HashSet<string> found = [];

		if (string.IsNullOrEmpty(text))
			return found;

		List<string> stack = [];
		bool inBlock = false;

		foreach (string rawLine in text.TrimStart('﻿').Split('\n')) {
			string line = rawLine.Trim('\r', '﻿').Trim();

			if (line.Length == 0)
				continue;

			// A whole-line comment declares nothing, which is the case that
			// matters: a commented-out entry must stop counting as a key.
			// Trailing comments are deliberately not stripped, because a value
			// may contain "//" and cutting there would miscount the fences
			// below. A key is left of its colon, so trailing text is harmless.
			if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('#'))
				continue;

			bool fenced = Occurrences(line, Fence) % 2 != 0;

			// Inside a fenced block nothing declares a key, and the contents
			// may well hold braces and colons of their own.
			if (inBlock) {
				if (fenced)
					inBlock = false;

				continue;
			}

			if (line.StartsWith('}')) {
				if (stack.Count > 0)
					stack.RemoveAt(stack.Count - 1);

				continue;
			}

			Match opens = Opening.Match(line);

			if (opens.Success) {
				stack.Add(opens.Groups[1].Value);
				continue;
			}

			// The declaration is read before the fence is acted on, so that a
			// key whose value opens or completes a block on the same line still
			// counts. Both spellings appear in the corpus: a value fenced on
			// one line, and a fence opening a block for the lines beneath it. A
			// first pass skipped any line containing a fence outright, lost the
			// one-line spelling, and then reported that key as unreachable in
			// two translations where it was perfectly correct.
			Match declares = Declaration.Match(line);

			if (declares.Success)
				found.Add(string.Join('.', stack.Append(declares.Groups[1].Value)));

			if (fenced)
				inBlock = true;
		}

		return found;
	}

	/// <summary>
	/// The keys a translation declares that its English counterpart does not,
	/// and which therefore can never be registered.
	/// <para/>
	/// One-directional by design. A key in English and absent from a
	/// translation is an untranslated string, which renders in English and is
	/// correct; only the reverse is unreachable work.
	/// </summary>
	public static IReadOnlySet<string> Unreachable(string english, string translated)
	{
		IReadOnlySet<string> baseline = Paths(english);

		return Paths(translated).Where(key => !baseline.Contains(key)).ToHashSet();
	}

	/// <summary>The marker hjson uses around a multi-line value.</summary>
	private const string Fence = "'''";

	/// <summary>A key opening a nested block: <c>Items: {</c>.</summary>
	private static readonly Regex Opening = new(@"^([A-Za-z0-9_$.\-]+)\s*:\s*\{\s*$", RegexOptions.Compiled);

	/// <summary>A key with a value on the same line: <c>DisplayName: Foo</c>.</summary>
	private static readonly Regex Declaration = new(@"^([A-Za-z0-9_$.\-]+)\s*:", RegexOptions.Compiled);

	private static int Occurrences(string line, string token)
	{
		int count = 0;

		for (int at = line.IndexOf(token, StringComparison.Ordinal); at >= 0;
			at = line.IndexOf(token, at + token.Length, StringComparison.Ordinal)) {
			count++;
		}

		return count;
	}
}
