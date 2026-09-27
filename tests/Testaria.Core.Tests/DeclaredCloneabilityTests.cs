using System.Reflection;
using System.Reflection.Emit;

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

	// The other route, and the common one in the wild: a mod answering for
	// itself by overriding tModLoader's own property. Stood in for here by a
	// base class outside the subject's own hierarchy of declarations, because
	// what separates an authored answer from a computed one is which assembly
	// declares the property, and every type in this file shares one assembly
	// with its base. A cross-assembly base is covered by the types tModLoader
	// itself supplies, which no unit test can reach.
	private abstract class Content
	{
		public virtual bool IsCloneable => false;
	}

	private sealed class SaysNothingAboutCloning : Content;

	private sealed class DeclaresItShares : Content
	{
		public override bool IsCloneable => false;
	}

	private sealed class DeclaresItDoesNot : Content
	{
		public override bool IsCloneable => true;
	}

	private sealed class DeclaresItSharesInABlock : Content
	{
		public override bool IsCloneable {
			get { return false; }
		}
	}

	private sealed class ComputesTheAnswer : Content
	{
		private readonly bool answer = true;

		public override bool IsCloneable => answer;
	}

	private sealed class DefersToItsBase : Content
	{
		public override bool IsCloneable => base.IsCloneable;
	}

	private sealed class CopiesItself : Content
	{
		public override bool IsCloneable => true;

		public CopiesItself Clone() => new();
	}

	[ExpectCloneableAttribute(false)]
	private sealed class SaysItTwice : Content
	{
		public override bool IsCloneable => false;
	}

	[ExpectCloneableAttribute(true)]
	private sealed class SaysBothThings : Content
	{
		public override bool IsCloneable => false;
	}

	[Fact]
	public void A_type_overriding_the_property_with_false_declares_that()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(DeclaresItShares));

		XAssert.Equal(CloneabilityClaim.NotCloneable, declared.Claim);
		XAssert.Equal(CloneabilityDeclarationKind.Override, declared.Kind);
		XAssert.True(declared.ReplacesComputation);
		XAssert.Contains(nameof(DeclaresItShares), declared.Source);
	}

	[Fact]
	public void A_type_overriding_the_property_with_true_declares_that()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(DeclaresItDoesNot));

		XAssert.Equal(CloneabilityClaim.Cloneable, declared.Claim);
		XAssert.Equal(CloneabilityDeclarationKind.Override, declared.Kind);
	}

	/// <summary>
	/// A debug build compiles a block-bodied getter to a store, a branch and a
	/// load around the constant. Same statement, different instructions, and
	/// the run that matters is a debug one.
	/// </summary>
	[Fact]
	public void A_constant_returned_from_a_block_is_still_a_constant()
	{
		CloneabilityDeclaration declared =
			DeclaredCloneability.Of(typeof(DeclaresItSharesInABlock));

		XAssert.Equal(CloneabilityClaim.NotCloneable, declared.Claim);
	}

	/// <summary>
	/// The condition that keeps this from silencing the finding it exists to
	/// make: a getter that reports a value is not a getter that declares one.
	/// </summary>
	[Fact]
	public void A_getter_that_computes_declares_nothing()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(ComputesTheAnswer));

		XAssert.Equal(CloneabilityClaim.Undeclared, declared.Claim);
	}

	[Fact]
	public void A_getter_that_defers_to_its_base_declares_nothing()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(DefersToItsBase));

		XAssert.Equal(CloneabilityClaim.Undeclared, declared.Claim);
	}

	/// <summary>
	/// A mod's own base class declaring for its subclasses, which is how
	/// Everglow does it: the declaration sits on an abstract projectile and
	/// governs everything derived from it in the same mod.
	/// </summary>
	[Fact]
	public void A_declaration_on_a_base_class_in_the_same_assembly_governs_it()
	{
		CloneabilityDeclaration declared =
			DeclaredCloneability.Of(typeof(SaysNothingAboutCloning));

		XAssert.Equal(CloneabilityClaim.NotCloneable, declared.Claim);
		XAssert.Contains(nameof(Content), declared.Source);
	}

	/// <summary>
	/// The condition the whole thing rests on: a property declared in another
	/// assembly is somebody else's answer, not this type's declaration. In a
	/// real sweep that is every type leaving tModLoader's own
	/// <c>IsCloneable</c> alone, which is nearly all of them, and reading those
	/// as declarations would turn the check off everywhere at once.
	/// <para/>
	/// Emitted rather than written, because the situation cannot be written:
	/// every type in this file shares an assembly with its base, and the
	/// assemblies that would supply a foreign one are tModLoader's.
	/// </summary>
	[Fact]
	public void A_property_declared_in_another_assembly_is_not_a_declaration()
	{
		Type foreign = EmitForeignBase();
		Type mine = EmitDerived(foreign);

		XAssert.NotSame(foreign.Assembly, mine.Assembly);
		XAssert.Equal(CloneabilityClaim.Undeclared, DeclaredCloneability.Of(mine).Claim);

		// And the same property, read on the type that does declare it, is one.
		XAssert.Equal(CloneabilityClaim.NotCloneable, DeclaredCloneability.Of(foreign).Claim);
	}

	private static Type EmitForeignBase()
	{
		TypeBuilder type = Assembly("StandsInForTheLoader")
			.DefineType("StandsInForTheLoader.ContentBase",
				TypeAttributes.Public | TypeAttributes.Abstract);

		MethodBuilder getter = type.DefineMethod("get_IsCloneable",
			MethodAttributes.Public | MethodAttributes.Virtual
			| MethodAttributes.SpecialName | MethodAttributes.HideBySig,
			typeof(bool), Type.EmptyTypes);

		ILGenerator il = getter.GetILGenerator();
		il.Emit(OpCodes.Ldc_I4_0);
		il.Emit(OpCodes.Ret);

		type.DefineProperty("IsCloneable", PropertyAttributes.None, typeof(bool), null)
			.SetGetMethod(getter);

		return type.CreateType();
	}

	private static Type EmitDerived(Type foreignBase)
		=> Assembly("StandsInForAMod")
			.DefineType("StandsInForAMod.Content", TypeAttributes.Public, foreignBase)
			.CreateType();

	private static ModuleBuilder Assembly(string name)
		=> AssemblyBuilder
			.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect)
			.DefineDynamicModule(name);

	[Fact]
	public void Both_routes_agreeing_is_one_declaration_naming_both()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(SaysItTwice));

		XAssert.Equal(CloneabilityClaim.NotCloneable, declared.Claim);
		XAssert.Equal(CloneabilityDeclarationKind.Both, declared.Kind);
		XAssert.Contains(nameof(ExpectCloneableAttribute), declared.Source);
		XAssert.Contains(nameof(SaysItTwice), declared.Source);
	}

	[Fact]
	public void Two_routes_that_disagree_declare_nothing()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(SaysBothThings));

		XAssert.Equal(CloneabilityClaim.Undeclared, declared.Claim);
	}

	/// <summary>
	/// The corroboration, which is not a declaration and is never read as one.
	/// </summary>
	[Fact]
	public void A_mod_declaring_its_own_Clone_is_visible_as_such()
	{
		XAssert.True(DeclaredCloneability.OverridesCloneItself(typeof(CopiesItself)));
		XAssert.False(DeclaredCloneability.OverridesCloneItself(typeof(DeclaresItDoesNot)));
	}

	[Fact]
	public void An_attribute_alone_does_not_replace_the_computation()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(SharesOnPurpose));

		XAssert.Equal(CloneabilityDeclarationKind.Attribute, declared.Kind);
		XAssert.False(declared.ReplacesComputation);
	}
}
