namespace Testaria;

/// <summary>
/// Declares that a member cannot be trusted without a completed load pass.
/// <para/>
/// This is the boundary from PLAN.md section 2.2 written down where a tool can
/// read it. Loader state does not fail cleanly outside the game: measured,
/// <c>ContentSamples.ItemsByType</c> is empty, <c>ModLoader.Mods</c> is empty,
/// an item's <c>Name</c> is <c>""</c>, and <c>ItemID.Sets.Deprecated</c> is
/// vanilla-sized. A test touching any of them from a bare host goes green
/// while proving nothing, which is worse than no test at all.
/// <para/>
/// Marking a member says "this needs the game". The analyzer then stops
/// blaming the member itself and starts blaming its undeclared callers, so the
/// dependency travels up the call graph to the test that has to answer for it,
/// rather than stopping at whoever wrote the helper.
/// </summary>
[AttributeUsage(
	AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property
	| AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly,
	AllowMultiple = false,
	Inherited = true)]
public sealed class RequiresLoadedGameAttribute : Attribute
{
	/// <summary>Declares a dependency on loader state.</summary>
	public RequiresLoadedGameAttribute() { }

	/// <param name="reason">What about a loaded game this member needs.</param>
	public RequiresLoadedGameAttribute(string reason) => Reason = reason;

	/// <summary>What about a loaded game this member needs, if it says.</summary>
	public string? Reason { get; }
}
