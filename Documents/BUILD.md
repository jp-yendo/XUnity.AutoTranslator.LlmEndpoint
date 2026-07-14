# Build guide

English | [日本語](BUILD-ja.md)

## Requirements

- .NET SDK 8 or later with the `net6.0` targeting pack
- A compatible `XUnity.AutoTranslator.Plugin.Core.dll` from the Developer distribution used by the target installation

The default development reference currently points to the following path relative to the project root:

```text
ref\XUnity.AutoTranslator-MelonMod-IL2CPP-5.6.1\UserLibs\XUnity.AutoTranslator.Plugin.Core.dll
```

The `ref` directory is excluded by `.gitignore`. Use `XUnityAutoTranslatorCorePath` when building against another compatible Developer distribution.

## Build

```powershell
dotnet restore .\XUnity.AutoTranslator.LlmEndpoint.csproj
dotnet build .\XUnity.AutoTranslator.LlmEndpoint.csproj -c Release --no-restore
```

To use a Core assembly at another location:

```powershell
dotnet build .\XUnity.AutoTranslator.LlmEndpoint.csproj -c Release `
  -p:XUnityAutoTranslatorCorePath="D:\path\to\XUnity.AutoTranslator.Plugin.Core.dll"
```

Output:

```text
bin\Release\net6.0\XUnity.AutoTranslator.LlmEndpoint.dll
```

## Verification

```powershell
dotnet format .\XUnity.AutoTranslator.LlmEndpoint.csproj --verify-no-changes --no-restore
dotnet run --project .\tests\XUnity.AutoTranslator.LlmEndpoint.Tests.csproj -c Release
```

The `tests\SingleDllSmoke` project verifies each backend by loading the release DLL in a separate .NET 6 process and sending a loopback HTTP request.
