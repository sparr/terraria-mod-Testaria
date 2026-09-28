namespace Testaria.Tests;

/// <summary>
/// <see cref="SweptProperties"/>, which decides both what the property checks
/// ask about and when one subject's answer stands for another's.
/// <para/>
/// The shapes below are the ones a real sweep meets: a mod's base class
/// holding an auto-property that a family of generated types inherits, and the
/// same base also exposing something with a body, which is what stops the
/// family being collapsed under the reading check.
/// </summary>
public class SweptPropertiesTests
{
	private abstract class Generated
	{
		public ModSideStandIn Side { get; set; }

		public string Computed => Side.ToString();
	}

	private enum ModSideStandIn { Both, Client }

	private sealed class OneOfTheFamily : Generated;

	private sealed class AnotherOfTheFamily : Generated;

	private sealed class SaysSomethingItself : Generated
	{
		public int Own { get; set; }
	}

	private sealed class Bodied : Generated
	{
		private int stored;

		public new int Computed { get; set; }

		public int Validated {
			get => stored;
			set => stored = value < 0 ? 0 : value;
		}
	}

	private sealed class NothingToAsk;

	[Fact]
	public void An_auto_property_is_recognized_as_one()
		=> XAssert.True(SweptProperties.IsAutoImplemented(
			typeof(Generated).GetProperty(nameof(Generated.Side))!));

	[Fact]
	public void A_property_with_a_body_is_not()
		=> XAssert.False(SweptProperties.IsAutoImplemented(
			typeof(Bodied).GetProperty(nameof(Bodied.Validated))!));

	/// <summary>
	/// The collapsible case: everything asked about is inherited, and none of
	/// it can behave differently for one subject than another.
	/// </summary>
	[Fact]
	public void A_type_adding_only_inherited_auto_properties_has_a_signature()
	{
		string? signature = SweptProperties.InheritedAutoSignature(
			typeof(OneOfTheFamily), PropertyScope.RoundTrippable);

		XAssert.NotNull(signature);
		XAssert.Contains(nameof(Generated.Side), signature);
	}

	/// <summary>
	/// And two of them agree, which is what lets one answer for the other.
	/// </summary>
	[Fact]
	public void Two_types_inheriting_the_same_properties_share_a_signature()
		=> XAssert.Equal(
			SweptProperties.InheritedAutoSignature(typeof(OneOfTheFamily), PropertyScope.RoundTrippable),
			SweptProperties.InheritedAutoSignature(typeof(AnotherOfTheFamily), PropertyScope.RoundTrippable));

	[Fact]
	public void A_type_declaring_a_property_itself_has_none()
		=> XAssert.Null(SweptProperties.InheritedAutoSignature(
			typeof(SaysSomethingItself), PropertyScope.RoundTrippable));

	/// <summary>
	/// The condition that keeps the collapse honest. A body can answer
	/// differently depending on the value the subject holds, so a subject
	/// carrying one cannot be spoken for.
	/// </summary>
	[Fact]
	public void A_type_whose_inherited_properties_have_bodies_has_none()
		=> XAssert.Null(SweptProperties.InheritedAutoSignature(
			typeof(Bodied), PropertyScope.RoundTrippable));

	/// <summary>
	/// Scope decides it, and this is the real corpus case: the family's
	/// writable property is an auto-property, so the round trip collapses,
	/// while the same base also exposes a read-only property with a body, so
	/// the reading check keeps asking every subject.
	/// </summary>
	[Fact]
	public void A_readable_body_stops_the_collapse_for_reading_only()
	{
		XAssert.NotNull(SweptProperties.InheritedAutoSignature(
			typeof(OneOfTheFamily), PropertyScope.RoundTrippable));

		XAssert.Null(SweptProperties.InheritedAutoSignature(
			typeof(OneOfTheFamily), PropertyScope.Readable));
	}

	/// <summary>
	/// A subject with nothing to ask about is its own honest pass, not
	/// somebody else's answer.
	/// </summary>
	[Fact]
	public void A_type_with_no_properties_in_scope_has_none()
		=> XAssert.Null(SweptProperties.InheritedAutoSignature(
			typeof(NothingToAsk), PropertyScope.RoundTrippable));

	/// <summary>
	/// Scope also decides what is asked about at all: a property nothing can
	/// write back is not part of the round trip.
	/// </summary>
	[Fact]
	public void A_read_only_property_is_outside_the_round_trip()
	{
		XAssert.Contains(SweptProperties.Of(typeof(Generated), PropertyScope.Readable),
			p => p.Name == nameof(Generated.Computed));

		XAssert.DoesNotContain(SweptProperties.Of(typeof(Generated), PropertyScope.RoundTrippable),
			p => p.Name == nameof(Generated.Computed));
	}

	[Fact]
	public void No_type_is_not_a_question()
	{
		XAssert.Throws<ArgumentNullException>(
			() => SweptProperties.InheritedAutoSignature(null!, PropertyScope.Readable));
		XAssert.Throws<ArgumentNullException>(() => SweptProperties.IsAutoImplemented(null!));
	}
}
