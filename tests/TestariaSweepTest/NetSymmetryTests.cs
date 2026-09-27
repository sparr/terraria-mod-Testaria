using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// That the two halves of a mod's networking agree.
/// <para/>
/// A tier 1 suite about netcode, which is the point: the disagreement is between
/// two methods in one assembly, so proving it needs no client and no world. Tier
/// 3 proves a packet arrives; this proves that what arrives can be read.
/// <para/>
/// What this covers and does not is worth stating, because the limit is real. It
/// exercises each pair against a freshly defaulted entity, so it proves the
/// halves agree about the default state and says nothing about branches taken
/// only when a field has moved. That catches the common asymmetry, which is
/// unconditional: a field added to one side, a width that does not match. It
/// does not catch a conditional one, and it cannot catch a guard that reads
/// machine-local state at all. Daybreak's handler opens both halves with
/// <c>if (Mod.NetID &lt; 0) return;</c>, which is symmetric in the source, reads
/// state local to one machine, and can therefore still have the two ends
/// disagree in a real session. A local round trip proves the methods agree about
/// a given state, not that two machines are in it.
/// </summary>
[MutatesGlobalState]
public class NetSymmetryTests
{
	public static IEnumerable<string> Npcs => NetSweep.Npcs();
	public static IEnumerable<string> Projectiles => NetSweep.Projectiles();
	public static IEnumerable<string> Systems => NetSweep.Systems();

	// ---- NPCs -------------------------------------------------------------

	[LoadedTest]
	[CaseSource(nameof(Npcs))]
	public void An_npc_can_send_its_extra_ai(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.GetterReads);
		NetSweep.WriteReads(NetSweep.RequireNpc(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Npcs))]
	public void An_npc_reads_back_exactly_what_it_sent(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		NetSweep.RoundTrips(NetSweep.RequireNpc(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Npcs))]
	public void An_npc_would_send_on_what_it_received(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.Settling);
		NetSweep.Settles(NetSweep.RequireNpc(qualified));
	}

	// ---- projectiles ------------------------------------------------------

	[LoadedTest]
	[CaseSource(nameof(Projectiles))]
	public void A_projectile_can_send_its_extra_ai(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.GetterReads);
		NetSweep.WriteReads(NetSweep.RequireProjectile(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Projectiles))]
	public void A_projectile_reads_back_exactly_what_it_sent(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		NetSweep.RoundTrips(NetSweep.RequireProjectile(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Projectiles))]
	public void A_projectile_would_send_on_what_it_received(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.Settling);
		NetSweep.Settles(NetSweep.RequireProjectile(qualified));
	}

	// ---- world systems ----------------------------------------------------

	[LoadedTest]
	[CaseSource(nameof(Systems))]
	public void A_system_can_send_its_state(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.GetterReads);
		NetSweep.WriteReads(NetSweep.RequireSystem(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Systems))]
	public void A_system_reads_back_exactly_what_it_sent(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		NetSweep.RoundTrips(NetSweep.RequireSystem(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Systems))]
	public void A_system_would_send_on_what_it_received(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.Settling);
		NetSweep.Settles(NetSweep.RequireSystem(qualified));
	}
}
