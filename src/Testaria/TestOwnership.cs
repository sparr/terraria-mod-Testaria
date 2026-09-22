using Terraria;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// Which entities belong to a running test.
/// <para/>
/// The first working piece of the ownership warden described in PLAN.md
/// section 2.4. For now it answers one question, "is this mine", which is
/// enough to stop the game reclaiming a test's entities out from under it.
/// </summary>
internal static class TestOwnership
{
	private static readonly HashSet<int> OwnedNpcs = [];

	public static void OwnNpc(int index) => OwnedNpcs.Add(index);

	public static void ReleaseNpc(int index) => OwnedNpcs.Remove(index);

	public static bool OwnsNpc(int index) => OwnedNpcs.Contains(index);

	/// <summary>
	/// Drops everything. Indices are slots the game reuses, so stale ownership
	/// would eventually protect an unrelated entity from despawning.
	/// </summary>
	public static void Clear() => OwnedNpcs.Clear();
}

/// <summary>
/// Keeps a test's entities alive for as long as the test is running.
/// <para/>
/// <c>NPC.CheckActive</c> despawns anything outside <c>activeRangeX/Y</c> of a
/// player. On a headless server there are no players, so *everything* is out
/// of range and a spawned NPC is reclaimed within about a second. Measured:
/// ExampleMod's critter spawned, was alive on the next tick, and was gone a
/// second later.
/// <para/>
/// That is the game behaving correctly and the test being wrong to assume
/// otherwise, so the fix belongs in the framework rather than in every test.
/// </summary>
public sealed class TestariaNPC : GlobalNPC
{
	/// <inheritdoc />
	public override bool CheckActive(NPC npc)
		=> !TestOwnership.OwnsNpc(npc.whoAmI);
}
