using System.IO;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// The mod itself, which exists for one reason: packets arrive here.
/// <para/>
/// <c>Mod.HandlePacket</c> is the only entry point tModLoader offers for a
/// mod's own network traffic, so tier 3 needs a <see cref="Mod"/> subclass even
/// though everything else in Testaria lives in a <c>ModSystem</c>.
/// </summary>
public sealed class TestariaMod : Mod
{
	/// <summary>
	/// Reads one of Testaria's own packets.
	/// <para/>
	/// The leading byte is ours to define: tModLoader prefixes only the mod
	/// identity, so a mod that writes no discriminator of its own cannot tell
	/// its messages apart.
	/// </summary>
	public override void HandlePacket(BinaryReader reader, int whoAmI)
	{
		ArgumentNullException.ThrowIfNull(reader);

		ClientLink.Receive(this, (ClientLink.Message)reader.ReadByte(), reader, whoAmI);
	}
}
