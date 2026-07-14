# 設定リファレンス

[English](CONFIGURATION.md) | 日本語

設定セクション名は `[LlmEndpoint]` です。設定名は大文字と小文字を含め、表の表記どおりに記述してください。

## 全設定

| キー | 初期値 | 許容値・上限 | 説明 |
|---|---:|---|---|
| `Backend` | `Ollama` | `Ollama`, `OpenAI`, `Anthropic` | 使用するAPI系統。 |
| `EndpointUrl` | バックエンド別 | 絶対HTTP/HTTPSベースURL | このベースURLへバックエンド固有のAPIパスを追加する。 |
| `ApiKey` | 空 | 文字列 | 認証が必要なバックエンドでのみ指定（条件付き）。空の場合はバックエンドに応じた既定環境変数を自動参照する。 |
| `Model` | 空 | 空でないモデルID | **必須。** プロバイダのモデルを選択する。`hy-mt2` を含む場合は専用プロファイルになる。 |
| `BatchIntervalMs` | `300` | `0` 以上の整数 | 最初の項目から測る最大収集時間。`MaxBatchSize` に達した場合は直ちに確定する。 |
| `MaxBatchSize` | `5` | `1`-`50` | 1リクエストに含める最大件数。実件数は `MaxRequestTokens` の判定で小さくなる。大きくすると1バッチの所要時間が延び、遅い環境ではホストの1翻訳あたりタイムアウト（約150秒）を単一バッチで超える恐れがあるため小さめにする。 |
| `MaxRequestTokens` | `8192` | `1` 以上の整数 | リクエストを組み立てるためだけに使う保守的な推定トークン予算。LLMのcontext設定値ではなく、実際のプロバイダcontext上限以下にする。 |
| `MaxParallelRequests` | `1` | `1`-`50` | 同時に実行する実HTTPリクエスト数。バックエンドで並列生成が有効と確認できた場合だけ増やす。 |
| `MaxConcurrency` | `10` | `1`-`50` | XUnityから同時に受け取り内部バッファへ保留できる翻訳数（`ITranslateEndpoint.MaxConcurrency`）。**大きくし過ぎない**。遅いバックエンドで大きいと、後方の項目がホスト側の1翻訳あたりタイムアウト（約150秒）を超えるまで処理されず、訳しても表示されない。処理速度に見合う値（概ね `150秒 × MaxBatchSize ÷ 1バッチ所要秒` 未満）にする。 |
| `RequestTimeoutMs` | `300000` | `0` 以上の整数 | 1リクエスト全体（接続・生成待ち・応答受信）のタイムアウト（ミリ秒）。`0` で無効（＝無制限）。無効にすると、FIN/RSTを伴わないネットワーク切断でリクエストが永久に待機し続ける恐れがある。 |
| `RetryCount` | `1` | `0` 以上の整数 | 一時的なHTTP・通信失敗に対する、初回以外の再試行回数。 |
| `LogLevel` | `Info` | `Debug`, `Info`, `Warn`, `Error`, `Off` | 最小ログレベル。ログは `LogFile` を指定した場合のみ出力される。 |
| `LogBatchActivity` | `true` | `true`, `false` | バッチ依頼・応答のInfoログを有効にする。`LogFile` の指定が必要（未指定なら何も出力されない）。 |
| `LogTranslationItems` | `false` | `true`, `false` | 個別項目のInfoログ（各依頼の原文・各翻訳結果）を有効にする。冗長。`LogFile` の指定が必要。 |
| `LogFile` | 空 | ファイルパス | 空ならログを一切出力しない（コンソールにも出さない）。相対パスは `Translators` 基準。実ファイルは日付別に分割され、`…\LLM.log` の指定なら `…\LLM-yyyyMMdd.log` に書き込む（ローカル日付）。対象がロックされている場合は `…\LLM-yyyyMMdd-1.log` のように連番退避する。 |
| `LogRetentionDays` | `7` | `0` 以上の整数 | 日付別ログの保持日数。これより古い日付のログを削除する。`0` で削除しない（無期限）。 |
| `AppSummary` | 空 | 文字列 | 翻訳対象アプリ・ゲームの時代背景や設定・ジャンルなどの任意の補足。systemメッセージへ加えて翻訳のニュアンスを適正化する。文字数の強制上限はないが、コンテキスト節約とモデルの誤動作回避のため200文字以下を推奨。 |
| `AdditionalInstructions` | 空 | 文字列 | systemメッセージへ加える信頼済み翻訳指示。翻訳対象本文とは分離される。文字数の強制上限はないが、同じ理由で200文字以下を推奨。 |

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

## 8 GB GPU向けの全項目設定例

```ini
[LlmEndpoint]
Backend=Ollama
EndpointUrl=http://localhost:11434
ApiKey=
Model=replace-with-an-installed-ollama-model
BatchIntervalMs=300
MaxBatchSize=5
MaxRequestTokens=8192
MaxParallelRequests=1
MaxConcurrency=10
RequestTimeoutMs=300000
RetryCount=1
LogLevel=Info
LogBatchActivity=true
LogTranslationItems=false
LogFile=
LogRetentionDays=7
AppSummary=
AdditionalInstructions=
```

## 16 GB GPU向けの全項目設定例

```ini
[LlmEndpoint]
Backend=Ollama
EndpointUrl=http://localhost:11434
ApiKey=
Model=replace-with-an-installed-ollama-model
BatchIntervalMs=300
MaxBatchSize=8
MaxRequestTokens=16384
MaxParallelRequests=1
MaxConcurrency=10
RequestTimeoutMs=300000
RetryCount=1
LogLevel=Info
LogBatchActivity=true
LogTranslationItems=false
LogFile=
LogRetentionDays=7
AppSummary=
AdditionalInstructions=
```

## AppSummary指定例

`AppSummary` は「何を翻訳しているか」の背景をモデルに与え、適切な語調や用語選択を促します。任意設定で文字数の強制上限はありませんが、コンテキストを圧迫するため200文字以下を推奨します。

```ini
; 時代物
AppSummary=大正時代の日本を舞台にしたビジュアルノベル。やや硬めで時代に合った語彙を使う。

; SFアクション
AppSummary=近未来を舞台にしたテンポの速いSFシューター。UIや戦闘用語は簡潔で現代的にする。
```

改行を含めたい場合は、値の中に `\n` と書くと、プロンプト上では改行に置き換わります。`\r`、`\t`、`\\`（バックスラッシュ自体）も同様に使えます。この置換は `AppSummary` と `AdditionalInstructions` の両方に適用されます。

```ini
AdditionalInstructions=固有名詞は原語のまま残す。\n数値の単位は変更しない。
```

## 調整例

次の容量別設定は開始時の参考値です。増やす前に、実際のモデルまたはサーバーcontext上限と応答時間を確認してください。

| 条件 | MaxRequestTokens | MaxBatchSize | MaxParallelRequests | MaxConcurrency |
|---|---:|---:|---:|---:|
| 8 GB GPU、余裕がない（遅い） | `4096` | `3` | `1` | `6` |
| 8 GB GPU、標準 | `8192` | `5` | `1` | `10` |
| 16 GB GPU、標準 | `16384` | `8` | `1` | `12` |
| 並列処理を確認済みのリモートバックエンド | `16384` | `8` | `2` | `16` |

`MaxRequestTokens` 自体はプロバイダのcontext上限を変更せず、VRAMも割り当てません。ただし、Ollama側で実際のcontext設定を増やすとKV cacheのVRAM消費が増える場合があります。プロバイダcontextはプラグイン外で設定し、`MaxRequestTokens` はその実上限以下にしてください。並列要求がバックエンドを過負荷にせず全体時間を改善すると確認できるまでは、`MaxParallelRequests=1` を維持してください。

`MaxBatchSize` と `MaxConcurrency` は、ホストの1翻訳あたりタイムアウト（約150秒）を基準に決めます。**単一バッチが150秒を超えない**よう `MaxBatchSize` を、**バッファ後方の項目が150秒以内に処理される**よう `MaxConcurrency` を、実測の1バッチ所要時間に合わせて小さめにします（遅いモデルほど小さく）。大きすぎると、訳せても表示に間に合わず破棄されます。

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
