using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// The server's handle on a connected test client.
/// <para/>
/// Tier 3 is asymmetric on purpose. The run lives on the server: it owns the
/// arena, the scheduler, and the report, and it is the only side that decides
/// anything. The client is a puppet that answers questions about what it can
/// see. The alternative, running the same test body on both sides and
/// reconciling two verdicts, doubles everything that can go wrong and gives a
/// failure two places to hide.
/// <para/>
/// A question is asked by sending a packet and is answered by another, so an
/// answer arrives some number of ticks later. Every request is therefore a
/// handle a test waits on, never a value a test reads.
/// </summary>
public static class ClientLink
{
	/// <summary>Testaria's own packet kinds. The leading byte of every packet.</summary>
	public enum Message : byte
	{
		/// <summary>Server asks a client to answer immediately.</summary>
		Ping = 1,

		/// <summary>Client's answer to a ping.</summary>
		Pong = 2,

		/// <summary>Server asks a client what tile it sees at a position.</summary>
		AskTile = 3,

		/// <summary>Client's answer about a tile.</summary>
		TellTile = 4,

		/// <summary>Server asks a client what it sees in an NPC slot.</summary>
		AskNpc = 5,

		/// <summary>Client's answer about an NPC slot.</summary>
		TellNpc = 6,
	}

	private static readonly Dictionary<int, Request> Pending = [];
	private static int nextId;

	/// <summary>One question, and the answer when it arrives.</summary>
	public sealed class Request
	{
		internal Request(int id) => Id = id;

		/// <summary>Identifies this question among those in flight.</summary>
		public int Id { get; }

		/// <summary>True once the client has answered.</summary>
		public bool Answered { get; internal set; }

		/// <summary>The answer, meaning depends on the question.</summary>
		public int Value { get; internal set; }

		/// <summary>Which client answered.</summary>
		public int From { get; internal set; } = -1;
	}

	/// <summary>Forgets anything still in flight, between runs.</summary>
	public static void Clear()
	{
		lock (Pending)
			Pending.Clear();
	}

	/// <summary>How many clients are connected and playing.</summary>
	public static int ConnectedClients
	{
		get {
			int count = 0;

			for (int i = 0; i < Main.maxPlayers; i++) {
				if (Netplay.Clients[i].IsActive)
					count++;
			}

			return count;
		}
	}

	/// <summary>The player slot of the first connected client, or -1.</summary>
	public static int FirstClient
	{
		get {
			for (int i = 0; i < Main.maxPlayers; i++) {
				if (Netplay.Clients[i].IsActive)
					return i;
			}

			return -1;
		}
	}

	/// <summary>Asks a client to answer, which is the smallest round trip there is.</summary>
	/// <param name="to">Client slot, or -1 for the first connected one.</param>
	public static Request Ping(int to = -1) => Ask(Message.Ping, to, null);

	/// <summary>Asks a client what tile it sees at a tile position.</summary>
	/// <param name="x">Tile x.</param>
	/// <param name="y">Tile y.</param>
	/// <param name="to">Client slot, or -1 for the first connected one.</param>
	/// <returns>
	/// A request whose <see cref="Request.Value"/> is the tile type the client
	/// sees, or -1 where the client sees no tile at all.
	/// </returns>
	public static Request AskTile(int x, int y, int to = -1)
		=> Ask(Message.AskTile, to, writer => {
			writer.Write(x);
			writer.Write(y);
		});

	/// <summary>
	/// Asks a client what it sees in an NPC slot.
	/// <para/>
	/// Slots are the same array index on both sides, since the server assigns
	/// them and syncs by index, so this is a fair question to ask.
	/// </summary>
	/// <param name="index">The slot in <c>Main.npc</c>.</param>
	/// <param name="to">Client slot, or -1 for the first connected one.</param>
	/// <returns>
	/// A request whose <see cref="Request.Value"/> is the NPC type the client
	/// sees there, or -1 when the client has nothing active in that slot.
	/// </returns>
	public static Request AskNpc(int index, int to = -1)
		=> Ask(Message.AskNpc, to, writer => writer.Write(index));

	/// <summary>
	/// Sends a client the world section holding a tile position.
	/// <para/>
	/// A client is only told about the sections it has been sent, which are the
	/// ones near its own player. A test box in the cavern is nowhere near where
	/// a joining client spawns, so a tile edit there means nothing to the
	/// client until it has the ground it sits in. Found by a test that placed a
	/// tile, sent it, and was told the client saw nothing at all.
	/// </summary>
	/// <param name="x">Tile x anywhere in the wanted section.</param>
	/// <param name="y">Tile y anywhere in the wanted section.</param>
	/// <param name="to">Client slot, or -1 for the first connected one.</param>
	public static void SendSection(int x, int y, int to = -1)
	{
		if (Main.netMode != NetmodeID.Server)
			throw new InvalidOperationException("Only the server sends world sections.");

		int client = to >= 0 ? to : FirstClient;

		if (client < 0)
			throw new InvalidOperationException("No client is connected, so there is nobody to send to.");

		NetMessage.SendSection(client, Netplay.GetSectionX(x), Netplay.GetSectionY(y));
	}

	private static Request Ask(Message message, int to, Action<BinaryWriter>? payload)
	{
		if (Main.netMode != NetmodeID.Server)
			throw new InvalidOperationException("Only the server asks questions; the client answers them.");

		int client = to >= 0 ? to : FirstClient;

		if (client < 0)
			throw new InvalidOperationException("No client is connected, so there is nobody to ask.");

		var request = new Request(++nextId);

		lock (Pending)
			Pending[request.Id] = request;

		ModPacket packet = ModContent.GetInstance<TestariaMod>().GetPacket();
		packet.Write((byte)message);
		packet.Write(request.Id);
		payload?.Invoke(packet);
		packet.Send(client);

		return request;
	}

	/// <summary>Handles a packet on whichever side received it.</summary>
	internal static void Receive(Mod mod, Message message, BinaryReader reader, int whoAmI)
	{
		int id = reader.ReadInt32();

		switch (message) {
			case Message.Ping:
				Reply(mod, Message.Pong, id, writer => writer.Write(Main.myPlayer));
				break;

			case Message.AskTile:
				int x = reader.ReadInt32();
				int y = reader.ReadInt32();
				Reply(mod, Message.TellTile, id, writer => writer.Write(SeenTile(x, y)));
				break;

			case Message.AskNpc:
				int index = reader.ReadInt32();
				Reply(mod, Message.TellNpc, id, writer => writer.Write(SeenNpc(index)));
				break;

			case Message.Pong:
			case Message.TellTile:
			case Message.TellNpc:
				Answer(id, reader.ReadInt32(), whoAmI);
				break;

			default:
				// A packet kind this build does not know is a version mismatch
				// between the two processes, which is worth saying out loud
				// rather than ignoring: the run that follows would be testing
				// something other than what was built.
				mod.Logger.Warn($"Testaria received an unknown packet kind {message} from {whoAmI}");
				break;
		}
	}

	// -1 rather than 0 for "no tile here", because 0 is a real tile type
	// (dirt) and a test asserting on an empty space would otherwise be told
	// the client sees dirt.
	private static int SeenTile(int x, int y)
	{
		if (!WorldGen.InWorld(x, y))
			return -1;

		Tile tile = Main.tile[x, y];

		return tile.HasTile ? tile.TileType : -1;
	}

	// -1 for "nothing here", for the same reason as tiles: type 0 is a real
	// NPC and a test asking about an empty slot should not be told about it.
	private static int SeenNpc(int index)
	{
		if (index < 0 || index >= Main.npc.Length)
			return -1;

		NPC npc = Main.npc[index];

		return npc.active ? npc.type : -1;
	}

	private static void Reply(Mod mod, Message message, int id, Action<BinaryWriter> payload)
	{
		ModPacket packet = mod.GetPacket();
		packet.Write((byte)message);
		packet.Write(id);
		payload(packet);
		packet.Send();
	}

	private static void Answer(int id, int value, int from)
	{
		lock (Pending) {
			if (!Pending.TryGetValue(id, out Request? request))
				return;

			request.Value = value;
			request.From = from;
			request.Answered = true;
			Pending.Remove(id);
		}
	}
}
