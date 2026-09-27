using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// That a mod's configuration survives being written out and read back.
/// <para/>
/// The same ladder as everywhere else, against the path tModLoader itself takes
/// on every multiplayer join. Nothing here writes to a live config: every rung
/// works on a copy, so a run cannot reset anybody's settings.
/// </summary>
public class ConfigTests
{
	public static IEnumerable<string> Configs => ConfigSweep.Every();

	[LoadedTest]
	[CaseSource(nameof(Configs))]
	public void A_config_can_be_written_out(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.GetterReads);
		ConfigSweep.SerializeReads(qualified);
	}

	[LoadedTest]
	[CaseSource(nameof(Configs))]
	public void A_config_can_be_read_back_into_a_copy(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		ConfigSweep.RoundTrips(qualified);
	}

	[LoadedTest]
	[CaseSource(nameof(Configs))]
	public void A_config_survives_its_own_round_trip(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.Settling);
		ConfigSweep.Settles(qualified);
	}

	/// <summary>
	/// The recovery path, which is the one nobody tests: tModLoader populates a
	/// config from an empty object when its file fails to load.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Configs))]
	public void A_config_can_be_populated_from_nothing(string qualified)
	{
		Exempt.Unless(qualified, SweepCheck.RoundTrip);
		ConfigSweep.PopulatesFromNothing(qualified);
	}
}
