; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TSTA001 | Testaria.Tiers | Error | Loader-dependent API used where no load pass has run
TSTA002 | Testaria.Tiers | Error | Undeclared call to a member marked [RequiresLoadedGame]
