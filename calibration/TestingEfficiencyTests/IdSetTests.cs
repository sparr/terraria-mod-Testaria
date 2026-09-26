using Terraria.ID;
using Testaria;
using TestingEfficiency.DamageStats;

namespace TestingEfficiencyTests;

/// <summary>
/// The NPC ID sets the damage tracker is built on.
/// <para/>
/// Tier 1 by construction: an ID set is a bare array until a load pass sizes
/// it and fills it in, so none of this is answerable outside a loaded game.
/// The sets decide which NPC a hit is credited to, so an error here is not a
/// crash, it is a number in a report that is quietly wrong.
/// </summary>
public class IdSetTests
{
	/// <summary>
	/// Every set is sized to the loaded NPC count rather than to vanilla's.
	/// <para/>
	/// The failure this catches is specific: a set built before mods load, or
	/// not registered through the factory, stays at the vanilla length and
	/// then throws the moment a modded NPC is looked up in it. Reading it is
	/// the common case, so the exception arrives far from the cause.
	/// </summary>
	[LoadedTest]
	[Case("NpcToCountAs")]
	[Case("ShouldMergeInstances")]
	[Case("ShouldBlacklist")]
	[Case("ShouldTrackAsABoss")]
	[Case("BossKillTimes")]
	public void Set_is_sized_for_every_loaded_npc(string which)
	{
		int length = which switch {
			"NpcToCountAs" => IDSets.NpcToCountAs.Length,
			"ShouldMergeInstances" => IDSets.ShouldMergeInstances.Length,
			"ShouldBlacklist" => IDSets.ShouldBlacklist.Length,
			"ShouldTrackAsABoss" => IDSets.ShouldTrackAsABoss.Length,
			"BossKillTimes" => IDSets.BossKillTimes.Length,
			_ => -1,
		};

		Assert.Equal(NPCID.Count, length, $"{which} is sized {length}, not the loaded NPC count");
	}

	/// <summary>
	/// Every redirection points at an NPC that exists.
	/// <para/>
	/// An out of range or negative target would be read straight back into
	/// <c>Main.npc</c> indexing or a ContentSamples lookup by the tracker.
	/// </summary>
	[LoadedTest]
	public void Every_redirection_names_a_real_npc()
	{
		for (int type = 0; type < IDSets.NpcToCountAs.Length; type++) {
			int target = IDSets.NpcToCountAs[type];
			if (target == -1)
				continue;

			Assert.True(
				target >= 0 && target < NPCID.Count,
				$"NPC {type} is counted as {target}, which is not a loaded NPC type");
		}
	}

	/// <summary>
	/// No redirection points at something that is itself redirected.
	/// <para/>
	/// The tracker reads the set once, not in a loop, so a chain would credit
	/// damage to the middle of it and no assertion anywhere else would notice.
	/// Worth stating as an invariant even while it holds, because the set is
	/// hand written and a second hop is exactly what gets added by accident.
	/// </summary>
	[LoadedTest]
	public void No_redirection_is_itself_redirected()
	{
		for (int type = 0; type < IDSets.NpcToCountAs.Length; type++) {
			int target = IDSets.NpcToCountAs[type];
			if (target == -1)
				continue;

			Assert.Equal(
				-1, IDSets.NpcToCountAs[target],
				$"NPC {type} is counted as {target}, which is itself counted as "
				+ $"{IDSets.NpcToCountAs[target]}: a chain the tracker does not follow");
		}
	}

	/// <summary>Nothing is redirected to itself, which would say nothing and cost a lookup.</summary>
	[LoadedTest]
	public void No_redirection_is_a_loop()
	{
		for (int type = 0; type < IDSets.NpcToCountAs.Length; type++)
			Assert.NotEqual(type, IDSets.NpcToCountAs[type], $"NPC {type} is counted as itself");
	}

	/// <summary>
	/// The multi-part bosses the set exists for, one case each.
	/// <para/>
	/// Parameterised so a failure names the part rather than the loop: these
	/// are the mappings a Terraria update is most likely to invalidate, and
	/// knowing which one moved is the whole of the fix.
	/// </summary>
	[LoadedTest]
	[Case(NPCID.WallofFleshEye, NPCID.WallofFlesh)]
	[Case(NPCID.TheHungryII, NPCID.TheHungry)]
	[Case(NPCID.GolemFistLeft, NPCID.Golem)]
	[Case(NPCID.GolemFistRight, NPCID.Golem)]
	[Case(NPCID.GolemHead, NPCID.Golem)]
	[Case(NPCID.PrimeCannon, NPCID.SkeletronPrime)]
	[Case(NPCID.PrimeSaw, NPCID.SkeletronPrime)]
	[Case(NPCID.PrimeLaser, NPCID.SkeletronPrime)]
	[Case(NPCID.PrimeVice, NPCID.SkeletronPrime)]
	[Case(NPCID.SkeletronHand, NPCID.SkeletronHead)]
	[Case(NPCID.TheDestroyerBody, NPCID.TheDestroyer)]
	[Case(NPCID.TheDestroyerTail, NPCID.TheDestroyer)]
	[Case(NPCID.EaterofWorldsBody, NPCID.EaterofWorldsHead)]
	[Case(NPCID.EaterofWorldsTail, NPCID.EaterofWorldsHead)]
	public void Body_part_is_credited_to_its_boss(int part, int boss)
		=> Assert.Equal(boss, IDSets.NpcToCountAs[part],
			$"{part} should be counted as {boss}");

	/// <summary>
	/// A plain single-segment NPC is not redirected anywhere.
	/// <para/>
	/// The control for the cases above: without it they would all pass on a
	/// set that redirected everything.
	/// </summary>
	[LoadedTest]
	[Case(NPCID.BlueSlime)]
	[Case(NPCID.Zombie)]
	[Case(NPCID.EyeofCthulhu)]
	public void An_ordinary_npc_is_not_redirected(int type)
		=> Assert.Equal(-1, IDSets.NpcToCountAs[type], $"{type} should stand for itself");
}
