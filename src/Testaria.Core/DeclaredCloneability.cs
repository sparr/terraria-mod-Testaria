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
/// How an author said it, which decides what the saying is worth.
/// </summary>
public enum CloneabilityDeclarationKind
{
	/// <summary>Nothing was said.</summary>
	None,

	/// <summary>
	/// An attribute alongside the loader's own computation, which still runs and
	/// can still disagree.
	/// </summary>
	Attribute,

	/// <summary>
	/// The mod overriding <c>IsCloneable</c> with a constant, which replaces the
	/// loader's computation rather than sitting beside it. Nothing else reports
	/// what the computation would have said.
	/// </summary>
	Override,

	/// <summary>Both, agreeing.</summary>
	Both,
}

/// <summary>
/// A type's own claim about its cloneability, how it was made, and where to
/// look for it.
/// </summary>
/// <param name="Claim">What the author said.</param>
/// <param name="Source">
/// Where the claim was read from, phrased for a message that has to be evaluated
/// later by somebody who did not write this. Empty when nothing was declared.
/// </param>
/// <param name="Kind">Which mechanism carried it.</param>
public readonly record struct CloneabilityDeclaration(
	CloneabilityClaim Claim,
	string Source,
	CloneabilityDeclarationKind Kind)
{
	/// <summary>Nothing was declared.</summary>
	public static CloneabilityDeclaration None { get; }
		= new(CloneabilityClaim.Undeclared, "", CloneabilityDeclarationKind.None);

	/// <summary>Whether an author said anything this code could read.</summary>
	public bool IsDeclared => Claim != CloneabilityClaim.Undeclared;

	/// <summary>
	/// Whether the claim stands in place of the loader's own computation rather
	/// than beside it. An overridden <c>IsCloneable</c> is the only answer
	/// anybody can read afterwards, so a check that trusts it has not measured
	/// anything and should say so.
	/// </summary>
	public bool ReplacesComputation
		=> Kind is CloneabilityDeclarationKind.Override or CloneabilityDeclarationKind.Both;
}

/// <summary>
/// Whether a type's author has already answered the cloneability question, in
/// whichever vocabulary they reached for.
/// <para/>
/// tModLoader computes <c>IsCloneable</c> and warns when it is false, but the
/// warning cannot distinguish a mistake from a mod that shares one object
/// between clones on purpose. Mods do say which it is, in two ways, and both
/// are read here:
/// <list type="bullet">
/// <item><description>
/// Overriding <c>IsCloneable</c> with a constant. This is the common one, and it
/// is tModLoader's own vocabulary rather than any library's: Everglow pairs
/// <c>IsCloneable =&gt; false</c> with <c>CloneNewInstances =&gt; false</c> on
/// its projectiles, terraguardians does the same on an NPC, and Fargo,
/// ImproveGame and TerraIntegration assert <c>=&gt; true</c> beside a real
/// <c>Clone</c> override. tModLoader's own <c>ModAchievement</c> declares false.
/// </description></item>
/// <item><description>
/// An <c>ExpectCloneable</c>-shaped attribute. Daybreak declares
/// <c>[ExpectCloneable(false)]</c> on such a type and enforces it at load, in
/// developer mode, throwing when the computed value disagrees with it.
/// </description></item>
/// </list>
/// <para/>
/// A declaration either way is the same statement <see cref="SweepExemptions"/>
/// exists to record, made by the party best placed to make it, in a place that
/// is already maintained. Reading it is strictly better than asking for it
/// again: nothing has to be added to this framework per mod, and the claim
/// cannot drift from the code it is about.
/// <para/>
/// Nothing here is specific to one mod, and nothing may become so. The attribute
/// is matched by name rather than by type, both because it belongs to an
/// assembly this one neither references nor can reference and because the name
/// is the only stable part: Daybreak has already moved it from
/// <c>Daybreak.Common.CodeAnalysis</c> to <c>Daybreak.Contracts.V1</c>, and the
/// same reflection-by-name is how <c>CloneSweep</c> reaches <c>IsCloneable</c>
/// itself and how it matches <c>CloneByReference</c>. The shape matched is
/// narrow: an attribute called <c>ExpectCloneable</c> exposing a
/// <c>bool IsCloneable</c>.
/// <para/>
/// This only ever reads. What a caller does with a claim is the caller's policy,
/// and <c>CloneSweep</c>'s is to withhold a failure the author has accounted
/// for and to report what it could not measure, never to raise a failure
/// because a declaration exists. Inventing one out of another mod's vocabulary
/// would make this framework the enforcer of a standard it does not own.
/// </summary>
public static class DeclaredCloneability
{
	/// <summary>The attribute name matched, with and without the conventional suffix.</summary>
	public const string AttributeName = "ExpectCloneable";

	/// <summary>The property a mod overrides to answer for itself, and the member read off the attribute.</summary>
	public const string MemberName = "IsCloneable";

	/// <summary>
	/// What a type's author said about cloning it, by either route.
	/// <para/>
	/// Two routes that disagree are treated as nothing said, on the same
	/// reasoning as two attributes that disagree: a contradiction is not a claim
	/// anybody can act on, and picking a winner would be this framework deciding
	/// which half of a mod's own source to believe.
	/// </summary>
	public static CloneabilityDeclaration Of(Type type)
	{
		CloneabilityDeclaration attribute = FromAttribute(type);
		CloneabilityDeclaration property = FromOverride(type);

		if (!attribute.IsDeclared)
			return property;

		if (!property.IsDeclared)
			return attribute;

		if (attribute.Claim != property.Claim)
			return CloneabilityDeclaration.None;

		return new CloneabilityDeclaration(attribute.Claim,
			$"{attribute.Source} and {property.Source}",
			CloneabilityDeclarationKind.Both);
	}

	/// <summary>
	/// What an <c>ExpectCloneable</c>-shaped attribute on the type says.
	/// <para/>
	/// Inherited attributes count, matching what a declaring mod's own enforcer
	/// sees: <c>GetCustomAttribute&lt;T&gt;</c> on a <see cref="Type"/> searches
	/// the base chain by default, so a declaration on a base class governs its
	/// subclasses there and should here too.
	/// <para/>
	/// Two readable attributes that disagree are treated as nothing said. The
	/// order attributes come back in is not specified, so taking the first would
	/// make the answer depend on it.
	/// </summary>
	public static CloneabilityDeclaration FromAttribute(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);

		CloneabilityDeclaration found = CloneabilityDeclaration.None;

		foreach (object attribute in type.GetCustomAttributes(inherit: true)) {
			Type attributeType = attribute.GetType();

			if (!IsExpectCloneable(attributeType.Name))
				continue;

			if (!TryReadMember(attribute, attributeType, out bool expected))
				continue;

			CloneabilityClaim claim = Claim(expected);

			if (found.IsDeclared && found.Claim != claim)
				return CloneabilityDeclaration.None;

			found = new CloneabilityDeclaration(claim,
				$"[{attributeType.FullName ?? attributeType.Name}]",
				CloneabilityDeclarationKind.Attribute);
		}

		return found;
	}

	/// <summary>
	/// What the type says by overriding <c>IsCloneable</c> itself.
	/// <para/>
	/// Two conditions, and both are load bearing. The property must be declared
	/// in the same assembly as the type, which is what separates a mod answering
	/// for itself from tModLoader computing an answer for it, and is the
	/// ownership test <c>CloneSweep.Blame</c> already applies to fields. And the
	/// getter must return a constant, read from its IL, because only a literal
	/// is a statement: a getter that computes, caches, or defers to
	/// <c>base</c> is reporting a value rather than declaring one, and reading
	/// it as a declaration would silence exactly the finding this check exists
	/// to make.
	/// </summary>
	public static CloneabilityDeclaration FromOverride(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);

		PropertyInfo? property = type.GetProperty(MemberName,
			BindingFlags.Public | BindingFlags.Instance);

		if (property?.GetMethod is not { } getter)
			return CloneabilityDeclaration.None;

		if (property.DeclaringType is not { } owner || owner.Assembly != type.Assembly)
			return CloneabilityDeclaration.None;

		if (!TryReadConstant(getter, out bool declared))
			return CloneabilityDeclaration.None;

		return new CloneabilityDeclaration(Claim(declared),
			$"{owner.FullName ?? owner.Name}'s own {MemberName}",
			CloneabilityDeclarationKind.Override);
	}

	/// <summary>
	/// Whether the mod itself provides a <c>Clone</c>, which is the one thing
	/// that corroborates a type's claim to be cloneable.
	/// <para/>
	/// Not a declaration and never read as one. A type asserting
	/// <c>IsCloneable =&gt; true</c> has replaced the only answer anybody could
	/// have checked, and the assertion is true when the mod copies its own
	/// fields, which is what a <c>Clone</c> override does and what tModLoader's
	/// own <c>Cloning.IsCloneable</c> looks for first. A caller can use this to
	/// tell an assertion it can stand behind from one it merely inherited.
	/// </summary>
	public static bool OverridesCloneItself(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);

		for (Type? at = type; at is not null && at != typeof(object); at = at.BaseType) {
			if (at.Assembly != type.Assembly)
				return false;

			if (at.GetMethods(BindingFlags.Instance | BindingFlags.Public
					| BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
				.Any(method => method.Name == "Clone")) {
				return true;
			}
		}

		return false;
	}

	private static CloneabilityClaim Claim(bool cloneable)
		=> cloneable ? CloneabilityClaim.Cloneable : CloneabilityClaim.NotCloneable;

	private static bool IsExpectCloneable(string name)
		=> name == AttributeName || name == AttributeName + nameof(Attribute);

	/// <summary>
	/// The bool off one attribute instance, as a property or a field.
	/// <para/>
	/// Both, because which one an author reached for says nothing about what
	/// they meant. Anything else the name might be attached to, and any getter
	/// that throws, reads as no declaration: this is foreign code running inside
	/// a check, and a mod that cannot state its claim cleanly does not get to
	/// fail the run over it.
	/// </summary>
	private static bool TryReadMember(object attribute, Type attributeType, out bool expected)
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

	/// <summary>
	/// Whether a getter is nothing but a constant, and which one.
	/// <para/>
	/// Read from IL rather than by calling it, because calling needs an instance
	/// and because a getter that does anything at all is not the thing being
	/// looked for. The instructions accepted are the ones a C# compiler emits
	/// for <c>=&gt; false</c> and for <c>get { return false; }</c>, which differ
	/// between optimised and unoptimised builds: a load of the constant, and the
	/// local, branch and return around it that a debug build adds. Anything
	/// outside that set ends the read, so a getter reading a field, calling
	/// <c>base</c>, or branching on state declares nothing.
	/// </summary>
	private static bool TryReadConstant(MethodInfo getter, out bool value)
	{
		value = false;

		byte[]? il;

		try {
			il = getter.GetMethodBody()?.GetILAsByteArray();
		}
		catch (Exception) {
			return false;
		}

		if (il is null || il.Length == 0)
			return false;

		bool? seen = null;

		for (int at = 0; at < il.Length; at++) {
			switch (il[at]) {
				case 0x00: // nop
				case 0x06: // ldloc.0
				case 0x0A: // stloc.0
				case 0x2A: // ret
					break;

				case 0x2B: // br.s, whose one operand byte must not be read as an opcode
					at++;
					break;

				case 0x16: // ldc.i4.0
				case 0x17: // ldc.i4.1
					bool loaded = il[at] == 0x17;

					if (seen is { } already && already != loaded)
						return false;

					seen = loaded;
					break;

				default:
					return false;
			}
		}

		if (seen is not { } constant)
			return false;

		value = constant;
		return true;
	}
}
