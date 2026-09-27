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
/// A type's own claim about its cloneability, and where to look for it.
/// </summary>
/// <param name="Claim">What the author said.</param>
/// <param name="Source">
/// Where the claim was read from, phrased for a message that has to be evaluated
/// later by somebody who did not write this. Empty when nothing was declared.
/// </param>
public readonly record struct CloneabilityDeclaration(CloneabilityClaim Claim, string Source)
{
	/// <summary>Nothing was declared.</summary>
	public static CloneabilityDeclaration None { get; } = new(CloneabilityClaim.Undeclared, "");

	/// <summary>Whether an author said anything this code could read.</summary>
	public bool IsDeclared => Claim != CloneabilityClaim.Undeclared;
}

/// <summary>
/// Whether a type's author has already answered the cloneability question by
/// overriding <c>IsCloneable</c> themselves.
/// <para/>
/// tModLoader computes that property and warns when it is false, but the
/// warning cannot distinguish a mistake from a mod that shares one object
/// between clones on purpose. A mod can say which it is by answering for
/// itself, and this is the convention mods use: <c>override bool IsCloneable</c>
/// appears in sixteen public repositories, and <c>IsCloneable =&gt; false</c> in
/// nineteen files. Everglow pairs it with <c>CloneNewInstances =&gt; false</c> on
/// its projectiles, terraguardians on an NPC, and tModLoader's own
/// <c>ModAchievement</c> declares false. Fargo, ImproveGame and TerraIntegration
/// assert <c>=&gt; true</c> beside a real <c>Clone</c> override.
/// <para/>
/// The vocabulary is tModLoader's own, which is what makes reading it general.
/// A library's attribute would not be: one was read here for a while, matched by
/// the name <c>ExpectCloneable</c>, and it turned out that exactly one mod in the
/// ecosystem uses that name, so the code was a special case for a single
/// repository wearing a general shape. A mod that declares its intent only in a
/// library's vocabulary can say so through <see cref="SweepExemptions"/>
/// instead, which is what that exists for.
/// <para/>
/// A declaration read here is the same statement an exemption would record,
/// made by the party best placed to make it, in a place that is already
/// maintained. It cannot drift from the code it is about, and nothing has to be
/// added to this framework per mod.
/// <para/>
/// This only ever reads. What a caller does with a claim is the caller's policy,
/// and <c>CloneSweep</c>'s is to withhold a failure the author has accounted for
/// and to report what it could not measure, never to raise a failure because a
/// declaration exists.
/// </summary>
public static class DeclaredCloneability
{
	/// <summary>The property tModLoader computes and a mod may answer for itself.</summary>
	public const string MemberName = "IsCloneable";

	/// <summary>
	/// What a type says about cloning itself, by overriding <c>IsCloneable</c>.
	/// <para/>
	/// Two conditions, and both are load bearing. The property must be declared
	/// in the same assembly as the type, which separates a mod answering for
	/// itself from tModLoader computing an answer for it, and is the ownership
	/// test <c>CloneSweep.Blame</c> already applies to fields. Without it every
	/// swept type would read as declaring, since they all inherit the property.
	/// A mod's own base class declaring for its subclasses does count, which is
	/// how Everglow writes it.
	/// <para/>
	/// And the getter must return a constant, read from its IL, because only a
	/// literal is a statement: a getter that computes, caches, or defers to
	/// <c>base</c> is reporting a value rather than declaring one, and reading
	/// it as a declaration would silence exactly the finding this check exists
	/// to make.
	/// </summary>
	public static CloneabilityDeclaration Of(Type type)
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

		return new CloneabilityDeclaration(
			declared ? CloneabilityClaim.Cloneable : CloneabilityClaim.NotCloneable,
			$"{owner.FullName ?? owner.Name}'s own {MemberName}");
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
	/// tell an assertion it can stand behind from one it merely wrote down.
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
