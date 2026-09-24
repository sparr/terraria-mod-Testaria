using System.Diagnostics;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// Says who sent a client a piece of the world, and from where.
/// <para/>
/// Written for one question. A tier 3 test placed a tile on the server without
/// sending it and the client saw it anyway, about a second later, repeatably
/// (PLAN.md section 8.6d). Reading the source produced candidates and no
/// answer, so this asks the running game instead: every section send and every
/// tile square send is logged with the call stack that produced it.
/// <para/>
/// Off unless asked for, and a dedicated server only, because it logs a stack
/// trace per send and a real game sends a great many.
/// </summary>
public static class NetTrace
{
	/// <summary>Launch parameter turning the trace on.</summary>
	public const string Flag = "-testariatracenet";

	private static Mod? mod;

	/// <summary>True when this process was asked to trace world sends.</summary>
	public static bool Enabled => Program.LaunchParameters.ContainsKey(Flag);

	/// <summary>Installs the hooks, if asked.</summary>
	public static void Install(Mod owner)
	{
		if (!Enabled || !Main.dedServ)
			return;

		mod = owner;

		try {
			MethodInfo section = typeof(NetMessage).GetMethod(
				nameof(NetMessage.SendSection),
				BindingFlags.Public | BindingFlags.Static,
				[typeof(int), typeof(int), typeof(int)])
				?? throw new InvalidOperationException("NetMessage.SendSection(int, int, int) not found.");

			MonoModHooks.Add(section, TraceSection);

			MethodInfo square = typeof(NetMessage).GetMethod(
				nameof(NetMessage.SendTileSquare),
				BindingFlags.Public | BindingFlags.Static,
				[typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(TileChangeType)])
				?? throw new InvalidOperationException("NetMessage.SendTileSquare(int, int, int, int, int, TileChangeType) not found.");

			MonoModHooks.Add(square, TraceSquare);

			owner.Logger.Info("Testaria: tracing world sends.");
		}
		catch (Exception ex) {
			owner.Logger.Error($"Testaria: could not install the net trace. {ex}");
		}
	}

	private delegate void OrigSection(int whoAmi, int sectionX, int sectionY);

	private delegate void OrigSquare(int whoAmi, int tileX, int tileY, int xSize, int ySize, TileChangeType changeType);

	private static void TraceSection(OrigSection orig, int whoAmi, int sectionX, int sectionY)
	{
		// Whether it will actually go out, which is the interesting half: a
		// section the client already has is skipped inside, so a caller that
		// fires constantly may be sending nothing at all.
		bool alreadyHas = whoAmi >= 0
			&& whoAmi < Main.maxPlayers
			&& sectionX >= 0 && sectionX < Main.maxSectionsX
			&& sectionY >= 0 && sectionY < Main.maxSectionsY
			&& Netplay.Clients[whoAmi].TileSections[sectionX, sectionY];

		Log($"SendSection to {whoAmi} at section {sectionX},{sectionY} "
			+ $"(tiles {sectionX * 200}..{(sectionX * 200) + 199} x {sectionY * 150}..{(sectionY * 150) + 149}) "
			+ (alreadyHas ? "[client already has it, will be skipped]" : "[NEW, will be sent]"));

		orig(whoAmi, sectionX, sectionY);
	}

	private static void TraceSquare(OrigSquare orig, int whoAmi, int tileX, int tileY, int xSize, int ySize, TileChangeType changeType)
	{
		Log($"SendTileSquare to {whoAmi} at {tileX},{tileY} size {xSize}x{ySize} change {changeType}");

		orig(whoAmi, tileX, tileY, xSize, ySize, changeType);
	}

	private static void Log(string what)
	{
		// Frames from the game rather than from the hook, and enough of them
		// to name the mechanism rather than just its last step.
		string[] frames = new StackTrace(fNeedFileInfo: false)
			.GetFrames()
			.Skip(2)
			.Take(8)
			.Select(frame => frame.GetMethod() is MethodBase method
				? $"{method.DeclaringType?.Name}.{method.Name}"
				: "?")
			.ToArray();

		mod?.Logger.Info($"NETTRACE tick {Main.GameUpdateCount}: {what}\n        via {string.Join(" <- ", frames)}");
	}
}
