using TAssert = Testaria.Assert;

namespace Testaria.Tests;

/// <summary>
/// <see cref="Testaria.Assert.SettersSettleAfterOneWrite"/>, which allows a
/// property to change the value once and not twice.
/// <para/>
/// The sibling of <see cref="SetterGetterTests"/>, and about a different
/// fault: not a property that throws, but one whose value never comes to rest.
/// </summary>
public class SetterSettleTests
{
	/// <summary>
	/// Normalization, which is allowed: the first write reformats, the second
	/// finds nothing left to change.
	/// </summary>
	private sealed class NormalizesOnce
	{
		private int seconds;

		public string Text {
			get => $"{seconds / 60}:{seconds % 60:00}";
			set {
				string[] parts = value.Split(':');
				if (parts.Length == 2 && int.TryParse(parts[0], out int m) && int.TryParse(parts[1], out int s))
					seconds = m * 60 + s;
			}
		}
	}

	/// <summary>The fault: every write appends, so the value walks away.</summary>
	private sealed class NeverSettles
	{
		private string stored = string.Empty;

		public string Text {
			get => stored;
			set => stored = value + "!";
		}
	}

	/// <summary>
	/// The subtler version of the same fault: it escapes what it is given, so
	/// each pass doubles the escaping rather than lengthening by a constant.
	/// </summary>
	private sealed class EscapesEveryTime
	{
		private string stored = "a&b";

		public string Text {
			get => stored;
			set => stored = value.Replace("&", "&amp;");
		}
	}

	private sealed class Ordinary
	{
		public int Number { get; set; } = 3;
		public string Name { get; set; } = "unchanged";
	}

	[Fact]
	public void A_property_that_does_not_change_its_value_settles()
		=> TAssert.SettersSettleAfterOneWrite(new Ordinary());

	/// <summary>
	/// A property may reformat what it is given. "2:5" becoming "2:05" is the
	/// case this assertion deliberately permits, and the reason it looks at the
	/// second write rather than the first.
	/// </summary>
	[Fact]
	public void Normalizing_once_is_allowed()
	{
		var subject = new NormalizesOnce { Text = "2:5" };

		XAssert.Equal("2:05", subject.Text);

		TAssert.SettersSettleAfterOneWrite(subject);
	}

	[Fact]
	public void A_value_that_grows_on_every_write_is_caught()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersSettleAfterOneWrite(new NeverSettles()));

		XAssert.Contains("Text", thrown.Message);
		XAssert.Contains("never settles", thrown.Message);
	}

	/// <summary>The message shows all three values, which is what names the drift.</summary>
	[Fact]
	public void The_message_shows_what_it_became_each_time()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersSettleAfterOneWrite(new NeverSettles()));

		XAssert.Contains("started as \"\"", thrown.Message);
		XAssert.Contains("became \"!\"", thrown.Message);
		XAssert.Contains("\"!!\" after a second", thrown.Message);
	}

	[Fact]
	public void Repeated_escaping_is_caught()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersSettleAfterOneWrite(new EscapesEveryTime()));

		XAssert.Contains("never settles", thrown.Message);
	}

	/// <summary>
	/// The whole message, exactly, because the README quotes this shape and a
	/// documented example that has drifted from the code is worse than none.
	/// </summary>
	[Fact]
	public void The_message_reads_as_documented()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersSettleAfterOneWrite(new EscapesEveryTime()));

		XAssert.Equal(
			"Assert.SettersSettleAfterOneWrite() Failure\n"
			+ "On a EscapesEveryTime, 1 property does not settle:\n"
			+ "  Text: started as \"a&b\", became \"a&amp;b\" after one write, "
			+ "and \"a&amp;amp;b\" after a second, so it never settles",
			thrown.Message);
	}

	private sealed class SetterThrows
	{
		public string Text {
			get => string.Empty;
			set => throw new InvalidOperationException("cannot take it");
		}
	}

	/// <summary>
	/// A property that cannot be written is a skip, not a failure.
	/// <para/>
	/// Whether a property throws is what <c>SettersAcceptTheirOwnGetters</c>
	/// asks, and it will say so. This asks only whether a value that can be
	/// written comes to rest, and one that cannot be written has no answer
	/// either way. Reporting it in both places made one defect arrive as two
	/// failures saying different things about it. Skipped rather than passed,
	/// because a silent pass would claim it had been checked.
	/// </summary>
	[Fact]
	public void A_property_that_cannot_be_written_is_skipped_rather_than_failed()
	{
		SkipTestException skipped = XAssert.Throws<SkipTestException>(
			() => TAssert.SettersSettleAfterOneWrite(new SetterThrows()));

		XAssert.Contains("could not be read and written", skipped.Message);
		XAssert.Contains("InvalidOperationException", skipped.Message);
	}

	private sealed class OneUnwritableAndOneDrifting
	{
		private string drifts = string.Empty;

		public string Unwritable {
			get => string.Empty;
			set => throw new InvalidOperationException("will not take it");
		}

		public string Drifts {
			get => drifts;
			set => drifts = value + "x";
		}
	}

	/// <summary>A property that cannot be written does not hide one that drifts.</summary>
	[Fact]
	public void An_unwritable_property_does_not_hide_a_drifting_one()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersSettleAfterOneWrite(new OneUnwritableAndOneDrifting()));

		XAssert.Contains("Drifts", thrown.Message);
	}

	[Fact]
	public void A_null_subject_fails_rather_than_throwing_a_null_reference()
		=> XAssert.Throws<AssertionException>(
			() => TAssert.SettersSettleAfterOneWrite(null!));

	[Fact]
	public void It_counts_as_an_assertion()
	{
		int before = TAssert.Invocations;

		TAssert.SettersSettleAfterOneWrite(new Ordinary());

		XAssert.True(TAssert.Invocations > before);
	}

	private sealed class TwoDrifters
	{
		private string one = string.Empty;
		private string two = string.Empty;

		public string First {
			get => one;
			set => one = value + "a";
		}

		public string Second {
			get => two;
			set => two = value + "b";
		}
	}

	[Fact]
	public void All_drifting_properties_are_reported_together()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersSettleAfterOneWrite(new TwoDrifters()));

		XAssert.Contains("First", thrown.Message);
		XAssert.Contains("Second", thrown.Message);
		XAssert.Contains("2 properties do not settle", thrown.Message);
	}
}
