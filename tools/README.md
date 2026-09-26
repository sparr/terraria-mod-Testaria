# tools

Odds and ends that are not part of the framework and not gates. Nothing in
`scripts/` runs them.

## `read-tmod.py`

Reads a `.tmod` archive: its manifest, its file table, and optionally every
file in it.

```
tools/read-tmod.py SomeMod.tmod              # manifest and contents
tools/read-tmod.py SomeMod.tmod out/         # and extract into out/
```

Written while working out where two 1.4.5 builds had come from, when neither
mod had published 1.4.5 source. The manifest answers that: it carries the
author, the version, the tModLoader the mod was built against, and the
`modSource` path on the machine that built it, which is how those two turned
out to be private local builds rather than anything on a branch.

The format is tModLoader's own, from `Terraria/ModLoader/Core/TmodFile.cs`:
the magic `TMOD`, a length-prefixed version string, twenty bytes of hash, two
hundred and fifty-six of signature, a data length, then the mod name, its
version, a file table, and the blob. Entries are raw deflate wherever the
compressed length differs from the real one.

Two things worth knowing when reading one. A mod only carries its own source
if it was built with `includeSource`, and most are not, so extracting usually
yields assets and a `.dll` rather than code. And the manifest lives in an entry
called `Info` rather than in `build.txt`, which is what the build reads rather
than what the archive keeps.
