# XUnity.AutoTranslator.LlmEndpoint

[English](README.md) | 日本語

`XUnity.AutoTranslator` の翻訳エンドポイントとして動作する独立DLLです。

対応バックエンド:

- Ollama native chat API
- OpenAI Chat Completions APIおよび互換API
- Anthropic Messages APIおよび互換API

## インストール

1. `XUnity.AutoTranslator.LlmEndpoint.dll` をゲームの `Translators` フォルダへコピーします。
2. XUnity.AutoTranslatorの `Config.ini` を開きます。
3. `[Service]` セクションでエンドポイントを選択します。

```ini
[Service]
Endpoint=LlmEndpoint
```

4. `[LlmEndpoint]` セクションを追加し、少なくともバックエンドとモデルを設定します。

## 最小設定

```ini
[LlmEndpoint]
Backend=Ollama
Model=replace-with-an-installed-model
```

バックエンドの初期値:

| Backend | ベースURL初期値 | APIキー環境変数 |
|---|---|---|
| `Ollama` | `http://localhost:11434` | `OLLAMA_API_KEY` |
| `OpenAI` | `https://api.openai.com/v1` | `OPENAI_API_KEY` |
| `Anthropic` | `https://api.anthropic.com` | `ANTHROPIC_API_KEY` |

APIキーが必要な場合は `ApiKey` に設定します。空の場合は表の環境変数を使用します。`EndpointUrl` を指定すると、バックエンドのベースURL初期値を上書きできます。

設定例:

- [Ollama](examples/Config.Ollama.ini)
- [OpenAI](examples/Config.OpenAI.ini)
- [Anthropic](examples/Config.Anthropic.ini)

全設定と参考値は[設定リファレンス](Documents/CONFIGURATION-ja.md)を参照してください。

## ログ

初期状態では `Info` レベルのログをホストのコンソールへ出力します。`LogFile` を設定しない限りログファイルは作成しません。

`LogBatchActivity=true` にすると、件数、推定サイズ、所要時間を含むバッチ依頼・応答をInfoへ出力します。個別の翻訳情報はDebugで確認できます。APIキー、完全なプロンプト、完全な応答は出力しません。

## トラブルシューティング

- `Model` は必須です。空の場合は初期化に失敗します。
- `EndpointUrl` には絶対HTTP/HTTPSベースURLを指定します。
- 初期化や接続に失敗した場合はホストのコンソールを確認してください。

## 関連ドキュメント

- [設定リファレンス](Documents/CONFIGURATION-ja.md)
- [第三者ライセンス通知](THIRD_PARTY_NOTICES.md)
