namespace Testaria.Tests;

/// <summary>
/// <see cref="DeclaredCloneability"/>, which reads another mod's own statement
/// about whether one of its types may share state between clones.
/// <para/>
/// The attributes below stand in for Daybreak's <c>ExpectCloneableAttribute</c>.
/// They are declared here rather than referenced from it for the same reason
/// the production code matches by name: the real one lives in an assembly this
/// solution does not reference and cannot, so what is being tested is exactly
/// the shape, not the type.
/// </summary>
public class DeclaredCloneabilityTests
{
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	private sealed class ExpectCloneableAttribute(bool isCloneable = true) : Attribute
	{
		public bool IsCloneable => isCloneable;
	}

	/// <summary>Named without the suffix, which C# allows and somebody will do.</summary>
	[AttributeUsage(AttributeTargets.Class)]
	private sealed class ExpectCloneable : Attribute
	{
		public bool IsCloneable;
	}

	[AttributeUsage(AttributeTargets.Class)]
	private sealed class ExpectCloneableNotReallyAttribute : Attribute
	{
		public bool IsCloneable => false;
	}

	[AttributeUsage(AttributeTargets.Class)]
	private sealed class ExpectCloneableSilentAttribute : Attribute
	{
		public string IsCloneable => "no";
	}

	[AttributeUsage(AttributeTargets.Class)]
	private sealed class ExpectCloneableAngryAttribute : Attribute
	{
		public bool IsCloneable => throw new InvalidOperationException("no");
	}

	[ExpectCloneableAttribute(false)]
	private sealed class SharesOnPurpose;

	[ExpectCloneableAttribute(true)]
	private sealed class MustNotShare;

	[ExpectCloneableAttribute]
	private sealed class MustNotShareByDefault;

	[@ExpectCloneable(IsCloneable = false)]
	private sealed class SharesOnPurposeWithAField;

	private sealed class SaysNothing;

	[ExpectCloneableNotReally]
	private sealed class SaysNothingWithASimilarName;

	[ExpectCloneableSilent]
	private sealed class SaysSomethingUnreadable;

	[ExpectCloneableAngry]
	private sealed class ThrowsWhenAsked;

	[ExpectCloneableAttribute(false)]
	[ExpectCloneableAttribute(true)]
	private sealed class ContradictsItself;

	[ExpectCloneableAttribute(false)]
	private class DeclaredOnTheBase;

	private sealed class InheritsTheDeclaration : DeclaredOnTheBase;

	[Fact]
	public void A_type_declaring_it_shares_is_read_as_such()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(SharesOnPurpose));

		XAssert.Equal(CloneabilityClaim.NotCloneable, declared.Claim);
		XAssert.True(declared.IsDeclared);
	}

	/// <summary>
	/// The source is in every message the claim produces, so a reader who
	/// doubts a skip can go and look at the attribute that caused it.
	/// </summary>
	[Fact]
	public void A_declaration_names_the_attribute_it_came_from()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(SharesOnPurpose));

		XAssert.Contains(nameof(ExpectCloneableAttribute), declared.Source);
	}

	[Fact]
	public void A_type_declaring_it_must_not_share_is_read_as_such()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(MustNotShare));

		XAssert.Equal(CloneabilityClaim.Cloneable, declared.Claim);
	}

	/// <summary>
	/// Daybreak's attribute defaults to true, and an author who writes the bare
	/// attribute means what its own default means.
	/// </summary>
	[Fact]
	public void An_attribute_left_at_its_default_is_read_at_that_default()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(MustNotShareByDefault));

		XAssert.Equal(CloneabilityClaim.Cloneable, declared.Claim);
	}

	/// <summary>
	/// A field rather than a property. Which one an author reached for says
	/// nothing about what they meant.
	/// </summary>
	[Fact]
	public void A_claim_held_in_a_field_is_read_too()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(SharesOnPurposeWithAField));

		XAssert.Equal(CloneabilityClaim.NotCloneable, declared.Claim);
	}

	[Fact]
	public void A_type_saying_nothing_declares_nothing()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(SaysNothing));

		XAssert.Equal(CloneabilityClaim.Undeclared, declared.Claim);
		XAssert.False(declared.IsDeclared);
		XAssert.Equal(CloneabilityDeclaration.None, declared);
	}

	/// <summary>
	/// Matching by name is loose enough to be worth pinning: the name has to be
	/// the name, not merely start with it, or an unrelated attribute could
	/// silence a finding.
	/// </summary>
	[Fact]
	public void An_attribute_whose_name_merely_begins_the_same_is_not_a_declaration()
	{
		CloneabilityDeclaration declared =
			DeclaredCloneability.Of(typeof(SaysNothingWithASimilarName));

		XAssert.Equal(CloneabilityClaim.Undeclared, declared.Claim);
	}

	[Fact]
	public void An_attribute_carrying_no_bool_declares_nothing()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(SaysSomethingUnreadable));

		XAssert.Equal(CloneabilityClaim.Undeclared, declared.Claim);
	}

	/// <summary>
	/// Foreign code runs inside this call. A mod whose getter throws has said
	/// nothing, and must not be able to end the run that asked.
	/// </summary>
	[Fact]
	public void An_attribute_that_throws_when_read_declares_nothing()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(ThrowsWhenAsked));

		XAssert.Equal(CloneabilityClaim.Undeclared, declared.Claim);
	}

	/// <summary>
	/// Attributes come back in no specified order, so taking the first would
	/// make the answer depend on it.
	/// </summary>
	[Fact]
	public void Two_declarations_that_disagree_declare_nothing()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(ContradictsItself));

		XAssert.Equal(CloneabilityClaim.Undeclared, declared.Claim);
	}

	/// <summary>
	/// Inherited, matching what a declaring mod's own enforcer sees:
	/// <c>GetCustomAttribute</c> on a type searches the base chain by default.
	/// </summary>
	[Fact]
	public void A_declaration_on_a_base_class_governs_its_subclasses()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(InheritsTheDeclaration));

		XAssert.Equal(CloneabilityClaim.NotCloneable, declared.Claim);
	}

	[Fact]
	public void No_type_is_not_a_question()
		=> XAssert.Throws<ArgumentNullException>(() => DeclaredCloneability.Of(null!));
}
