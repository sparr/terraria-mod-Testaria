using System.Collections;
using Terraria;
using Terraria.ModLoader;
using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// That a mod's content survives being run, not merely being well formed.
/// <para/>
/// This is the one class of defect the tier 1 sweeps structurally cannot reach.
/// An AI that indexes <c>ai[]</c> it never initialised, or assumes a target, or
/// divides by a velocity that is zero on the first frame, passes every check that
/// reads content and throws the moment anything moves.
/// <para/>
/// <b>Off unless asked for.</b> Every case here leases a box, so the cost is a box
/// per piece of content across every enabled mod, where the rest of the sweep
/// costs a reflection call. Run with <c>BEHAVIOUR=1</c>.
/// <para/>
/// Each test pauses the world and drives the entity by hand. That is not an
/// optimisation: an exception thrown inside the game's own NPC or projectile loop
/// surfaces on the game thread, where a test cannot catch it and tModLoader may
/// swallow it. The same code called from the test is catchable and can name the
/// tick it failed on.
/// </summary>
public class BehaviourTests
{
	public static IEnumerable<string> Npcs => ContentSweep.Every<ModNPC>();
	public static IEnumerable<string> Projectiles => ContentSweep.Every<ModProjectile>();
	public static IEnumerable<string> PlainTiles => BehaviourSweep.PlainTiles();

	/// <summary>
	/// Stands down unless the run asked for these. A skip carrying its reason
	/// rather than an absent test, so a report cannot read as though the
	/// behaviour of every mod had been checked.
	/// </summary>
	private static void OnlyWhenAsked()
	{
		if (!TestSession.BehaviourRequested) {
			Assert.Skip("the behaviour tests were not asked for. They lease a box per piece "
				+ "of content, so they are off by default: run with BEHAVIOUR=1.");
		}
	}

	[GameTest(Band = Band.Cavern, Timeout = 900)]
	[CaseSource(nameof(Npcs))]
	public IEnumerator An_npc_survives_its_own_ai(ITestContext ctx, string qualified)
	{
		OnlyWhenAsked();

		var box = (TestContext)ctx;

		box.Pause();

		NPC npc = box.SpawnNPC(ContentSweep.Require<ModNPC>(qualified).Type, 8, 8);

		BehaviourSweep.AnNpcSurvivesItsOwnAi(npc, qualified);

		yield break;
	}

	[GameTest(Band = Band.Cavern, Timeout = 900)]
	[CaseSource(nameof(Projectiles))]
	public IEnumerator A_projectile_survives_its_own_ai(ITestContext ctx, string qualified)
	{
		OnlyWhenAsked();

		var box = (TestContext)ctx;

		box.Pause();

		Projectile projectile = box.SpawnProjectile(
			ContentSweep.Require<ModProjectile>(qualified).Type, 8, 8);

		BehaviourSweep.AProjectileSurvivesItsOwnAi(projectile, qualified);

		yield break;
	}

	/// <summary>
	/// A plain block goes into the world and comes out again, leaving nothing.
	/// <para/>
	/// Plain blocks only, and <see cref="BehaviourSweep.PlainTiles"/> explains
	/// why: a framed tile's placement preconditions belong to the content, and
	/// asking about them generically reports the sweep's ignorance as the mod's
	/// defect. ExampleMod's own suite already shows the cost of that, with a door
	/// reported as placing nothing because the test gave it no pocket to sit in.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 900)]
	[CaseSource(nameof(PlainTiles))]
	public IEnumerator A_plain_tile_is_placed_and_mined(ITestContext ctx, string qualified)
	{
		OnlyWhenAsked();

		var box = (TestContext)ctx;
		int type = ContentSweep.Require<ModTile>(qualified).Type;

		box.ClearTile(8, 8);
		box.PlaceTile(8, 8, type);

		yield return Wait.Ticks(2);

		Assert.Equal(type, (int)box.GetTile(8, 8).TileType,
			$"{qualified} did not go into the world when placed by the plain path");

		box.ClearTile(8, 8);

		yield return Wait.Ticks(2);

		Assert.False(box.GetTile(8, 8).HasTile,
			$"{qualified} was still there after being cleared");
	}
}
