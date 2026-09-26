# Calibration suites for mods that live elsewhere

Suites aimed at mods nobody here wrote, kept because the repositories they
belong in are not ours to push to. They are not gates: nothing in
`scripts/` runs them, and they need their subject built and installed first.

See [`docs/ecosystem-calibration.md`](../docs/ecosystem-calibration.md) for
what each one found.

## TestingEfficiencyTests

Aimed at [Doze's Testing Efficiency](https://github.com/Doze-Zoze/TestingEfficiency),
branch `1.4.5`. 55 tests: the NPC ID sets the damage tracker credits hits
through, and the two string properties a boss result is edited through.

To run it, from a directory laid out the way `ModSources` is, so that
`..\tModLoader.targets` resolves:

```
git clone -b 1.4.5 https://github.com/Doze-Zoze/TestingEfficiency
cp "$(ls -d ~/.local/share/Terraria/tModLoader*/ModSources)/tModLoader.targets" .
cp -r <this repo>/calibration/TestingEfficiencyTests .

# The mod ships two project files in one folder, so name the one to build,
# and override the assembly name: tModLoader refuses to load a mod whose
# assembly name is not the mod name, and this project's would be
# "TestingEfficiency-main".
dotnet build TestingEfficiency/TestingEfficiency-main.csproj -c Release \
  -p:AssemblyName=TestingEfficiency

dotnet build TestingEfficiencyTests -c Release -p:TestariaRepo=<this repo>

ENABLED="Testaria TestingEfficiency TestingEfficiencyTests" \
RUN_NAME=TestingEfficiency BLANK=1 BUILD=0 <this repo>/scripts/run-tests.sh
```

`TestingEfficiency.csproj`, the one not built above, is the author's own and
uses a third-party SDK (`Tomat.Terraria.ModLoader.Sdk`) rather than the stock
one. Building that instead needs whatever it needs; none of this has been
tried against it.
