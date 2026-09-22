using System.Collections;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Testaria;

namespace TestariaRedTest;

/// <summary>
/// Tests that are meant to fail, and to fail in four distinguishable ways.
/// <para/>
/// A framework that can only report green is indistinguishable from one that
/// works. Nothing here proves the framework is correct; what it proves is that
/// a real failure survives the whole trip, from assertion through the
/// coroutine and the runner into JUnit XML and out as a non-zero exit code.
/// <para/>
/// This mod is only ever enabled by the red check, never by an ordinary run.
/// </summary>
public class RedTests
{
	[LoadedTest]
	public void Deliberately_fails_an_assertion()
		=> Assert.Equal(1, 2, "deliberate assertion failure");

	[LoadedTest]
	public void Deliberately_throws()
		=> throw new InvalidOperationException("deliberate error, not an assertion");

	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 30)]
	public IEnumerator Deliberately_times_out()
	{
		// Never completes. The runner should abandon it at the tick budget and
		// report a failure rather than an error, since the usual cause of a
		// timeout is the subject never doing the thing.
		while (true)
			yield return null;
	}

	/// <summary>
	/// Contaminates its own box on purpose, by spawning an NPC directly rather
	/// than through the context, so the framework never learns it is ours.
	/// <para/>
	/// Every assertion here holds, so without the warden this would report a
	/// clean pass from a box something else was living in. It should be
	/// downgraded to an error instead: not a failure, which would blame a
	/// subject that is fine, and certainly not a pass.
	/// </summary>
	[GameTest(Band = Band.Cavern, Width = 48, Height = 32, Timeout = 120)]
	public IEnumerator Deliberately_contaminates_its_own_box(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		WorldPoint where = box.At(8, 8);

		int index = NPC.NewNPC(new EntitySource_DebugCommand("RedTest"), (int)where.X, (int)where.Y, NPCID.BlueSlime);

		Assert.True(index >= 0 && index < Main.npc.Length, $"spawn should return a slot, got {index}");

		// It will be despawned shortly, being far from any player, which is
		// precisely why the watcher has to run before entity updates rather
		// than after them.
		yield return Wait.Ticks(5);

		Assert.True(true, "the assertions pass; the box is what is wrong");
	}

	/// <summary>
	/// Malformed on purpose: a test method must return void or IEnumerator.
	/// Discovery should report this rather than quietly leaving it out, since
	/// a test that vanishes leaves the suite green for the wrong reason.
	/// </summary>
	[LoadedTest]
	public int Deliberately_malformed() => 0;
}
