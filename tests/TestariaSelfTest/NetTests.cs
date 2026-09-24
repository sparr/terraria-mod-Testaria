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
	[NetTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 300)]
	public IEnumerator A_client_is_connected(ITestContext ctx)
	{
		// The run itself is proof of the tier gate: this body only executes
		// when the session decided a client was present.
		Assert.Equal(NetmodeID.Server, Main.netMode);
		Assert.True(ClientLink.ConnectedClients > 0, "a client should be connected");
		Assert.InRange(ClientLink.FirstClient, 0, Main.maxPlayers - 1);

		yield break;
	}

	[NetTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 600)]
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

	[NetTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 900)]
	public IEnumerator A_tile_the_server_places_reaches_the_client(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		int x = ctx.Interior.Left + 4;
		int y = ctx.Interior.Top + 4;

		box.ClearTile(4, 4);

		// A client only knows the sections it has been sent, which are the ones
		// around its own player, and a box in the cavern is nowhere near where
		// a joining client spawns. Without this the client answers "no tile" to
		// everything here, whatever the server does.
		ClientLink.SendSection(x, y);
		yield return Wait.Ticks(10);

		box.PlaceTile(4, 4, TileID.Stone);

		// The edit is local until it is sent: the server changes its own copy
		// of the world and tells nobody. This line is as much the subject of
		// the test as the placement is.
		NetMessage.SendTileSquare(-1, x, y, 1);

		ClientLink.Request seen = ClientLink.AskTile(x, y);

		yield return Wait.Until(() => seen.Answered, "the client to report what it sees");

		Assert.Equal(TileID.Stone, seen.Value);
	}

	[NetTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 900)]
	public IEnumerator A_tile_the_server_keeps_to_itself_does_not_reach_the_client(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		int x = ctx.Interior.Left + 8;
		int y = ctx.Interior.Top + 4;

		box.ClearTile(8, 4);

		// The client is given this ground, so that what it reports afterwards
		// is about the tile edit rather than about never having heard of the
		// place. Without this the test would pass whatever happened.
		ClientLink.SendSection(x, y);
		yield return Wait.Ticks(10);

		ClientLink.Request before = ClientLink.AskTile(x, y);
		yield return Wait.Until(() => before.Answered, "the client to report the empty space");
		Assert.Equal(-1, before.Value);

		// Placed without being sent. The other half of the test above: if the
		// client saw this, the previous test would have proved nothing about
		// SendTileSquare, only that both sides happen to agree.
		box.PlaceTile(8, 4, TileID.Stone);
		yield return Wait.Ticks(30);

		ClientLink.Request after = ClientLink.AskTile(x, y);
		yield return Wait.Until(() => after.Answered, "the client to report again");

		Assert.Equal(-1, after.Value);
	}
}
