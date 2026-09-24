using System.Collections;
using Terraria;
using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// Tier 3 against somebody else's mod.
/// <para/>
/// The calibration argument from PLAN.md section 8.3, applied to netcode: a
/// framework's own self-tests are written by the person who wrote the
/// framework, and agree with it by construction. Pointing the same machinery
/// at a mod maintained by the people who wrote the netcode is what turns "my
/// tests pass" into "this measures something".
/// <para/>
/// The property under test throughout is the one that makes modded multiplayer
/// work at all: <b>modded content ids are assigned per load and synced between
/// server and client</b>, so an id that means ExampleBlock on the server has to
/// mean ExampleBlock on the client. Nothing here hardcodes an id; every test
/// resolves one by name on the server and checks what the client reports back.
/// </summary>
public class NetTests
{
	[NetTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 900)]
	public IEnumerator A_modded_tile_reaches_the_client_as_the_same_content(ITestContext ctx)
	{
		Subject.Require();

		if (!ModContent.TryFind(Subject.Name, "ExampleBlock", out ModTile block))
			Assert.Skip("ExampleBlock is not registered, so there is no modded tile to send.");

		var box = (TestContext)ctx;
		int x = ctx.Interior.Left + 4;
		int y = ctx.Interior.Top + 4;

		box.ClearTile(4, 4);
		ClientLink.SendSection(x, y);
		yield return Wait.Ticks(10);

		box.PlaceTile(4, 4, block.Type);
		NetMessage.SendTileSquare(-1, x, y, 1);

		ClientLink.Request seen = ClientLink.AskTile(x, y);
		yield return Wait.Until(() => seen.Answered, "the client to report the tile");

		// The id itself is meaningless, which is the point: it is assigned at
		// load and is not the same number from one session to the next. What
		// matters is that both processes agree on it.
		Assert.Equal(block.Type, seen.Value);
		Assert.True(block.Type >= Terraria.ID.TileID.Count, "a modded tile should have an id above the vanilla range");
	}

	[NetTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 900)]
	public IEnumerator A_modded_npc_the_server_spawns_reaches_the_client(ITestContext ctx)
	{
		Subject.Require();

		if (!ModContent.TryFind(Subject.Name, "ExampleCritterNPC", out ModNPC critter))
			Assert.Skip("ExampleCritterNPC is not registered, so there is no modded NPC to spawn.");

		var box = (TestContext)ctx;

		// The client is given the ground first: an NPC in a section the client
		// has never heard of is not a test of NPC sync.
		ClientLink.SendSection(ctx.Interior.Left, ctx.Interior.Top);
		yield return Wait.Ticks(10);

		NPC spawned = box.SpawnNPC(critter.Type, 8, 8);

		// Spawning on a server syncs by itself; this is the wait for it to
		// happen rather than a nudge to make it happen.
		yield return Wait.Ticks(30);

		ClientLink.Request seen = ClientLink.AskNpc(spawned.whoAmI);
		yield return Wait.Until(() => seen.Answered, "the client to report the NPC slot");

		Assert.Equal(critter.Type, seen.Value);
	}

	[NetTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 900)]
	public IEnumerator An_empty_slot_is_empty_on_both_sides(ITestContext ctx)
	{
		Subject.Require();

		// The other half of the test above. Without it, a client that answered
		// "the same id" to everything would look like perfect synchronisation.
		int unused = Main.npc.Length - 1;

		Assert.False(Main.npc[unused].active, "the last NPC slot should be free in a test world");

		ClientLink.Request seen = ClientLink.AskNpc(unused);
		yield return Wait.Until(() => seen.Answered, "the client to report the empty slot");

		Assert.Equal(-1, seen.Value);
	}
}
