# ビルド手順

[English](BUILD.md) | 日本語

## 必要環境

- .NET SDK 8以降
- 対象環境のDeveloper配布物に含まれる `XUnity.AutoTranslator.Plugin.Core.dll`（両バリアント分）

## デュアルビルド

単一ソースから2つのターゲットフレームワークへビルドします。

| ターゲット | バリアント | 参照（`ref\`） |
|---|---|---|
| `net6.0` | IL2CPP（BepInEx 6 / MelonLoader IL2CPP） | `XUnity.AutoTranslator-Developer-IL2CPP\XUnity.AutoTranslator.Plugin.Core.dll` |
| `net35` | Mono（BepInEx 5 / MelonLoader Mono） | `XUnity.AutoTranslator-Developer\XUnity.AutoTranslator.Plugin.Core.dll` |

フレームワーク依存のHTTP層は `src\Backends\HttpTransport.cs` に集約しています（`net6.0` は `HttpClient`、`net35` は `HttpWebRequest`）。`net35` ビルドは `Microsoft.NETFramework.ReferenceAssemblies` をビルド時のみ取得します。`ref` ディレクトリは `.gitignore` の対象です。

```powershell
dotnet build .\XUnity.AutoTranslator.LlmEndpoint.csproj -c Release
```

生成物:

```text
bin\Release\net6.0\XUnity.AutoTranslator.LlmEndpoint.dll
bin\Release\net35\XUnity.AutoTranslator.LlmEndpoint.dll
```

別の場所のCoreアセンブリで単一フレームワークをビルドする場合は `XUnityAutoTranslatorCorePath` を指定します（ビルド対象のフレームワークに適用されます）。

```powershell
dotnet build .\XUnity.AutoTranslator.LlmEndpoint.csproj -c Release -f net6.0 `
  -p:XUnityAutoTranslatorCorePath="D:\path\to\XUnity.AutoTranslator.Plugin.Core.dll"
```

## リリースパッケージ

`scripts\pack.ps1` は両フレームワークをビルドし、ゲームディレクトリへそのまま展開（オーバーレイ）できるローダー別ZIPを生成します。

```powershell
pwsh scripts\pack.ps1            # バージョンはAssemblyInfoから取得
pwsh scripts\pack.ps1 -Version 1.2.0
```

| 書庫 | フレームワーク | 書庫内パス |
|---|---|---|
| `...-BepInEx-<ver>.zip` | net35 | `BepInEx\plugins\XUnity.AutoTranslator\Translators\` |
| `...-BepInEx-IL2CPP-<ver>.zip` | net6.0 | `BepInEx\plugins\XUnity.AutoTranslator\Translators\` |
| `...-MelonMod-<ver>.zip` | net35 | `UserLibs\Translators\` |
| `...-MelonMod-IL2CPP-<ver>.zip` | net6.0 | `UserLibs\Translators\` |

成果物は `dist\`（`.gitignore` 対象）に出力されます。

## 検証

```powershell
dotnet format .\XUnity.AutoTranslator.LlmEndpoint.csproj --verify-no-changes --no-restore
dotnet run --project .\tests\XUnity.AutoTranslator.LlmEndpoint.Tests.csproj -c Release
```

`tests\SingleDllSmoke` プロジェクトは、`net6.0` のリリースDLLを.NET 6の別プロセスで読み込み、各バックエンドからループバックへHTTPリクエストを送信して検証します。

```powershell
dotnet run --project .\tests\SingleDllSmoke\SingleDllSmoke.csproj -c Release -- `
  bin\Release\net6.0\XUnity.AutoTranslator.LlmEndpoint.dll ref\XUnity.AutoTranslator-Developer-IL2CPP Ollama
```
