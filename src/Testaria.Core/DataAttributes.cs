namespace Testaria;

/// <summary>
/// Supplies one set of arguments to a test, inline.
/// <para/>
/// Named <c>Case</c> rather than xUnit's <c>InlineData</c> deliberately.
/// Tier 0 projects use xUnit and Testaria.Core together by design, so a type
/// of the same name in both would make <c>using Xunit; using Testaria;</c>
/// ambiguous, which is a poor welcome. The shape is xUnit's regardless.
/// <para/>
/// Unlike xUnit there is no separate <c>[Theory]</c> marker: the tier
/// attribute is already what marks a method as a test, so a method that has
/// data parameters and a source of data for them is parameterised, and one
/// that does not is not. A leading <see cref="ITestContext"/> parameter is the
/// context rather than data, and is not counted.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class CaseAttribute(params object?[] data) : Attribute
{
	/// <summary>The arguments, in parameter order.</summary>
	public object?[] Data { get; } = data ?? [null];
}

/// <summary>
/// Draws a test's arguments from a static member on the declaring type,
/// returning <c>IEnumerable&lt;object?[]&gt;</c>.
/// <para/>
/// The reason parameterisation was worth building. Cases that only exist once
/// the game is loaded, every item a mod registers, every recipe it adds,
/// cannot be written out inline, and discovery runs in the game for every tier
/// above zero, so a member source can enumerate them.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class CaseSourceAttribute(string memberName) : Attribute
{
	/// <summary>Name of a static field, property or parameterless method.</summary>
	public string MemberName { get; } = memberName;
}
