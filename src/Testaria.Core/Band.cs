namespace Testaria;

/// <summary>
/// A Terraria depth layer.
/// <para/>
/// Depth is semantic, not merely positional: a band determines spawn pools,
/// biome, background, music, and ambient lighting, so two boxes at different
/// depths are not equivalent test environments. That is why boxes tile
/// horizontally within a band rather than stacking vertically, and why a test
/// that needs to cross a boundary has to say so.
/// <para/>
/// Values are ordered top to bottom so that a band set can be checked for
/// contiguity and reduced to a span.
/// </summary>
[Flags]
public enum Band
{
	/// <summary>No band.</summary>
	None = 0,

	/// <summary>Above the surface, where gravity and spawns change.</summary>
	Space = 1 << 0,

	/// <summary>The overworld.</summary>
	Surface = 1 << 1,

	/// <summary>The dirt layer below the surface.</summary>
	Underground = 1 << 2,

	/// <summary>The rock layer below the dirt.</summary>
	Cavern = 1 << 3,

	/// <summary>The underworld at the bottom of the world.</summary>
	Underworld = 1 << 4,

	/// <summary>Every band, top to bottom.</summary>
	All = Space | Surface | Underground | Cavern | Underworld,
}
