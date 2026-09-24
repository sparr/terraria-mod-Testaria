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

		/// <summary>
		/// Client telling the server it is in the world with the ground it
		/// asked for, which is later than the server's own "has joined".
		/// </summary>
		Ready = 7,

		/// <summary>Server asks a client a question the test mod registered.</summary>
		AskQuery = 8,

		/// <summary>Client's answer to a registered question, or why there is none.</summary>
		TellQuery = 9,
	}

	/// <summary>Clients that have told the server they are in the world.</summary>
	public static int ReadyClients { get; private set; }

	private static readonly Dictionary<int, Request> Pending = [];
	private static int nextId;

	/// <summary>
	/// How many questions one test may have waiting at once.
	/// <para/>
	/// A limit rather than no limit because the failure it prevents is awful:
	/// a question asked from inside a <c>Wait.Until</c> predicate is asked
	/// again every tick, which fills the server's send buffer and wedges its
	/// main thread. Measured, that took the whole process down, the watchdog
	/// reported "Server hung for more than 10 seconds", and four neighbouring
	/// tests failed with timeouts that pointed nowhere near the cause.
	/// <para/>
	/// Far above any sane test: a body asking sixty-four questions before
	/// waiting for any of them has almost certainly made this mistake.
	/// </summary>
	public const int MaxQuestionsInFlight = 64;

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

		/// <summary>
		/// The bytes a registered question answered with. Empty for the
		/// built-in questions, which answer with <see cref="Value"/>.
		/// </summary>
		public byte[] Payload { get; internal set; } = [];

		/// <summary>
		/// Why the client could not answer, or null when it did.
		/// <para/>
		/// A question the client does not have registered, or a handler that
		/// threw, arrives here rather than never arriving at all. A test
		/// waiting on a reply that is never coming reports a timeout, which
		/// says nothing about what went wrong; this says it.
		/// </summary>
		public string? Error { get; internal set; }

		/// <summary>True when the client answered with a reason rather than an answer.</summary>
		public bool Failed => Error is not null;

		/// <summary>
		/// Reads the answer a registered question sent back.
		/// <para/>
		/// Fails the test if the client could not answer, rather than handing
		/// back an empty reader that would be misread as a legitimate "no".
		/// </summary>
		public BinaryReader Read()
		{
			if (Error is not null)
				throw new AssertionException("The client could not answer: " + Error);

			if (!Answered)
				throw new AssertionException(
					"The client has not answered yet. Wait for Answered before reading, with "
					+ "Wait.Until(() => request.Answered, \"...\").");

			return new BinaryReader(new MemoryStream(Payload, writable: false));
		}
	}

	/// <summary>Forgets anything still in flight, between runs.</summary>
	public static void Clear()
	{
		lock (Pending)
			Pending.Clear();
	}

	/// <summary>
	/// Fails every waiting question if there is no longer a client to answer
	/// it.
	/// <para/>
	/// A client can die mid-run: it is a whole game process, and unlike the
	/// server it draws, so it meets a category of failure the server never
	/// does. Measured, a mod whose shader asset was missing took the client
	/// down at the first frame that wanted it, well after the harness had
	/// watched it join. Every tier 3 test then sat waiting for an answer that
	/// was never coming and failed on its tick budget, which reads as "the
	/// network is slow" rather than "the other process is gone".
	/// <para/>
	/// Called every tick while a run is in progress.
	/// </summary>
	public static void FailPendingIfClientsGone()
	{
		lock (Pending) {
			if (Pending.Count == 0 || ConnectedClients > 0)
				return;

			foreach (Request request in Pending.Values) {
				request.Error = "The client is no longer connected: it exited or crashed during the run. "
					+ "Its log is under clients/ in the scratch directory, which --keep-scratch preserves.";
				request.Answered = true;
			}

			Pending.Clear();
		}
	}

	/// <summary>Forgets which clients have reported in, on unload.</summary>
	public static void Reset()
	{
		Clear();
		ReadyClients = 0;
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
	/// Asks a client a question the test mod registered with
	/// <see cref="ClientQuery"/>.
	/// <para/>
	/// The extension point tier 3 was missing. Everything else here asks about
	/// vanilla state, because that is all the framework itself understands; a
	/// mod's own synced state, which is the entire reason a mod has netcode,
	/// needs the mod's own code to look at it. That code is present on the
	/// client, because a test mod is loaded on both sides, so it only needed a
	/// way to be reached.
	/// <para/>
	/// The answer is bytes, and <see cref="Request.Read"/> is how a test reads
	/// them back in the order the handler wrote them.
	/// </summary>
	/// <param name="name">The name the handler was registered under.</param>
	/// <param name="arguments">What to send it, or null for a question with none.</param>
	/// <param name="to">Client slot, or -1 for the first connected one.</param>
	/// <example>
	/// <code>
	/// ClientLink.Request seen = ClientLink.Ask("MyMod.SeesWidget", w => { w.Write(x); w.Write(y); });
	/// yield return Wait.Until(() => seen.Answered, "the client to answer");
	/// Assert.True(seen.Read().ReadBoolean());
	/// </code>
	/// </example>
	public static Request Ask(string name, Action<BinaryWriter>? arguments = null, int to = -1)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		byte[] payload;

		using (var buffer = new MemoryStream())
		using (var writer = new BinaryWriter(buffer)) {
			arguments?.Invoke(writer);
			writer.Flush();
			payload = buffer.ToArray();
		}

		return Ask(Message.AskQuery, to, packet => {
			packet.Write(name);
			packet.Write(payload.Length);
			packet.Write(payload);
		});
	}

	/// <summary>
	/// Asks a registered question over and over, with a gap, until the answer
	/// satisfies you.
	/// <para/>
	/// The shape almost every netcode test needs, and the one that is easy to
	/// get dangerously wrong by hand. Waiting for a change on the other side
	/// means asking more than once, and the obvious way to write that,
	/// <c>Wait.Until(() =&gt; Ask(...).Answered &amp;&amp; ...)</c>, asks again on every
	/// tick, because that is what a <c>Wait.Until</c> predicate does. Measured,
	/// that filled the send buffer, hung the server's main thread for the rest
	/// of the run, and failed four neighbouring tests with timeouts pointing
	/// nowhere near the cause.
	/// <para/>
	/// This asks once, waits for that answer, tests it, and only then waits a
	/// gap before asking again.
	/// </summary>
	/// <param name="name">The registered question to ask.</param>
	/// <param name="arguments">What to send it, or null.</param>
	/// <param name="satisfied">
	/// Reads the answer and says whether it is what the test was waiting for.
	/// </param>
	/// <param name="what">What is being waited for, for the timeout message.</param>
	/// <param name="gapTicks">Ticks to wait between asking again.</param>
	/// <param name="to">Client slot, or -1 for the first connected one.</param>
	public static System.Collections.IEnumerator AwaitAnswer(
		string name,
		Action<BinaryWriter>? arguments,
		Func<BinaryReader, bool> satisfied,
		string what,
		int gapTicks = 10,
		int to = -1)
	{
		ArgumentNullException.ThrowIfNull(satisfied);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gapTicks);

		while (true) {
			Request answer = Ask(name, arguments, to);

			yield return Wait.Until(() => answer.Answered, what);

			// A question the client cannot answer fails here rather than
			// spinning until the tick budget runs out, because the reason is
			// known now and a timeout would throw it away.
			if (answer.Failed)
				throw new AssertionException($"Waiting for {what}, but the client could not answer: {answer.Error}");

			if (satisfied(answer.Read()))
				yield break;

			yield return Wait.Ticks(gapTicks);
		}
	}

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

	/// <summary>
	/// Sends a client the section holding a tile, and waits until the client
	/// agrees with the server about what is there.
	/// <para/>
	/// Sending is not instant, and an unreceived section looks exactly like
	/// empty ground: the client answers "no tile" for everything in it. A test
	/// that reads that as an empty space is reading its own impatience.
	/// Measured, a section arrived after the test had gone on to place a tile
	/// and carried the new tile with it, so a test asserting the tile had not
	/// arrived failed while everything was working correctly.
	/// <para/>
	/// Waiting until the two sides agree about this one tile is the cheapest
	/// honest proof that the ground is really there. Set the tile up first,
	/// then call this, then make the change the test is about.
	/// </summary>
	/// <param name="x">Tile x.</param>
	/// <param name="y">Tile y.</param>
	/// <param name="to">Client slot, or -1 for the first connected one.</param>
	public static System.Collections.IEnumerator AwaitSection(int x, int y, int to = -1)
	{
		SendSection(x, y, to);

		// And the tile itself, because sending the section is not enough on
		// its own. Terraria remembers which sections a client has been given
		// and SendSection quietly does nothing for one it has already sent, so
		// a tile the server changed locally afterwards, which is every tile a
		// test touches, is never corrected on the client. The two sides then
		// disagree forever and the wait below spends the whole tick budget.
		//
		// Found the hard way: the first test in a section passed and every
		// later test in the same section timed out, which looks exactly like a
		// broken connection and is not.
		NetMessage.SendTileSquare(to >= 0 ? to : FirstClient, x, y, 1);

		while (true) {
			Request probe = AskTile(x, y, to);

			yield return Wait.Until(() => probe.Answered, $"the client to say what it sees at {x},{y}");

			if (probe.Value == SeenTile(x, y))
				yield break;

			yield return Wait.Ticks(10);
		}
	}

	private static Request Ask(Message message, int to, Action<BinaryWriter>? payload)
	{
		if (Main.netMode != NetmodeID.Server)
			throw new InvalidOperationException("Only the server asks questions; the client answers them.");

		int client = to >= 0 ? to : FirstClient;

		if (client < 0)
			throw new InvalidOperationException("No client is connected, so there is nobody to ask.");

		var request = new Request(++nextId);

		lock (Pending) {
			if (Pending.Count >= MaxQuestionsInFlight)
				throw new AssertionException(
					$"{Pending.Count} questions are already waiting for an answer, which is over the limit of "
					+ $"{MaxQuestionsInFlight}. The usual cause is asking from inside a Wait.Until predicate, "
					+ "which runs every tick and so asks again every tick. Ask once, wait for that answer, and "
					+ "ask again after a gap; ClientLink.AwaitAnswer does exactly that.");

			Pending[request.Id] = request;
		}

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

			case Message.AskQuery: {
				string name = reader.ReadString();
				int length = reader.ReadInt32();
				byte[] arguments = reader.ReadBytes(length);

				QueryAnswer answer = ClientQuery.Invoke(name, arguments);

				Reply(mod, Message.TellQuery, id, writer => {
					writer.Write(answer.Failed);

					if (answer.Failed) {
						writer.Write(answer.Error!);
					}
					else {
						writer.Write(answer.Payload.Length);
						writer.Write(answer.Payload);
					}
				});

				break;
			}

			case Message.TellQuery: {
				bool failed = reader.ReadBoolean();

				if (failed) {
					AnswerWithError(id, reader.ReadString(), whoAmI);
				}
				else {
					int length = reader.ReadInt32();
					AnswerWithPayload(id, reader.ReadBytes(length), whoAmI);
				}

				break;
			}

			case Message.Ready:
				ReadyClients++;
				// The line the harness waits for. "Has joined" is the server
				// accepting a connection, which happens well before the world
				// the client asked for has finished arriving: measured, a
				// client was still receiving its spawn block five ticks after
				// a suite had started running against it.
				mod.Logger.Info($"Testaria: client {whoAmI} is in the world and ready");

				// To the console as well as the log, because the harness reads
				// the server's standard output and the logger does not go
				// there.
				Console.WriteLine($"Testaria: client {whoAmI} is in the world and ready");
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
		=> Settle(id, from, request => request.Value = value);

	private static void AnswerWithPayload(int id, byte[] payload, int from)
		=> Settle(id, from, request => request.Payload = payload);

	private static void AnswerWithError(int id, string error, int from)
		=> Settle(id, from, request => request.Error = error);

	/// <summary>
	/// Marks a question answered, however it was answered.
	/// <para/>
	/// <c>Answered</c> is set last and the request removed with it, so a test
	/// that sees <c>Answered</c> is guaranteed to see the answer alongside it
	/// rather than racing the fields that carry it.
	/// </summary>
	private static void Settle(int id, int from, Action<Request> fill)
	{
		lock (Pending) {
			if (!Pending.TryGetValue(id, out Request? request))
				return;

			fill(request);
			request.From = from;
			request.Answered = true;
			Pending.Remove(id);
		}
	}
}
