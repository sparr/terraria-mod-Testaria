using System.Reflection;
using System.Reflection.Emit;

namespace Testaria.Tests;

/// <summary>
/// <see cref="DeclaredCloneability"/>, which reads a mod's own statement about
/// whether one of its types may share state between clones.
/// <para/>
/// The statement is an override of tModLoader's <c>IsCloneable</c>, so the
/// types below stand in for swept content: what matters to the reader is which
/// assembly declares the property and whether its getter is a constant, not
/// which loader base class it derives from.
/// </summary>
public class DeclaredCloneabilityTests
{
	// A base declaring the property, standing in for the ModType that supplies
	// it in a real sweep. Every type in this file shares an assembly with this
	// base, so the cross-assembly case is emitted rather than written; see
	// below.
	private abstract class Content
	{
		public virtual bool IsCloneable => false;
	}

	private sealed class InheritsItsModsDeclaration : Content;

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

	private sealed class NoPropertyAtAll;

	private sealed class CopiesItself : Content
	{
		public override bool IsCloneable => true;

		public CopiesItself Clone() => new();
	}

	[Fact]
	public void A_type_overriding_the_property_with_false_declares_that()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(DeclaresItShares));

		XAssert.Equal(CloneabilityClaim.NotCloneable, declared.Claim);
		XAssert.True(declared.IsDeclared);
		XAssert.Contains(nameof(DeclaresItShares), declared.Source);
	}

	[Fact]
	public void A_type_overriding_the_property_with_true_declares_that()
	{
		CloneabilityDeclaration declared = DeclaredCloneability.Of(typeof(DeclaresItDoesNot));

		XAssert.Equal(CloneabilityClaim.Cloneable, declared.Claim);
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
			DeclaredCloneability.Of(typeof(InheritsItsModsDeclaration));

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

	/// <summary>
	/// The corroboration, which is not a declaration and is never read as one.
	/// </summary>
	[Fact]
	public void A_mod_declaring_its_own_Clone_is_visible_as_such()
	{
		XAssert.True(DeclaredCloneability.OverridesCloneItself(typeof(CopiesItself)));
		XAssert.False(DeclaredCloneability.OverridesCloneItself(typeof(DeclaresItDoesNot)));
	}

	/// <summary>A type with no such property at all says nothing.</summary>
	[Fact]
	public void A_type_without_the_property_declares_nothing()
		=> XAssert.Equal(CloneabilityClaim.Undeclared,
			DeclaredCloneability.Of(typeof(NoPropertyAtAll)).Claim);

	[Fact]
	public void No_type_is_not_a_question()
	{
		XAssert.Throws<ArgumentNullException>(() => DeclaredCloneability.Of(null!));
		XAssert.Throws<ArgumentNullException>(
			() => DeclaredCloneability.OverridesCloneItself(null!));
	}
}
