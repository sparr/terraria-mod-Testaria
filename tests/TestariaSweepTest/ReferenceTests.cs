using Terraria.ModLoader;
using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// That the IDs a piece of content points at exist.
/// <para/>
/// A field naming content that was never registered is an
/// <c>IndexOutOfRangeException</c> at the moment somebody uses the thing, often
/// on one client and in one situation, which is the worst kind of bug to find by
/// playing. Reading the sample costs nothing and answers it at load.
/// </summary>
public class ReferenceTests
{
	public static IEnumerable<string> Items => ContentSweep.Every<ModItem>();
	public static IEnumerable<string> Npcs => ContentSweep.Every<ModNPC>();

	[LoadedTest]
	[CaseSource(nameof(Items))]
	public void An_item_points_only_at_content_that_exists(string qualified)
		=> ReferenceSweep.ItemReferencesResolve(qualified);

	/// <summary>
	/// tModLoader computes this at load and writes a warning nobody reads. The
	/// only contribution here is making it a result.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Npcs))]
	public void An_npc_with_a_banner_item_declares_its_banner(string qualified)
		=> ReferenceSweep.NpcBannerAgrees(qualified);
}
