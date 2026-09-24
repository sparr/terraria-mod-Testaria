using Terraria;
using Terraria.ModLoader;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// Questions this suite teaches the client to answer.
/// <para/>
/// Registered from a load pass, which runs on the server and the client both,
/// because the answering half has to exist on the side that will be asked.
/// That is the whole mechanism: a test mod is loaded on both sides, so its own
/// code is already present on the client and only needed a way to be reached.
/// </summary>
public sealed class ClientQueries : ModSystem
{
	/// <summary>Sends back whatever integer it was given.</summary>
	public const string Echo = "TestariaSelfTest.Echo";

	/// <summary>Reports the netmode of whichever process answers.</summary>
	public const string Netmode = "TestariaSelfTest.Netmode";

	/// <summary>Throws, on purpose.</summary>
	public const string Boom = "TestariaSelfTest.Boom";

	/// <summary>Sends back several values, to prove ordering survives the trip.</summary>
	public const string Several = "TestariaSelfTest.Several";

	/// <inheritdoc />
	public override void Load()
	{
		ClientQuery.Register(Echo, (arguments, answer) => answer.Write(arguments.ReadInt32()));

		ClientQuery.Register(Netmode, (_, answer) => answer.Write(Main.netMode));

		ClientQuery.Register(Boom, (_, _) => throw new InvalidOperationException("deliberate"));

		ClientQuery.Register(Several, (_, answer) => {
			answer.Write(1);
			answer.Write("two");
			answer.Write(3.5f);
			answer.Write(true);
		});
	}
}
