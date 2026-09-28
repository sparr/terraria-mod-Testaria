using System.Reflection;

namespace Testaria;

/// <summary>Which properties a check asks about.</summary>
public enum PropertyScope
{
	/// <summary>Everything it can read.</summary>
	Readable,

	/// <summary>Everything it can read and write straight back.</summary>
	RoundTrippable,
}

/// <summary>
/// The properties the property checks ask about, and whether asking a type is
/// asking anything new.
/// <para/>
/// One definition, used by the assertions that do the asking and by the sweep
/// that decides whom to ask. Two copies of this rule would drift, and the
/// second copy is the one that decides a subject is redundant, so a drift
/// there would quietly stop a property being checked anywhere.
/// </summary>
public static class SweptProperties
{
	/// <summary>
	/// The public instance properties a type's own assembly declares, in the
	/// given scope.
	/// <para/>
	/// Not the inherited ones from somewhere else, and that is the whole of the
	/// rule: these checks ask about the code somebody wrote, not about the
	/// framework it derives from. Asking otherwise is not merely noisy, it is
	/// unanswerable. A tModLoader <c>ModType</c> gets its <c>Name</c>,
	/// <c>FullName</c> and <c>DisplayName</c> from the loader that registered
	/// it, so an instance built by reflection rather than by the loader throws
	/// from all three, and the mod that declared none of them is blamed.
	/// Measured: sweeping one corpus this way produced 51 failures, every one
	/// of them a property tModLoader declares.
	/// <para/>
	/// A base class in the same assembly is still the author's own code and is
	/// still asked about, which is why this tests the assembly rather than
	/// simply passing <c>DeclaredOnly</c>.
	/// </summary>
	public static IEnumerable<PropertyInfo> Of(Type type, PropertyScope scope)
	{
		ArgumentNullException.ThrowIfNull(type);

		foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			if (property.GetMethod is not { IsPublic: true })
				continue;

			if (scope == PropertyScope.RoundTrippable && property.SetMethod is not { IsPublic: true })
				continue;

			if (property.GetIndexParameters().Length != 0)
				continue;

			if (property.DeclaringType?.Assembly != type.Assembly)
				continue;

			// A ref struct cannot be boxed, so reflection cannot read one at
			// all: PropertyInfo.GetValue throws NotSupportedException whatever
			// the property does. Nothing can be asked of a Span<T> here, and
			// reporting that as the property's fault blames the wrong thing.
			// Measured: one mod's rigging code exposes its bones as
			// ReadOnlySpan<int>, and twelve of its types were reported as
			// unreadable when the only thing that could not read them was this.
			if (property.PropertyType.IsByRefLike)
				continue;

			yield return property;
		}
	}

	/// <summary>
	/// Whether a property is the compiler's, holding a value and doing nothing
	/// else.
	/// <para/>
	/// Recognised by the backing field the compiler emits beside it, which is
	/// the only trace an auto-property leaves in metadata. It matters because
	/// such accessors cannot behave differently from one instance to the next:
	/// the getter returns the field, the setter assigns it, and neither looks
	/// at the value or at anything else. A property with a body can validate,
	/// normalise, or throw, and two subjects holding different values can get
	/// different answers from it.
	/// </summary>
	public static bool IsAutoImplemented(PropertyInfo property)
	{
		ArgumentNullException.ThrowIfNull(property);

		return property.DeclaringType?.GetField($"<{property.Name}>k__BackingField",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;
	}

	/// <summary>
	/// What a type contributes to a scope beyond what its base classes already
	/// contribute, or null when it contributes something that has to be asked
	/// of this type in particular.
	/// <para/>
	/// The signature is the set of properties a check would ask about, named by
	/// where they are declared. Two types with the same signature are asked the
	/// same questions of the same code, so one of them answers for both, and
	/// this returns the same string for both.
	/// <para/>
	/// Null, meaning "ask this one", whenever that reasoning does not hold:
	/// <list type="bullet">
	/// <item><description>
	/// It declares one of the properties itself, so the code being asked about
	/// is its own and nothing else covers it.
	/// </description></item>
	/// <item><description>
	/// Any of them has a body. Then the answer can depend on the value the
	/// property holds, a different subject holds a different value, and one
	/// subject cannot stand for another. This is the condition that keeps the
	/// collapse honest, and it is why a getter reading a field set by a
	/// constructor is never collapsed away.
	/// </description></item>
	/// <item><description>
	/// There are none at all. A subject with nothing to ask about is a distinct
	/// and honest pass, and reporting it as covered by somebody else would say
	/// something untrue about it.
	/// </description></item>
	/// </list>
	/// </summary>
	public static string? InheritedAutoSignature(Type type, PropertyScope scope)
	{
		ArgumentNullException.ThrowIfNull(type);

		List<string> named = [];

		foreach (PropertyInfo property in Of(type, scope)) {
			if (property.DeclaringType == type)
				return null;

			if (!IsAutoImplemented(property))
				return null;

			named.Add($"{property.DeclaringType!.FullName}.{property.Name}");
		}

		if (named.Count == 0)
			return null;

		named.Sort(StringComparer.Ordinal);

		return string.Join(", ", named);
	}
}
