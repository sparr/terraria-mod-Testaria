using System.Reflection;

namespace Testaria;

/// <summary>What a type's own author has said about whether it can be cloned.</summary>
public enum CloneabilityClaim
{
	/// <summary>Nothing readable was said, so the check has only the loader's answer.</summary>
	Undeclared,

	/// <summary>The author says copies of this type must not share state.</summary>
	Cloneable,

	/// <summary>The author says sharing is intended, which is a claim the loader cannot make.</summary>
	NotCloneable,
}

/// <summary>
/// A type's own claim about its cloneability, and which attribute carried it.
/// </summary>
/// <param name="Claim">What the author said.</param>
/// <param name="Source">
/// The attribute the claim was read from, fully qualified, for a message that has
/// to be evaluated later by somebody who did not write this. Empty when nothing
/// was declared.
/// </param>
public readonly record struct CloneabilityDeclaration(CloneabilityClaim Claim, string Source)
{
	/// <summary>Nothing was declared.</summary>
	public static CloneabilityDeclaration None { get; } = new(CloneabilityClaim.Undeclared, "");

	/// <summary>Whether an author said anything this code could read.</summary>
	public bool IsDeclared => Claim != CloneabilityClaim.Undeclared;
}

/// <summary>
/// Whether a type's author has already answered the cloneability question, in
/// their own vocabulary.
/// <para/>
/// tModLoader computes <c>IsCloneable</c> and warns when it is false, but the
/// warning cannot distinguish a mistake from a mod that shares one registry
/// between clones on purpose. Some mods say which it is. Daybreak declares
/// <c>[ExpectCloneable(false)]</c> on such a type and enforces the declaration
/// at load, throwing when the computed value disagrees with it.
/// <para/>
/// A declaration like that is the same statement <see cref="SweepExemptions"/>
/// exists to record, made by the party best placed to make it, in a place that
/// is already maintained and already checked. Reading it is strictly better
/// than asking for it again: nothing has to be added to this framework per mod,
/// and the claim cannot drift from the mod it is about.
/// <para/>
/// Matched by name, not by type. The attribute belongs to another mod's
/// assembly, which this one neither references nor can reference, and the same
/// reflection-by-name is already how <c>CloneSweep</c> reaches
/// <c>IsCloneable</c> itself and how it matches <c>CloneByReference</c>.
/// The shape matched is deliberately narrow: an attribute called
/// <c>ExpectCloneable</c> exposing a <c>bool IsCloneable</c>.
/// <para/>
/// This only ever reads. What a caller does with a claim is the caller's
/// policy, and <c>CloneSweep</c>'s is to withhold a failure the author has
/// already accounted for, never to raise one because a declaration exists.
/// Inventing a new failure out of another mod's attribute would make this
/// framework the enforcer of a standard it does not own.
/// </summary>
public static class DeclaredCloneability
{
	/// <summary>The attribute name matched, with and without the conventional suffix.</summary>
	public const string AttributeName = "ExpectCloneable";

	/// <summary>The member read off it.</summary>
	public const string MemberName = "IsCloneable";

	/// <summary>
	/// What a type's author said about cloning it, if anything readable.
	/// <para/>
	/// Inherited attributes count, matching what the declaring mod's own
	/// enforcer sees: <c>GetCustomAttribute&lt;T&gt;</c> on a <see cref="Type"/>
	/// searches the base chain by default, so a declaration on a base class
	/// governs its subclasses there and should here too.
	/// <para/>
	/// Two readable attributes that disagree are treated as nothing said. The
	/// order attributes come back in is not specified, so taking the first
	/// would make the answer depend on it, and a contradiction is not a claim
	/// anybody can act on.
	/// </summary>
	public static CloneabilityDeclaration Of(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);

		CloneabilityDeclaration found = CloneabilityDeclaration.None;

		foreach (object attribute in type.GetCustomAttributes(inherit: true)) {
			Type attributeType = attribute.GetType();

			if (!IsExpectCloneable(attributeType.Name))
				continue;

			if (!TryRead(attribute, attributeType, out bool expected))
				continue;

			CloneabilityClaim claim = expected
				? CloneabilityClaim.Cloneable
				: CloneabilityClaim.NotCloneable;

			if (found.IsDeclared && found.Claim != claim)
				return CloneabilityDeclaration.None;

			found = new CloneabilityDeclaration(claim,
				attributeType.FullName ?? attributeType.Name);
		}

		return found;
	}

	private static bool IsExpectCloneable(string name)
		=> name == AttributeName || name == AttributeName + nameof(Attribute);

	/// <summary>
	/// The bool off one attribute instance, as a property or a field.
	/// <para/>
	/// Both, because which one an author reached for says nothing about what
	/// they meant. Anything else the name might be attached to, and any getter
	/// that throws, reads as no declaration: this is foreign code running
	/// inside a check, and a mod that cannot state its claim cleanly does not
	/// get to fail the run over it.
	/// </summary>
	private static bool TryRead(object attribute, Type attributeType, out bool expected)
	{
		expected = false;

		object? value;

		try {
			PropertyInfo? property = attributeType.GetProperty(MemberName,
				BindingFlags.Public | BindingFlags.Instance);

			if (property?.GetMethod is not null) {
				value = property.GetMethod.Invoke(attribute, null);
			}
			else {
				FieldInfo? field = attributeType.GetField(MemberName,
					BindingFlags.Public | BindingFlags.Instance);

				if (field is null)
					return false;

				value = field.GetValue(attribute);
			}
		}
		catch (Exception) {
			return false;
		}

		if (value is not bool read)
			return false;

		expected = read;
		return true;
	}
}
