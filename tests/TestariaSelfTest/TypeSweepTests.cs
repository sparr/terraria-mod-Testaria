using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// <see cref="TypeSweep"/>, pointed at this framework's own assembly.
/// <para/>
/// The sweep exists so that the two property checks can be asked of every type
/// a mod has rather than of the ones somebody named. Asking it of Testaria is
/// the cheapest way to know the mechanism works, and has the pleasant property
/// that the framework is held to the standard it offers.
/// </summary>
public class TypeSweepTests
{
	private const string Subject = "Testaria";

	public static IEnumerable<string> OwnTypes => TypeSweep.ConstructibleTypes(Subject);

	/// <summary>
	/// The sweep finds something. A source that silently yields nothing turns
	/// every case below into a skip, which reads like coverage and is not.
	/// </summary>
	[LoadedTest]
	public void The_sweep_finds_types_to_construct()
		=> Assert.NotEmpty(OwnTypes);

	/// <summary>Only types that can actually be made are offered.</summary>
	[LoadedTest]
	public void Every_type_it_offers_can_be_constructed()
	{
		foreach (string name in OwnTypes)
			Assert.NotNull(TypeSweep.Construct(Subject, name), $"{name} came back null");
	}

	/// <summary>An absent mod yields no cases rather than throwing during discovery.</summary>
	[LoadedTest]
	public void A_mod_that_is_not_installed_yields_nothing()
		=> Assert.Empty(TypeSweep.ConstructibleTypes("NoSuchModIsInstalled"));

	[LoadedTest]
	[CaseSource(nameof(OwnTypes))]
	public void Its_own_types_take_their_own_values(string typeName)
		=> Assert.SettersAcceptTheirOwnGetters(TypeSweep.Construct(Subject, typeName));

	[LoadedTest]
	[CaseSource(nameof(OwnTypes))]
	public void Its_own_types_settle(string typeName)
		=> Assert.SettersSettleAfterOneWrite(TypeSweep.Construct(Subject, typeName));
}
