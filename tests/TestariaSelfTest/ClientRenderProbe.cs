using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// Draws something on the client and reports the pixels.
/// <para/>
/// This is the answer to "can a client-only mod be tested", worked out as far
/// as it goes. The README says rendering is out of scope, and for a test body
/// it is: a body runs on the server, which has no graphics device. But a
/// client query runs inside the client process, and that process has a real
/// one, on the framebuffer the harness already starts for it.
/// <para/>
/// So the pixels below are produced by the GPU, read back, and asserted on by
/// a test running in another process. Nothing here is a simulation of
/// drawing.
/// </summary>
public sealed class ClientRenderProbe : ModSystem
{
	/// <summary>Clears a render target to a colour and reads it back.</summary>
	public const string ClearedTarget = "TestariaSelfTest.ClearedTarget";

	/// <summary>Draws a sprite into a render target and reads it back.</summary>
	public const string DrawnSprite = "TestariaSelfTest.DrawnSprite";

	private const int Size = 8;

	/// <inheritdoc />
	public override void Load()
	{
		ClientQuery.Register(ClearedTarget, (arguments, answer) => {
			var colour = new Color(arguments.ReadByte(), arguments.ReadByte(), arguments.ReadByte());

			Color[] pixels = Render(device => device.Clear(colour));

			// The corners and the middle, which is enough to tell a cleared
			// target from an untouched one.
			Write(answer, pixels[0]);
			Write(answer, pixels[(Size * Size) - 1]);
			Write(answer, pixels[((Size / 2) * Size) + (Size / 2)]);
		});

		ClientQuery.Register(DrawnSprite, (_, answer) => {
			Color[] pixels = Render(device => {
				device.Clear(Color.Black);

				// A one pixel white texture, stretched over the left half.
				// Made here rather than loaded, so this tests the drawing
				// path rather than the asset pipeline.
				using var white = new Texture2D(device, 1, 1);
				white.SetData([Color.White]);

				using var batch = new SpriteBatch(device);

				batch.Begin();
				batch.Draw(white, new Rectangle(0, 0, Size / 2, Size), Color.White);
				batch.End();
			});

			// A pixel the sprite covers, and one it does not.
			Write(answer, pixels[0]);
			Write(answer, pixels[Size - 1]);
		});
	}

	/// <summary>
	/// Runs a drawing action into an off-screen target and reads the result.
	/// <para/>
	/// The previous render targets are put back afterwards. A client query
	/// runs while the game is running, and leaving the device pointed
	/// somewhere else would corrupt whatever it drew next.
	/// </summary>
	private static Color[] Render(Action<GraphicsDevice> draw)
	{
		GraphicsDevice device = Main.graphics.GraphicsDevice;
		RenderTargetBinding[] previous = device.GetRenderTargets();

		using var target = new RenderTarget2D(device, Size, Size);

		device.SetRenderTarget(target);

		try {
			draw(device);
		}
		finally {
			device.SetRenderTargets(previous);
		}

		Color[] pixels = new Color[Size * Size];
		target.GetData(pixels);

		return pixels;
	}

	private static void Write(System.IO.BinaryWriter answer, Color pixel)
	{
		answer.Write(pixel.R);
		answer.Write(pixel.G);
		answer.Write(pixel.B);
	}
}
