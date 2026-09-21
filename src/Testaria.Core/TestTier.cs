namespace Testaria;

/// <summary>
/// Which execution environment a test needs.
/// <para/>
/// The tier is not a label, it is a hard boundary. A Tier 0 test that touches
/// loader state does not fail cleanly: in a bare test host those statics are
/// default-initialized rather than absent, so the test passes silently against
/// garbage. Declaring the tier is what lets a runner refuse to run a test
/// somewhere it cannot be trusted.
/// </summary>
public enum TestTier
{
	/// <summary>Pure logic, no loader state. Runs in a normal test host.</summary>
	Unit = 0,

	/// <summary>Needs a completed load pass, but no world.</summary>
	Loaded = 1,

	/// <summary>Needs a world and a running tick loop.</summary>
	World = 2,

	/// <summary>Needs a server and at least one client.</summary>
	MultiProcess = 3,
}
