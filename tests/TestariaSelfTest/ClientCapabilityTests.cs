using System.Collections;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// Prints what the tier 3 client reported about itself.
/// <para/>
/// An investigation rather than a check: the assertions are only that the
/// client answered at all, and the value is in what it said, which goes to the
/// server's console where the harness log will keep it.
/// </summary>
public class ClientCapabilityTests
{
	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator What_the_client_can_do(ITestContext ctx)
	{
		ClientLink.Request answer = ClientLink.Ask(ClientCapabilityProbe.Capabilities);

		yield return Wait.Until(() => answer.Answered, "the client to describe itself");

		Assert.False(answer.Failed, answer.Error);

		string report = answer.Read().ReadString();

		Console.WriteLine("=== client capabilities ===");

		foreach (string line in report.Split('\n')) {
			if (line.Length > 0)
				Console.WriteLine("  " + line);
		}

		Console.WriteLine("=== end client capabilities ===");

		Assert.NotEmpty(report);
	}
}
