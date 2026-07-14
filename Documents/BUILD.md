# Build guide

English | [日本語](BUILD-ja.md)

## Requirements

- .NET SDK 8 or later
- The `XUnity.AutoTranslator.Plugin.Core.dll` from both Developer distributions used by the target installations

## Dual build

The project builds from a single source to two target frameworks:

| Target framework | Variant | Reference (`ref\`) |
|---|---|---|
| `net6.0` | IL2CPP (BepInEx 6 / MelonLoader IL2CPP) | `XUnity.AutoTranslator-Developer-IL2CPP\XUnity.AutoTranslator.Plugin.Core.dll` |
| `net35` | Mono (BepInEx 5 / MelonLoader Mono) | `XUnity.AutoTranslator-Developer\XUnity.AutoTranslator.Plugin.Core.dll` |

The framework-specific HTTP layer lives in `src\Backends\HttpTransport.cs`: `net6.0` uses `HttpClient`, `net35` uses `HttpWebRequest`. The `net35` build pulls `Microsoft.NETFramework.ReferenceAssemblies` at build time only. The `ref` directory is excluded by `.gitignore`.

```powershell
dotnet build .\XUnity.AutoTranslator.LlmEndpoint.csproj -c Release
```

Output:

```text
bin\Release\net6.0\XUnity.AutoTranslator.LlmEndpoint.dll
bin\Release\net35\XUnity.AutoTranslator.LlmEndpoint.dll
```

To build a single framework against a Core assembly at another location, override `XUnityAutoTranslatorCorePath` (it applies to the framework being built):

```powershell
dotnet build .\XUnity.AutoTranslator.LlmEndpoint.csproj -c Release -f net6.0 `
  -p:XUnityAutoTranslatorCorePath="D:\path\to\XUnity.AutoTranslator.Plugin.Core.dll"
```

## Release packages

`scripts\pack.ps1` builds both frameworks and produces per-loader ZIPs whose directory layout overlays directly onto the game directory (extract into the game folder):

```powershell
pwsh scripts\pack.ps1            # version taken from AssemblyInfo
pwsh scripts\pack.ps1 -Version 1.2.0
```

| Archive | Framework | In-archive path |
|---|---|---|
| `...-BepInEx-<ver>.zip` | net35 | `BepInEx\plugins\XUnity.AutoTranslator\Translators\` |
| `...-BepInEx-IL2CPP-<ver>.zip` | net6.0 | `BepInEx\plugins\XUnity.AutoTranslator\Translators\` |
| `...-MelonMod-<ver>.zip` | net35 | `UserLibs\Translators\` |
| `...-MelonMod-IL2CPP-<ver>.zip` | net6.0 | `UserLibs\Translators\` |

Artifacts are written to `dist\` (excluded by `.gitignore`).

## Verification

```powershell
dotnet format .\XUnity.AutoTranslator.LlmEndpoint.csproj --verify-no-changes --no-restore
dotnet run --project .\tests\XUnity.AutoTranslator.LlmEndpoint.Tests.csproj -c Release
```

The `tests\SingleDllSmoke` project verifies each backend by loading the `net6.0` release DLL in a separate .NET 6 process and sending a loopback HTTP request:

```powershell
dotnet run --project .\tests\SingleDllSmoke\SingleDllSmoke.csproj -c Release -- `
  bin\Release\net6.0\XUnity.AutoTranslator.LlmEndpoint.dll ref\XUnity.AutoTranslator-Developer-IL2CPP Ollama
```
