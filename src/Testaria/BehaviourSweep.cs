using Terraria;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// Whether a mod's content survives being run, as opposed to merely being
/// well formed.
/// <para/>
/// Everything else in the sweep reads content. This runs it, which is the one
/// class the tier 1 checks structurally cannot reach: an AI that indexes
/// <c>ai[]</c> it never initialised, or assumes a target exists, or divides by a
/// velocity that is zero on the first frame, is perfectly well formed and throws
/// the moment anything moves.
/// <para/>
/// Expensive, and behind an opt-in for that reason: a box per piece of content,
/// across every mod a run enables. See <c>TestSession.BehaviourFlag</c>.
/// <para/>
/// The entity is driven by hand rather than by letting the world tick it, and
/// that is the whole reason this can assert anything. An exception thrown inside
/// <c>Main.UpdateWorld_NPCs</c> surfaces on the game thread, where a test cannot
/// catch it and tModLoader may swallow it; the same code called directly from
/// the test is catchable and nameable. The tests pause the world first so that
/// nothing is updated twice.
/// </summary>
public static class BehaviourSweep
{
	/// <summary>How many ticks of behaviour to ask for. Two seconds at 60 per second.</summary>
	public const int Ticks = 120;

	/// <summary>
	/// Runs an NPC's own update for a while and asserts it does not throw.
	/// <para/>
	/// The full <c>UpdateNPC</c> rather than <c>AI</c> alone, because the
	/// bookkeeping around the AI is part of what a real tick does and part of
	/// what a fragile AI depends on.
	/// <para/>
	/// An NPC that stops being active is not a failure. Despawning is ordinary
	/// behaviour and plenty of NPCs do it immediately on a server with no
	/// players nearby; the run simply stops there, having asked what it could.
	/// </summary>
	public static void AnNpcSurvivesItsOwnAi(NPC npc, string qualified)
	{
		for (int tick = 0; tick < Ticks; tick++) {
			if (!npc.active)
				return;

			try {
				npc.UpdateNPC(npc.whoAmI);
			}
			catch (Exception bad) {
				Assert.Fail($"{qualified} threw on tick {tick + 1} of its own update: "
					+ $"{Describe(bad)}. In a real world this is thrown inside the game's "
					+ "NPC loop, where nothing can catch it on the mod's behalf");
			}
		}
	}

	/// <summary>
	/// Runs a projectile's own update for a while and asserts it does not throw.
	/// <para/>
	/// Only that it does not throw. An earlier draft also asked that the
	/// projectile expire within its own <c>timeLeft</c>, on the reasoning that
	/// one which never dies leaks the thousand-slot pool until nothing in the
	/// world can shoot anything. That claim was withdrawn: a minion lives as long
	/// as its buff and resets <c>timeLeft</c> every tick by design, and so do
	/// held projectiles, so "makes progress toward expiry" is not an invariant at
	/// all. Detecting a genuine leak needs to know which kind a projectile is,
	/// which is the knowledge a sweep does not have.
	/// </summary>
	public static void AProjectileSurvivesItsOwnAi(Projectile projectile, string qualified)
	{
		for (int tick = 0; tick < Ticks; tick++) {
			if (!projectile.active)
				return;

			try {
				projectile.Update(projectile.whoAmI);
			}
			catch (Exception bad) {
				Assert.Fail($"{qualified} threw on tick {tick + 1} of its own update: "
					+ $"{Describe(bad)}. In a real world this is thrown inside the game's "
					+ "projectile loop, where nothing can catch it on the mod's behalf");
			}
		}
	}

	/// <summary>
	/// Every <c>ModTile</c> a sweep can reasonably try to place and mine.
	/// <para/>
	/// Plain blocks only, and the narrowing is the point rather than a
	/// convenience. A framed tile has placement preconditions that belong to the
	/// content: an anchor, a pocket of a particular size, a surface of a
	/// particular kind. ExampleMod's own suite already shows what ignoring that
	/// costs, with a door reported as placing nothing because the test did not
	/// give it a three-tile pocket. Asking those questions generically would
	/// report the sweep's ignorance as the mod's defect.
	/// <para/>
	/// What is left is the tile that should go anywhere: a solid block, not framed,
	/// so placed by the plain path with no object data to satisfy.
	/// <para/>
	/// Solid is the second half of the rule and it was measured rather than
	/// guessed. Excluding only framed tiles still caught ExampleMod's vine, which
	/// is not framed and still will not hang in mid-air, so the sweep reported a
	/// vine for not being a brick. A solid block is the one shape with no
	/// placement precondition beyond an empty space.
	/// </summary>
	public static IEnumerable<string> PlainTiles()
		=> ContentSweep.Every<ModTile>()
			.Where(IsPlain)
			.Order();

	/// <summary>Whether a swept tile is a plain block rather than a framed object.</summary>
	private static bool IsPlain(string qualified)
	{
		int split = qualified.IndexOf(ContentSweep.Separator);

		if (split <= 0 || !ModContent.TryFind(qualified[..split], qualified[(split + 1)..], out ModTile tile))
			return false;

		// No lower bound: ModTile.Type is a ushort, so it cannot be negative and
		// the compiler says so.
		return tile.Type < Main.tileFrameImportant.Length
			&& !Main.tileFrameImportant[tile.Type]
			&& tile.Type < Main.tileSolid.Length
			&& Main.tileSolid[tile.Type];
	}

	private static string Describe(Exception thrown)
		=> $"{thrown.GetType().Name}: {thrown.Message}";
}
