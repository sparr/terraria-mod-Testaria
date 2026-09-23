using Terraria;
using Terraria.DataStructures;
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
	/// <summary>
	/// Adopts an NPC the world produced from a tile inside the running test's
	/// box.
	/// <para/>
	/// Tests cause spawns without asking for them. Breaking one of ExampleMod's
	/// natural rubble tiles has a one in six chance of releasing a worm, which
	/// is the mod working as designed, and without this the test that broke the
	/// tile is failed for contamination it produced itself.
	/// <para/>
	/// Narrowed to tile provenance rather than "anything born in the box". An
	/// NPC conjured by a spawn tool, a cheat menu, or another test carries a
	/// different source and is still reported: adopting those makes
	/// contamination undetectable, and the red-path gate says so.
	/// </summary>
	public override void OnSpawn(NPC npc, IEntitySource source)
	{
		if (source is not AEntitySource_Tile)
			return;

		if (TestariaSystem.Current?.CurrentContext is not TestContext context || context.Interior.IsEmpty)
			return;

		if (BoxSpace.Contains(context.Interior, new WorldPoint(npc.Center.X, npc.Center.Y)))
			context.Adopt(npc);
	}

	/// <inheritdoc />
	public override bool CheckActive(NPC npc)
		=> !TestOwnership.OwnsNpc(npc.whoAmI);

	/// <summary>
	/// Stops the game spawning its own NPCs while a test is running.
	/// <para/>
	/// Natural spawning is driven entirely by proximity to a player, so it
	/// never happened while the server had none. The moment a test puts a
	/// player in its box, the game starts populating the area around it, and
	/// those arrivals are contamination by any reasonable definition. Better
	/// not to create them than to detect them.
	/// </summary>
	public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
	{
		if (!TestariaSystem.IsRunning)
			return;

		spawnRate = int.MaxValue;
		maxSpawns = 0;
	}
}
