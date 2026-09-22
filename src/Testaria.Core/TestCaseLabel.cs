using System.Globalization;
using System.Text;

namespace Testaria;

/// <summary>
/// Renders a case's arguments into something a person can find in a report.
/// <para/>
/// A parameterised test that reports only its method name is barely better
/// than a loop: the report names a test rather than the case that failed. The
/// label is what makes each case addressable, both to read and to filter for.
/// </summary>
public static class TestCaseLabel
{
	/// <summary>Longest a single rendered argument may be before it is cut.</summary>
	public const int MaxArgumentLength = 40;

	/// <summary>Renders arguments as "(a, b)", or empty for no arguments.</summary>
	public static string For(IReadOnlyList<object?> arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);

		if (arguments.Count == 0)
			return string.Empty;

		var builder = new StringBuilder("(");

		for (int i = 0; i < arguments.Count; i++) {
			if (i > 0)
				builder.Append(", ");

			builder.Append(Render(arguments[i]));
		}

		return builder.Append(')').ToString();
	}

	private static string Render(object? value)
	{
		string text = value switch {
			null => "null",
			string s => $"\"{s}\"",
			bool b => b ? "true" : "false",
			IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString() ?? "null",
		};

		// Long values make a report unreadable and a filter unusable, and the
		// distinguishing part of a name is almost always near its start.
		return text.Length <= MaxArgumentLength
			? text
			: string.Concat(text.AsSpan(0, MaxArgumentLength - 3), "...");
	}
}
