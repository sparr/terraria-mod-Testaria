using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// That a mod can read back what it writes into a world or a player file.
/// <para/>
/// The four checks are the ladder <see cref="PersistenceSweep"/> describes, and
/// they are separate results on purpose. Saving working, a save being readable,
/// a round trip changing nothing, and a load tolerating absent keys are four
/// different defects, and a single combined check would report the wrong one.
/// <para/>
/// Worth knowing before enabling this against a mod you care about: these call
/// the mod's real hooks on live content. Each one saves the current state first
/// and loads it back afterwards, so a mod whose load is the inverse of its save
/// is left as it was. A mod for which that is untrue is exactly what the
/// settling check reports, and such a subject may be left with its state moved.
/// Nothing a box can prevent, because world data is world-global and the
/// calibration notes already record that a box isolates a region and not a flag.
/// </summary>
[MutatesGlobalState]
public class PersistenceTests
{
	public static IEnumerable<string> Systems => PersistenceSweep.Systems();
	public static IEnumerable<string> Players => PersistenceSweep.Players();

	// ---- world data -------------------------------------------------------

	[LoadedTest]
	[CaseSource(nameof(Systems))]
	public void A_system_can_save(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.GetterReads);
		PersistenceSweep.SaveReads(PersistenceSweep.RequireSystem(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Systems))]
	public void A_system_can_read_back_its_own_save(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		PersistenceSweep.RoundTrips(PersistenceSweep.RequireSystem(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Systems))]
	public void A_system_survives_its_own_round_trip(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.Settling);
		PersistenceSweep.Settles(PersistenceSweep.RequireSystem(qualified));
	}

	/// <summary>
	/// The rung most likely to find something, because it is the one modelling
	/// what actually happens to players: a world written by an older build of
	/// the mod, whose tag is missing whatever the new build reads.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Systems))]
	public void A_system_can_load_a_tag_with_nothing_in_it(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		PersistenceSweep.LoadsAbsentKeys(PersistenceSweep.RequireSystem(qualified));
	}

	/// <summary>
	/// The other direction of the same question: a record holding more than this
	/// build writes, which is what a save from a build with a larger limit looks
	/// like. Neither the empty tag nor the round trip can reach it.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Systems))]
	public void A_system_survives_a_longer_record(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		PersistenceSweep.SurvivesALongerRecord(PersistenceSweep.RequireSystem(qualified));
	}

	// ---- player data ------------------------------------------------------

	[LoadedTest]
	[CaseSource(nameof(Players))]
	public void A_player_can_save(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.GetterReads);
		PersistenceSweep.SaveReads(PersistenceSweep.RequirePlayer(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Players))]
	public void A_player_can_read_back_its_own_save(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		PersistenceSweep.RoundTrips(PersistenceSweep.RequirePlayer(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Players))]
	public void A_player_survives_its_own_round_trip(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.Settling);
		PersistenceSweep.Settles(PersistenceSweep.RequirePlayer(qualified));
	}

	[LoadedTest]
	[CaseSource(nameof(Players))]
	public void A_player_can_load_a_tag_with_nothing_in_it(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		PersistenceSweep.LoadsAbsentKeys(PersistenceSweep.RequirePlayer(qualified));
	}

	/// <summary>
	/// The other direction of the same question: a record holding more than this
	/// build writes, which is what a save from a build with a larger limit looks
	/// like. Neither the empty tag nor the round trip can reach it.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Players))]
	public void A_player_survives_a_longer_record(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		PersistenceSweep.SurvivesALongerRecord(PersistenceSweep.RequirePlayer(qualified));
	}
}
