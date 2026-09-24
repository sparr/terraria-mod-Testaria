using System.Collections;
using System.IO;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// Rendering, tested.
/// <para/>
/// Every tier runs its test body on the server, which draws nothing, so "a
/// client-only mod cannot be tested" looked like a fact about the design. It
/// is not. The tier 3 client is a real game process with a real graphics
/// device, and a client query runs arbitrary mod code inside it, so anything
/// that process can do is reachable: the work happens there and the answer
/// comes back as bytes for a test on the server to assert on.
/// <para/>
/// These read pixels the GPU actually produced. They are the proof that the
/// pattern reaches all the way to drawing, which is the furthest thing from
/// what a headless server can do.
/// </summary>
public class ClientRenderTests
{
	private readonly record struct Pixel(byte R, byte G, byte B);

	private static Pixel ReadPixel(BinaryReader reader)
		=> new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());

	[NetTest(Band = Band.Cavern, Timeout = 900)]
	public IEnumerator The_client_can_clear_a_render_target(ITestContext ctx)
	{
		ClientLink.Request answer = ClientLink.Ask(ClientRenderProbe.ClearedTarget, w => {
			w.Write((byte)12);
			w.Write((byte)34);
			w.Write((byte)56);
		});

		yield return Wait.Until(() => answer.Answered, "the client to clear a target and read it back");

		Assert.False(answer.Failed, answer.Error);

		BinaryReader reader = answer.Read();

		var expected = new Pixel(12, 34, 56);

		// Three pixels: two corners and the middle. A target that was never
		// drawn to reads back as zeroes, so this distinguishes "the GPU did
		// it" from "nothing happened".
		Assert.Equal(expected, ReadPixel(reader));
		Assert.Equal(expected, ReadPixel(reader));
		Assert.Equal(expected, ReadPixel(reader));
	}

	[NetTest(Band = Band.Cavern, Timeout = 900)]
	public IEnumerator The_client_can_draw_a_sprite(ITestContext ctx)
	{
		ClientLink.Request answer = ClientLink.Ask(ClientRenderProbe.DrawnSprite);

		yield return Wait.Until(() => answer.Answered, "the client to draw and read it back");

		Assert.False(answer.Failed, answer.Error);

		BinaryReader reader = answer.Read();

		Pixel covered = ReadPixel(reader);
		Pixel uncovered = ReadPixel(reader);

		// White where the sprite was drawn, black where it was not. Both
		// halves matter: all white would mean the clear did nothing, and all
		// black would mean the draw did.
		Assert.Equal(new Pixel(255, 255, 255), covered);
		Assert.Equal(new Pixel(0, 0, 0), uncovered);
	}
}
