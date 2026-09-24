using Microsoft.Xna.Framework;
using Terraria;
using Terraria.IO;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// Makes a client process join a server without anybody at the keyboard.
/// <para/>
/// Tier 3 needs a second process, and there is no launch parameter that joins
/// a server: <c>Main.AutoJoin</c> exists, but nothing on the command line
/// reaches it, and it still waits for a character to be chosen. Checked against
/// the decompiled 1.4.5.8 source rather than assumed. So the client has to be
/// driven from inside, by this, which is the one piece of Testaria that runs
/// on a client rather than a server.
/// <para/>
/// Everything here is gated on <see cref="JoinFlag"/>, so a person playing the
/// game with Testaria installed is never touched by it.
/// </summary>
public sealed class ClientAgent : ModSystem
{
	/// <summary>
	/// Launch parameter asking this client to join a server, as
	/// <c>host:port</c> or just <c>host</c>.
	/// </summary>
	public const string JoinFlag = "-testariajoin";

	/// <summary>Name given to the fabricated character.</summary>
	public const string PlayerName = "TestariaClient";

	// The menu needs a few frames before it will accept being driven: the
	// first update builds the interface this walks through.
	private const int SettleFrames = 30;

	private static int frames;
	private static bool started;
	private static bool announced;

	/// <summary>True when this process was asked to join a server.</summary>
	public static bool Requested => Program.LaunchParameters.ContainsKey(JoinFlag);

	/// <summary>What the agent has done so far, for the log and for diagnosis.</summary>
	public static string State { get; private set; } = "idle";

	/// <summary>
	/// Silences the first-run dialogs that would otherwise sit in front of
	/// everything, waiting for a click that is never coming.
	/// <para/>
	/// Measured rather than guessed: a client given a fresh save directory
	/// stops at "Select language" and then at "Welcome to tModLoader", and
	/// never loads a mod or reaches a menu. The language one is decided before
	/// any mod exists (<c>Main</c> reads <c>_needsLanguageSelect</c> from the
	/// configuration file at startup), so the harness seeds that; the rest are
	/// public flags on <c>ModLoader</c> that <c>Interface.ModLoaderMenus</c>
	/// consults after mods have loaded, which is late enough for this to reach
	/// them.
	/// <para/>
	/// Gated on the join flag, so a person running the game with Testaria
	/// installed still gets every message the loader means them to see.
	/// </summary>
	public override void Load()
	{
		if (Main.dedServ || !Requested)
			return;

		Mod.Logger.Info("Testaria client agent: armed, waiting for the menu to settle");

		ModLoader.ShowFirstLaunchWelcomeMessage = false;
		ModLoader.ShowWhatsNew = false;
		ModLoader.PreviewFreezeNotification = false;
		ModLoader.SeenNewUpdatedModsInfo = true;
		ModLoader.SeenFirstLaunchModderWelcomeMessage = true;
		ModLoader.WarnedFamilyShare = true;
		ModLoader.WarnedFamilyShareDontShowAgain = true;

		// A detour on the game's own update rather than ModSystem.UpdateUI.
		// Measured: UpdateUI is not called while the main menu is up, which is
		// the only time this agent has anything to do.
		Terraria.On_Main.Update += Tick;
	}

	private void Tick(Terraria.On_Main.orig_Update orig, Main self, GameTime gameTime)
	{
		orig(self, gameTime);

		AnnounceWhenInWorld();

		if (started)
			return;

		if (++frames < SettleFrames)
			return;

		started = true;

		try {
			Join(Program.LaunchParameters[JoinFlag]);
		}
		catch (Exception e) {
			State = "failed: " + e.Message;
			Mod.Logger.Error("Testaria client agent could not join", e);
		}
	}

	/// <inheritdoc/>
	public override void Unload()
	{
		Terraria.On_Main.Update -= Tick;

		// Static state outliving a reload would pin this assembly, and would
		// also make a reloaded client think it had already joined.
		frames = 0;
		started = false;
		announced = false;
		State = "idle";
	}

	/// <summary>
	/// Tells the server, once, that this client is in the world.
	/// <para/>
	/// The server's own "has joined" line means a connection was accepted, and
	/// the world the client asked for is still arriving for some time after
	/// it: measured, a client first saw its spawn block five ticks after a
	/// suite had already started running against it. A tier 3 test that edits
	/// a tile in that window sees the section turn up afterwards carrying the
	/// edit, which looks exactly like the server sending something nobody
	/// asked it to send. Being in the world is the honest readiness signal,
	/// because the client only gets there once its requested sections have
	/// arrived.
	/// </summary>
	private void AnnounceWhenInWorld()
	{
		if (announced || !Requested || Main.dedServ)
			return;

		if (Main.gameMenu || Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient)
			return;

		announced = true;
		State = "in the world";

		ModPacket packet = Mod.GetPacket();
		packet.Write((byte)ClientLink.Message.Ready);
		// Every packet carries a request id, so that one reader can serve them
		// all; this message answers nothing, so it carries a nought.
		packet.Write(0);
		packet.Send();

		Mod.Logger.Info("Testaria client agent: in the world, telling the server");
	}

	private void Join(string address)
	{
		(string host, int port) = Parse(address);

		// A character, fabricated rather than chosen. The server only needs a
		// player that exists; nothing here cares what it looks like.
		var player = new Player { name = PlayerName };

		PlayerFileData data = PlayerFileData.CreateAndSave(player);
		data.SetAsActive();

		Netplay.SetRemoteIP(host);
		Netplay.ListenPort = port;

		// The server runs with an empty password, and a prompt would wait
		// forever for someone who is not there.
		Main.autoPass = true;

		// The menu state the game itself moves to when it starts connecting,
		// so that the connection's own progress reporting has somewhere to go.
		Main.menuMode = 10;

		Netplay.StartTcpClient();

		State = $"joining {host}:{port} as {PlayerName}";
		Mod.Logger.Info("Testaria client agent " + State);
	}

	private static (string Host, int Port) Parse(string address)
	{
		string text = (address ?? string.Empty).Trim();

		if (text.Length == 0)
			throw new ArgumentException($"{JoinFlag} needs a host, optionally with a port.", nameof(address));

		int colon = text.LastIndexOf(':');

		if (colon < 0)
			return (text, Netplay.ListenPort);

		if (!int.TryParse(text[(colon + 1)..], out int port))
			throw new ArgumentException($"'{text[(colon + 1)..]}' is not a port.", nameof(address));

		return (text[..colon], port);
	}
}
