using System.Collections;
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
	/// Malformed on purpose: a test method must return void or IEnumerator.
	/// Discovery should report this rather than quietly leaving it out, since
	/// a test that vanishes leaves the suite green for the wrong reason.
	/// </summary>
	[LoadedTest]
	public int Deliberately_malformed() => 0;
}
