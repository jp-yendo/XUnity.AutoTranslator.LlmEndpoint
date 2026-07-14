# 設定リファレンス

[English](CONFIGURATION.md) | 日本語

設定セクション名は `[LlmEndpoint]` です。設定名は大文字と小文字を含め、表の表記どおりに記述してください。

## 全設定

| キー | 初期値 | 必須 | 許容値・上限 | 8 GB参考値 | 16 GB参考値 | 説明 |
|---|---:|---|---|---|---|---|
| `Backend` | `Ollama` | いいえ | `Ollama`, `OpenAI`, `Anthropic` | `Ollama` | `Ollama` | 使用するAPI系統。 |
| `EndpointUrl` | バックエンド別 | いいえ | 絶対HTTP/HTTPSベースURL | バックエンド既定値 | 同左 | このベースURLへバックエンド固有のAPIパスを追加する。 |
| `ApiKey` | 空 | 条件付き | 文字列 | 空 | 空 | 空の場合はバックエンドに応じた既定環境変数を自動参照する。 |
| `Model` | 空 | はい | 空でないモデルID | 導入済みモデル | 導入済みモデル | プロバイダのモデルを選択する。`hy-mt2` を含む場合は専用プロファイルになる。 |
| `BatchIntervalMs` | `300` | いいえ | `0` 以上の整数 | `300` | `300` | 最初の項目から測る最大収集時間。`MaxBatchSize` に達した場合は直ちに確定する。 |
| `MaxBatchSize` | `8` | いいえ | `1`-`50` | `8` | `16` | 1リクエストに含める最大件数。実件数は `MaxRequestTokens` の判定で小さくなる。 |
| `MaxRequestTokens` | `8192` | いいえ | `1` 以上の整数 | `8192` | `16384` | リクエストを組み立てるためだけに使う保守的な推定トークン予算。LLMのcontext設定値ではなく、実際のプロバイダcontext上限以下にする。 |
| `MaxParallelRequests` | `1` | いいえ | `1`-`50` | `1` | `1` | 同時に実行する実HTTPリクエスト数。バックエンドで並列生成が有効と確認できた場合だけ増やす。 |
| `RetryCount` | `1` | いいえ | `0` 以上の整数 | `1` | `1` | 一時的なHTTP・通信失敗に対する、初回以外の再試行回数。 |
| `LogLevel` | `Info` | いいえ | `Debug`, `Info`, `Warn`, `Error`, `Off` | `Info` | `Info` | コンソールとファイルに共通の最小ログレベル。 |
| `LogBatchActivity` | `false` | いいえ | `true`, `false` | `false` | `false` | バッチ依頼・応答のInfoログを有効にする。 |
| `LogFile` | 空 | いいえ | ファイルパス | 空 | 空 | 空ならファイルを作成しない。相対パスは `Translators` 基準。 |
| `AdditionalInstructions` | 空 | いいえ | 文字列 | 空 | 空 | systemメッセージへ加える信頼済み翻訳指示。翻訳対象本文とは分離される。 |

バックエンド別の `EndpointUrl` 初期値は次のとおりです。

| Backend | EndpointUrl初期値 |
|---|---|
| `Ollama` | `http://localhost:11434` |
| `OpenAI` | `https://api.openai.com/v1` |
| `Anthropic` | `https://api.anthropic.com` |

`ApiKey` が空の場合、次の既定環境変数を自動的に読みます。環境変数名を指定する設定キーはありません。

| Backend | 環境変数 |
|---|---|
| `Ollama` | `OLLAMA_API_KEY` |
| `OpenAI` | `OPENAI_API_KEY` |
| `Anthropic` | `ANTHROPIC_API_KEY` |

## 8 GB GPU向け完全例

```ini
[LlmEndpoint]
Backend=Ollama
EndpointUrl=http://localhost:11434
ApiKey=
Model=replace-with-an-installed-ollama-model
BatchIntervalMs=300
MaxBatchSize=8
MaxRequestTokens=8192
MaxParallelRequests=1
RetryCount=1
LogLevel=Info
LogBatchActivity=false
LogFile=
AdditionalInstructions=
```

## 調整例

次の容量別設定は開始時の参考値です。増やす前に、実際のモデルまたはサーバーcontext上限と応答時間を確認してください。

| 条件 | MaxRequestTokens | MaxBatchSize | MaxParallelRequests |
|---|---:|---:|---:|
| 8 GB GPU、余裕がない | `4096` | `4` | `1` |
| 8 GB GPU、標準 | `8192` | `8` | `1` |
| 16 GB GPU、標準 | `16384` | `16` | `1` |
| 並列処理を確認済みのリモートバックエンド | `16384` | `16` | `2` |

`MaxRequestTokens` 自体はプロバイダのcontext上限を変更せず、VRAMも割り当てません。ただし、Ollama側で実際のcontext設定を増やすとKV cacheのVRAM消費が増える場合があります。プロバイダcontextはプラグイン外で設定し、`MaxRequestTokens` はその実上限以下にしてください。並列要求がバックエンドを過負荷にせず全体時間を改善すると確認できるまでは、`MaxParallelRequests=1` を維持してください。

## LogFile指定例

```ini
; No file output
LogFile=

; Relative to the Translators directory
LogFile=LlmEndpoint.log

; Absolute path
LogFile=D:\Logs\LlmEndpoint.log
```

相対パスと絶対パスのどちらも指定できます。親ディレクトリが存在しない場合は、最初のファイル書き込み時に作成します。
