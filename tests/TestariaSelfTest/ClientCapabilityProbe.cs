using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// Asks the tier 3 client what it can actually do.
/// <para/>
/// The question behind this: whether a client-only mod, which is most of the
/// most-subscribed ones, has anything a test can reach. Every Testaria tier
/// runs its test body on the server, and a server draws nothing, so the
/// conclusion looked obvious. It is not, because the tier 3 client is a real
/// game process on a real framebuffer, and a client query runs arbitrary mod
/// code inside it.
/// <para/>
/// So rather than reason about it, this reports what is present over there.
/// </summary>
public sealed class ClientCapabilityProbe : ModSystem
{
	/// <summary>Reports the client's capabilities as lines of text.</summary>
	public const string Capabilities = "TestariaSelfTest.Capabilities";

	/// <inheritdoc />
	public override void Load()
		=> ClientQuery.Register(Capabilities, (_, answer) => answer.Write(Describe()));

	private static string Describe()
	{
		var report = new StringBuilder();

		void Line(string name, object? value) => report.Append(name).Append(" = ").Append(value).Append('\n');

		Line("netMode", Main.netMode);
		Line("dedServ", Main.dedServ);
		Line("gameMenu", Main.gameMenu);
		Line("instance", Main.instance is not null);

		// The graphics device is the whole question: with one, textures,
		// render targets and the back buffer all exist, and a UI framework's
		// layout and drawing become reachable.
		Line("graphics", Main.graphics is not null);
		Line("GraphicsDevice", Try(() => Main.graphics?.GraphicsDevice is not null));
		Line("screen", $"{Main.screenWidth}x{Main.screenHeight}");
		Line("backBuffer", Try(() => {
			GraphicsDevice device = Main.graphics!.GraphicsDevice;

			return $"{device.PresentationParameters.BackBufferWidth}x{device.PresentationParameters.BackBufferHeight}";
		}));
		Line("UIScale", Main.UIScale);
		Line("spriteBatch", Main.spriteBatch is not null);

		// Can this process make a render target, which is what any framework
		// that composites its own UI needs?
		Line("newRenderTarget", Try(() => {
			using var target = new RenderTarget2D(Main.graphics!.GraphicsDevice, 16, 16);

			return $"{target.Width}x{target.Height}";
		}));

		// Assets: a client loads real textures, a server loads none.
		Line("assetRepo", Main.Assets is not null);
		Line("itemTextureLoaded", Try(() => TextureAssets.Item[1].IsLoaded));
		Line("itemTextureSize", Try(() => {
			Texture2D texture = TextureAssets.Item[1].Value;

			return $"{texture.Width}x{texture.Height}";
		}));

		// A local player exists on a client and does not on a server, which is
		// what a great deal of client-side mod code reads.
		Line("myPlayer", Main.myPlayer);
		Line("localPlayerActive", Try(() => Main.LocalPlayer.active));
		Line("localPlayerName", Try(() => Main.LocalPlayer.name));

		// The UI layer a client-side mod hangs itself off.
		Line("mouseX", Main.mouseX);
		Line("fontMouseText", Try(() => FontAssets.MouseText?.Value is not null));

		return report.ToString();
	}

	/// <summary>
	/// Reports what a call produced, including the exception if it threw.
	/// <para/>
	/// The failures are the interesting half: "this throws on a client" is the
	/// answer for anything a client-only mod cannot be tested for.
	/// </summary>
	private static string Try<T>(Func<T> probe)
	{
		try {
			return probe()?.ToString() ?? "null";
		}
		catch (Exception ex) {
			return $"threw {ex.GetType().Name}";
		}
	}
}
