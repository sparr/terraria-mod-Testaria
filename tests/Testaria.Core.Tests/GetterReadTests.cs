using TAssert = Testaria.Assert;

namespace Testaria.Tests;

/// <summary>
/// <see cref="Testaria.Assert.GettersDoNotThrow"/>, and
/// <see cref="Testaria.SweepExemptions"/>, which is how a mod's own suite says
/// an answer here was intended.
/// </summary>
public class GetterReadTests
{
	private sealed class Ordinary
	{
		public int Number { get; set; } = 3;
		public string Computed => Number.ToString();
	}

	private sealed class OneUnreadable
	{
		public int Fine { get; set; }

		/// <summary>The shape of a client-only property reached on a server.</summary>
		public string Font => throw new InvalidOperationException("no fonts are loaded here");
	}

	private sealed class WriteOnly
	{
		public string Sink { set { } }
	}

	private sealed class HasAnIndexer
	{
		public string this[int index] => throw new InvalidOperationException("an indexer has no value to read");

		public int Number { get; set; }
	}

	[Fact]
	public void A_type_whose_properties_can_all_be_read_passes()
		=> TAssert.GettersDoNotThrow(new Ordinary());

	[Fact]
	public void A_getter_that_throws_is_a_failure()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.GettersDoNotThrow(new OneUnreadable()));

		XAssert.Contains("Font", thrown.Message);
		XAssert.Contains("InvalidOperationException", thrown.Message);
		XAssert.Contains("1 property cannot be read", thrown.Message);
	}

	/// <summary>
	/// A read-only property is the point rather than an edge case: it is the
	/// kind computed on the fly, and the kind that throws.
	/// </summary>
	[Fact]
	public void A_property_with_no_setter_is_still_read()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => TAssert.GettersDoNotThrow(new OneUnreadable()));

		XAssert.Contains("Font", thrown.Message);
	}

	private sealed class HasARefStructProperty
	{
		private readonly int[] numbers = [1, 2, 3];

		/// <summary>Reflection cannot box one of these, whatever it returns.</summary>
		public ReadOnlySpan<int> Numbers => numbers;

		public int Fine { get; set; }
	}

	/// <summary>
	/// A property returning a ref struct is skipped rather than reported. It
	/// cannot be read by reflection at all, so a failure would be about this
	/// check rather than about the property.
	/// </summary>
	[Fact]
	public void A_ref_struct_property_is_not_reported()
		=> TAssert.GettersDoNotThrow(new HasARefStructProperty());

	[Fact]
	public void A_write_only_property_is_left_alone()
		=> TAssert.GettersDoNotThrow(new WriteOnly());

	[Fact]
	public void An_indexer_is_skipped()
		=> TAssert.GettersDoNotThrow(new HasAnIndexer());

	[Fact]
	public void A_null_subject_fails_rather_than_throwing_a_null_reference()
		=> XAssert.Throws<AssertionException>(() => TAssert.GettersDoNotThrow(null!));

	// ---- exemptions -------------------------------------------------------

	[Fact]
	public void An_undeclared_subject_is_not_exempt()
	{
		SweepExemptions.Clear();

		XAssert.False(SweepExemptions.IsExempt("Mod/Type", SweepCheck.Settling, out _));
	}

	[Fact]
	public void A_declared_exemption_carries_its_reason()
	{
		SweepExemptions.Clear();
		SweepExemptions.Declare("Mod/Type", SweepCheck.Settling, "it drifts on purpose");

		XAssert.True(SweepExemptions.IsExempt("Mod/Type", SweepCheck.Settling, out string reason));
		XAssert.Equal("it drifts on purpose", reason);
	}

	/// <summary>An exemption is for one check, not for the type as a whole.</summary>
	[Fact]
	public void An_exemption_does_not_spread_to_the_other_checks()
	{
		SweepExemptions.Clear();
		SweepExemptions.Declare("Mod/Type", SweepCheck.Settling, "it drifts on purpose");

		XAssert.False(SweepExemptions.IsExempt("Mod/Type", SweepCheck.RoundTrip, out _));
		XAssert.False(SweepExemptions.IsExempt("Mod/Type", SweepCheck.GetterReads, out _));
	}

	/// <summary>
	/// A reason is not optional. An exemption nobody can evaluate later cannot
	/// be told apart from one that was wrong when it was made.
	/// </summary>
	[Fact]
	public void An_exemption_without_a_reason_is_refused()
	{
		SweepExemptions.Clear();

		XAssert.Throws<ArgumentException>(
			() => SweepExemptions.Declare("Mod/Type", SweepCheck.Settling, "  "));
	}

	[Fact]
	public void An_exemption_without_a_subject_is_refused()
	{
		SweepExemptions.Clear();

		XAssert.Throws<ArgumentException>(
			() => SweepExemptions.Declare("", SweepCheck.Settling, "a reason"));
	}

	/// <summary>
	/// Clearing matters: static state that outlives a mod reload carries one
	/// run's declarations into the next.
	/// </summary>
	[Fact]
	public void Clearing_forgets_everything()
	{
		SweepExemptions.Declare("Mod/Type", SweepCheck.Settling, "a reason");
		SweepExemptions.Clear();

		XAssert.Empty(SweepExemptions.All);
	}
}
