# ビルド手順

[English](BUILD.md) | 日本語

## 必要環境

- `net6.0` ターゲティングパックを利用できる.NET SDK 8以降
- 対象環境のDeveloper配布物に含まれる互換性のある `XUnity.AutoTranslator.Plugin.Core.dll`

開発時の既定参照は、プロジェクトルートから次の相対パスです。

```text
ref\XUnity.AutoTranslator-MelonMod-IL2CPP-5.6.1\UserLibs\XUnity.AutoTranslator.Plugin.Core.dll
```

`ref` ディレクトリは `.gitignore` の対象です。別の互換Developer配布物を使用する場合は `XUnityAutoTranslatorCorePath` で指定します。

## ビルド

```powershell
dotnet restore .\XUnity.AutoTranslator.LlmEndpoint.csproj
dotnet build .\XUnity.AutoTranslator.LlmEndpoint.csproj -c Release --no-restore
```

別の場所にあるCoreアセンブリを使用する場合:

```powershell
dotnet build .\XUnity.AutoTranslator.LlmEndpoint.csproj -c Release `
  -p:XUnityAutoTranslatorCorePath="D:\path\to\XUnity.AutoTranslator.Plugin.Core.dll"
```

生成物:

```text
bin\Release\net6.0\XUnity.AutoTranslator.LlmEndpoint.dll
```

## 検証

```powershell
dotnet format .\XUnity.AutoTranslator.LlmEndpoint.csproj --verify-no-changes --no-restore
dotnet run --project .\tests\XUnity.AutoTranslator.LlmEndpoint.Tests.csproj -c Release
```

`tests\SingleDllSmoke` プロジェクトでは、リリースDLLを.NET 6の別プロセスで読み込み、各バックエンドからループバックへHTTPリクエストを送信して検証します。
