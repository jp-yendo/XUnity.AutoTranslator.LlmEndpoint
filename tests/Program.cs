using System;
using System.Collections.Generic;
using System.IO;
using XUnity.AutoTranslator.LlmEndpoint.Backends;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Dispatch;
using XUnity.AutoTranslator.LlmEndpoint.Logging;
using XUnity.AutoTranslator.LlmEndpoint.Prompts;
using XUnity.AutoTranslator.LlmEndpoint.Serialization;
using XUnity.AutoTranslator.LlmEndpoint.Text;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;

namespace XUnity.AutoTranslator.LlmEndpoint.Tests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            try
            {
                TestMiniJson();
                TestTextProtection();
                TestDefaultProfile();
                TestHyMt2Profile();
                TestSchema();
                TestConfigurationDefaults();
                TestPromptBudget();
                TestRequestBudgetPolicy();
                TestLogging();
                TestDurationFormatting();
                TestResultOrdering();
                TestProviderEndpoints();
                Console.WriteLine("PASS: " + assertions + " assertions");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
        }

        private static void TestMiniJson()
        {
            Dictionary<string, object> input = new Dictionary<string, object>();
            input["text"] = "line1\n\"line2\" \u65E5\u672C";
            input["flag"] = true;
            string json = MiniJson.Serialize(input);
            Dictionary<string, object> output = MiniJson.Deserialize(json) as Dictionary<string, object>;
            Equal((string)input["text"], MiniJson.GetString(output, "text"), "JSON string round trip");
        }

        private static void TestTextProtection()
        {
            string source = "Hello <b>{0}</b>\r\n%s\\n";
            ProtectedText protectedText = TextProtector.Protect(source);
            string candidate = protectedText.Value.Replace("Hello", "Bonjour");
            string restored;
            string error;
            True(protectedText.TryRestore(candidate, out restored, out error), "Protected tokens restore");
            Equal("Bonjour <b>{0}</b>\r\n%s\\n", restored, "Protected token values");

            ProtectedText literalMarker = TextProtector.Protect("literal __XUA_value {0}");
            True(literalMarker.TryRestore(literalMarker.Value, out restored, out error), "Literal marker prefix accepted");
            Equal("literal __XUA_value {0}", restored, "Literal marker prefix restored");

            int start = protectedText.Value.IndexOf("__XUA_", StringComparison.Ordinal);
            int end = protectedText.Value.IndexOf("__", start + 6, StringComparison.Ordinal);
            string missing = protectedText.Value.Remove(start, end + 2 - start);
            True(!protectedText.TryRestore(missing, out restored, out error), "Missing token rejection");
        }

        private static void TestDefaultProfile()
        {
            DefaultTranslationProfile profile = new DefaultTranslationProfile();
            List<PromptItem> items = new List<PromptItem>();
            items.Add(Item("a", "first"));
            items.Add(Item("b", "second"));
            PromptContext context = Context();
            context.AdditionalInstructions = "Keep UI labels concise.";
            PromptEnvelope prompt = profile.BuildPrompt(items, context, 0);
            True(prompt.SystemMessage.IndexOf("untrusted data", StringComparison.Ordinal) >= 0, "Untrusted data boundary");
            True(prompt.SystemMessage.IndexOf("Keep UI labels concise.", StringComparison.Ordinal) >= 0, "Trusted additional instruction");
            True(prompt.SystemMessage.IndexOf("first", StringComparison.Ordinal) < 0, "Source absent from system prompt");
            True(prompt.UserMessage.IndexOf("first", StringComparison.Ordinal) >= 0, "Source present in user payload");

            string reordered = "{\"items\":[{\"id\":\"b\",\"translation\":\"two\"},{\"id\":\"a\",\"translation\":\"one\"}]}";
            ProfileParseResult parsed = profile.ParseResponse(reordered, items, 0);
            True(parsed.IsFormatValid, "Reordered response accepted by ID");
            Equal("one", parsed.Translations["a"], "First translation mapped by ID");
            Equal("two", parsed.Translations["b"], "Second translation mapped by ID");

            string partial = "{\"items\":[{\"id\":\"a\",\"translation\":\"one\"}]}";
            parsed = profile.ParseResponse(partial, items, 0);
            True(!parsed.IsFormatValid, "Partial response detected");
            Equal(1, parsed.Translations.Count, "Valid partial item retained");
        }

        private static void TestHyMt2Profile()
        {
            ProfileRegistry registry = new ProfileRegistry();
            TranslationProfile profile = registry.Resolve("vendor/HY-MT2-7B");
            Equal("hy-mt2-single", profile.Id, "hy-mt2 profile selection");
            Equal(DispatchMode.Single, profile.DispatchMode, "hy-mt2 single dispatch");
            Equal(2, profile.FormatAttemptCount, "hy-mt2 retry prompts");

            List<PromptItem> items = new List<PromptItem>();
            items.Add(Item("x", "source"));
            PromptEnvelope first = profile.BuildPrompt(items, Context(), 0);
            PromptEnvelope second = profile.BuildPrompt(items, Context(), 1);
            True(first.UserMessage.IndexOf("<hytext>", StringComparison.OrdinalIgnoreCase) >= 0, "hy-mt2 first wrapper");
            True(second.UserMessage.IndexOf("[HyText]", StringComparison.Ordinal) >= 0, "hy-mt2 fallback wrapper");
            True(first.SystemMessage.Length > 0, "hy-mt2 system prompt");

            ProfileParseResult parsed = profile.ParseResponse("translated", items, 0);
            True(parsed.IsFormatValid, "hy-mt2 plain output accepted");
            parsed = profile.ParseResponse("hyAssistant translated", items, 0);
            True(!parsed.IsFormatValid, "hy-mt2 control token rejected");
            parsed = profile.ParseResponse("<hytext>translated</hytext>", items, 0);
            True(!parsed.IsFormatValid, "hy-mt2 wrapper echo rejected");
        }

        private static void TestSchema()
        {
            Dictionary<string, object> schema = JsonSchemaFactory.CreateTranslationSchema(new List<string>(new string[] { "a", "b" }));
            Dictionary<string, object> properties = MiniJson.GetObject(schema, "properties");
            Dictionary<string, object> items = MiniJson.GetObject(properties, "items");
            Equal(2, Convert.ToInt32(items["minItems"]), "Schema minimum item count");
            Equal(2, Convert.ToInt32(items["maxItems"]), "Schema maximum item count");

            schema = JsonSchemaFactory.CreateTranslationSchema(new List<string>(new string[] { "a", "b" }), false);
            properties = MiniJson.GetObject(schema, "properties");
            items = MiniJson.GetObject(properties, "items");
            True(!items.ContainsKey("minItems"), "Portable schema omits minimum item count");
            True(!items.ContainsKey("maxItems"), "Portable schema omits maximum item count");
        }

        private static void TestResultOrdering()
        {
            PendingOperation operation = new PendingOperation(new string[] { "first", "second" });
            operation.CompleteItem(1, "two");
            operation.CompleteItem(0, "one");
            string[] results = operation.GetResults(false);
            Equal("one", results[0], "First result order");
            Equal("two", results[1], "Second result order");
        }

        private static void TestLogging()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "logger-test-" + Guid.NewGuid().ToString("N") + ".log");
            string filteredPath = Path.Combine(AppContext.BaseDirectory, "logger-test-" + Guid.NewGuid().ToString("N") + ".log");
            TextWriter originalOutput = Console.Out;
            StringWriter consoleOutput = new StringWriter();
            try
            {
                Console.SetOut(consoleOutput);
                EndpointLogger logger = new EndpointLogger(LogLevel.Debug, path, string.Empty);
                logger.BatchActivity(false, "hidden-batch-entry");
                True(!File.Exists(path), "Disabled batch activity is hidden at Debug level");
                True(consoleOutput.ToString().IndexOf("hidden-batch-entry", StringComparison.Ordinal) < 0,
                   "Disabled batch activity is absent from console output");
                logger.BatchActivity(true, "visible-batch-entry");
                True(consoleOutput.ToString().IndexOf("visible-batch-entry", StringComparison.Ordinal) >= 0,
                   "Enabled batch activity appears in console output");
                True(File.ReadAllText(path).IndexOf("visible-batch-entry", StringComparison.Ordinal) >= 0,
                   "Enabled batch activity appears in file output");

                EndpointLogger filteredLogger = new EndpointLogger(LogLevel.Warn, filteredPath, string.Empty);
                filteredLogger.BatchActivity(true, "filtered-batch-entry");
                True(!File.Exists(filteredPath), "Batch activity does not raise the configured log level");
                True(consoleOutput.ToString().IndexOf("filtered-batch-entry", StringComparison.Ordinal) < 0,
                   "Batch activity obeys the common log level");
            }
            finally
            {
                Console.SetOut(originalOutput);
                consoleOutput.Dispose();
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(filteredPath)) File.Delete(filteredPath);
            }
        }

        private static void TestConfigurationDefaults()
        {
            Equal(300, LlmSettings.DefaultBatchIntervalMs, "Default batch interval");
            Equal(50, LlmSettings.EndpointMaxConcurrency, "Endpoint concurrency limit");
            Equal(500, LlmSettings.RetryBaseDelayMs, "Retry base delay");
            Equal(30000, LlmSettings.RetryMaximumDelayMs, "Retry maximum delay");
            Equal(1, LlmSettings.MaxContextItems, "Context items per side");
            Equal(100, LlmSettings.DispatcherRecoveryDelayMs, "Dispatcher recovery delay");
            Equal("2023-06-01", LlmSettings.AnthropicApiVersion, "Anthropic API version");
        }

        private static void TestDurationFormatting()
        {
            Equal("250 ms", DurationFormatter.Format(TimeSpan.FromMilliseconds(250)), "Millisecond duration");
            Equal("1.50 s", DurationFormatter.Format(TimeSpan.FromMilliseconds(1500)), "Second duration");
            Equal("1.50 m", DurationFormatter.Format(TimeSpan.FromSeconds(90)), "Minute duration");
        }

        private static void TestPromptBudget()
        {
            DefaultTranslationProfile profile = new DefaultTranslationProfile();
            List<PromptItem> one = new List<PromptItem>();
            one.Add(Item("a", "short"));
            PromptEnvelope onePrompt = profile.BuildPrompt(one, Context(), 0);
            int oneEstimate = PromptBudgetEstimator.EstimateUpperBoundTokens(onePrompt, one);

            List<PromptItem> two = new List<PromptItem>(one);
            two.Add(Item("b", new string('\u65E5', 100)));
            PromptEnvelope twoPrompt = profile.BuildPrompt(two, Context(), 0);
            int twoEstimate = PromptBudgetEstimator.EstimateUpperBoundTokens(twoPrompt, two);
            True(twoEstimate > oneEstimate, "Context budget grows with CJK input");
            True(twoEstimate <= 8192, "Small CJK batch fits default request budget");
            Equal(128, PromptBudgetEstimator.EstimateOutputUpperBoundTokens(one), "Minimum output token estimate");
            Equal(332, PromptBudgetEstimator.EstimateOutputUpperBoundTokens(
               new List<PromptItem>(new PromptItem[] { Item("c", new string('\u65E5', 100)) })),
               "CJK output token estimate");

        }

        private static void TestRequestBudgetPolicy()
        {
            True(RequestBudgetPolicy.CanAddItem(0, 9000, 8192), "First item is always admitted");
            True(RequestBudgetPolicy.CanAddItem(1, 8192, 8192), "Additional item fits exact request budget");
            True(!RequestBudgetPolicy.CanAddItem(1, 8193, 8192), "Additional item exceeding request budget is deferred");
        }

        private static void TestProviderEndpoints()
        {
            Uri ollama = ProviderEndpoint.OllamaChat("http://localhost:11434");
            Equal("/api/chat", ollama.AbsolutePath, "Ollama chat path");
            ollama = ProviderEndpoint.OllamaChat("http://localhost:11434/custom");
            Equal("/custom/api/chat", ollama.AbsolutePath, "Ollama prefixed chat path");
            Uri openAi = ProviderEndpoint.OpenAiChatCompletions("https://example.test/custom/v1?ignored=true");
            Equal("/custom/v1/chat/completions", openAi.AbsolutePath, "OpenAI-compatible chat path");
            Equal(string.Empty, openAi.Query, "Provider query removed");
            Uri anthropic = ProviderEndpoint.AnthropicMessages("https://example.test/custom");
            Equal("/custom/v1/messages", anthropic.AbsolutePath, "Anthropic messages path");
        }

        private static PromptItem Item(string id, string text)
        {
            PromptItem item = new PromptItem();
            item.Id = id;
            item.Text = text;
            return item;
        }

        private static PromptContext Context()
        {
            PromptContext context = new PromptContext();
            context.SourceLanguage = "ja";
            context.TargetLanguage = "en";
            return context;
        }

        private static void True(bool value, string name)
        {
            assertions++;
            if (!value) throw new InvalidOperationException(name);
        }

        private static void Equal(object expected, object actual, string name)
        {
            assertions++;
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(name + ": expected " + expected + ", actual " + actual);
            }
        }
    }
}
