using System.Collections;
using Terraria;
using Terraria.ID;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// Tier 3: a server with a client attached to it.
/// <para/>
/// These run only when a client has actually connected. Without one they are
/// reported as skipped, never run single-player, because a netcode test that
/// quietly runs with nobody on the other end passes while proving nothing,
/// which is the failure this framework exists to prevent.
/// </summary>
public class NetTests
{
	[NetTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator A_client_is_connected(ITestContext ctx)
	{
		// The run itself is proof of the tier gate: this body only executes
		// when the session decided a client was present.
		Assert.Equal(NetmodeID.Server, Main.netMode);
		Assert.True(ClientLink.ConnectedClients > 0, "a client should be connected");
		Assert.InRange(ClientLink.FirstClient, 0, Main.maxPlayers - 1);

		yield break;
	}

	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator A_packet_makes_the_round_trip(ITestContext ctx)
	{
		ClientLink.Request ping = ClientLink.Ping();

		yield return Wait.Until(() => ping.Answered, "the client to answer the ping");

		// The client answers with its own player slot, and the server sees
		// which connection the answer arrived on. Those agreeing is what says
		// the round trip went to the client we asked and came back from it,
		// rather than being answered by anything closer to home.
		Assert.Equal(ping.From, ping.Value);
	}

	[NetTest(Band = Band.Cavern, Timeout = 1800)]
	public IEnumerator A_tile_the_server_places_reaches_the_client(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		int x = ctx.Interior.Left + 4;
		int y = ctx.Interior.Top + 4;

		box.ClearTile(4, 4);

		// Both sides agree the space is empty before the test changes
		// anything, so what follows is about the change rather than about a
		// section that had not arrived yet.
		yield return ClientLink.AwaitSection(x, y);

		box.PlaceTile(4, 4, TileID.Stone);

		// The edit is local until it is sent: the server changes its own copy
		// of the world and tells nobody. This line is as much the subject of
		// the test as the placement is.
		NetMessage.SendTileSquare(-1, x, y, 1);

		ClientLink.Request seen = ClientLink.AskTile(x, y);

		yield return Wait.Until(() => seen.Answered, "the client to report what it sees");

		Assert.Equal(TileID.Stone, seen.Value);
	}

	[NetTest(Band = Band.Cavern, Timeout = 900)]
	public IEnumerator A_client_knows_nothing_of_ground_it_was_never_sent(ITestContext ctx)
	{
		// The control for the test above. Without it, a client that simply
		// echoed whatever the server believed would look like perfect
		// synchronisation, and the tile test would prove nothing about sending.
		//
		// Far from the arena, which sits at the left of the world, and far from
		// where a joining client spawns, so this is ground nothing has had
		// reason to send. The blank world makes it solid stone.
		int x = Main.maxTilesX - 200;
		int y = (int)Main.rockLayer + 100;

		Assert.True(Main.tile[x, y].HasTile, "the blank world should have ground here for the server to see");

		ClientLink.Request seen = ClientLink.AskTile(x, y);
		yield return Wait.Until(() => seen.Answered, "the client to report ground it has never been sent");

		// The server sees stone; the client has never heard of the place. The
		// answers are the client's own, which is the whole point.
		Assert.Equal(-1, seen.Value);
	}

}
