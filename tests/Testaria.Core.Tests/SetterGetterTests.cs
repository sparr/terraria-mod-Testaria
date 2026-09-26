using System.Globalization;
using TAssert = Testaria.Assert;

namespace Testaria.Tests;

/// <summary>
/// <see cref="Testaria.Assert.SettersAcceptTheirOwnGetters"/>, which asks one
/// question of every property a type has: does writing back what was just read
/// throw.
/// <para/>
/// The shapes below are reduced from the defect that prompted it, found in a
/// mod nobody here wrote: a property rendering an unset value as the empty
/// string and parsing with a method that throws on one.
/// </summary>
public class SetterGetterTests
{
	/// <summary>The defect, reduced: empty out, and empty not accepted back.</summary>
	private sealed class RendersEmptyAndCannotParseIt
	{
		public float? Value { get; set; }

		public string Text {
			get => Value is null ? string.Empty : Value.Value.ToString(CultureInfo.InvariantCulture);
			set => Value = float.Parse(value, CultureInfo.InvariantCulture);
		}
	}

	/// <summary>The same property written defensively, which is the fix.</summary>
	private sealed class RendersEmptyAndAcceptsIt
	{
		public float? Value { get; set; }

		public string Text {
			get => Value is null ? string.Empty : Value.Value.ToString(CultureInfo.InvariantCulture);
			set => Value = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
				? parsed
				: null;
		}
	}

	private sealed class Ordinary
	{
		public int Number { get; set; }
		public string Name { get; set; } = string.Empty;
		public bool Flag { get; set; }
	}

	[Fact]
	public void A_type_whose_properties_take_their_own_values_passes()
		=> TAssert.SettersAcceptTheirOwnGetters(new Ordinary());

	[Fact]
	public void The_defensive_version_of_the_defect_passes()
		=> TAssert.SettersAcceptTheirOwnGetters(new RendersEmptyAndAcceptsIt());

	[Fact]
	public void A_setter_that_throws_on_its_own_getter_is_caught()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersAcceptTheirOwnGetters(new RendersEmptyAndCannotParseIt()));

		XAssert.Contains("Text", thrown.Message);
	}

	/// <summary>
	/// The reported exception is the one the property threw, not reflection's
	/// wrapper, which names nothing useful.
	/// </summary>
	[Fact]
	public void The_message_names_the_real_exception()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersAcceptTheirOwnGetters(new RendersEmptyAndCannotParseIt()));

		XAssert.Contains("FormatException", thrown.Message);
		XAssert.DoesNotContain("TargetInvocationException", thrown.Message);
	}

	private sealed class TwoBadProperties
	{
		public string First {
			get => string.Empty;
			set => throw new InvalidOperationException("first");
		}

		public string Second {
			get => string.Empty;
			set => throw new InvalidOperationException("second");
		}
	}

	/// <summary>
	/// Every property is tried before anything is reported, so the first bad
	/// one does not hide the rest.
	/// </summary>
	[Fact]
	public void All_offending_properties_are_reported_together()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersAcceptTheirOwnGetters(new TwoBadProperties()));

		XAssert.Contains("First", thrown.Message);
		XAssert.Contains("Second", thrown.Message);
		XAssert.Contains("2 properties could not take their own values", thrown.Message);
	}

	private sealed class GetterThrows
	{
		public string Bad {
			get => throw new InvalidOperationException("reading is broken");
			set { }
		}
	}

	/// <summary>A getter that throws is reported too, and named as the getter.</summary>
	[Fact]
	public void A_getter_that_throws_is_reported()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.SettersAcceptTheirOwnGetters(new GetterThrows()));

		XAssert.Contains("reading it threw", thrown.Message);
	}

	private sealed class NotWritable
	{
		public string ReadOnly => throw new InvalidOperationException("never reached: there is no setter to test");
		public string PrivateSetter { get; private set; } = string.Empty;
	}

	/// <summary>
	/// Only public read-write properties are touched. A read-only property has
	/// no setter to test, and one whose setter is private cannot be reached by
	/// the interface code this is about.
	/// </summary>
	[Fact]
	public void Properties_without_a_public_setter_are_left_alone()
		=> TAssert.SettersAcceptTheirOwnGetters(new NotWritable());

	private sealed class HasAnIndexer
	{
		public string this[int index] {
			get => throw new InvalidOperationException("an indexer has no value to round trip");
			set => throw new InvalidOperationException("an indexer has no value to round trip");
		}

		public int Number { get; set; }
	}

	/// <summary>An indexer is skipped: there is no argument to give it.</summary>
	[Fact]
	public void An_indexer_is_skipped()
		=> TAssert.SettersAcceptTheirOwnGetters(new HasAnIndexer());

	[Fact]
	public void A_null_subject_fails_rather_than_throwing_a_null_reference()
		=> XAssert.Throws<AssertionException>(
			() => TAssert.SettersAcceptTheirOwnGetters(null!));

	/// <summary>
	/// It registers as having asserted something, which is what stops a test
	/// whose only check is this one from being reported as having checked
	/// nothing. Not an exact count: like <c>Empty</c>, it reaches another
	/// assertion on the way, and only zero against non-zero is meaningful.
	/// </summary>
	[Fact]
	public void It_counts_as_an_assertion()
	{
		int before = TAssert.Invocations;

		TAssert.SettersAcceptTheirOwnGetters(new Ordinary());

		XAssert.True(TAssert.Invocations > before);
	}
}
