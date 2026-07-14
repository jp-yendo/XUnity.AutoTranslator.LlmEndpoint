using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace SingleDllSmoke
{
    internal static class Program
    {
        private static string coreDirectory;

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length != 3)
                {
                    throw new ArgumentException("Expected: <plugin-dll> <core-directory> <Ollama|OpenAI|Anthropic>");
                }

                string pluginPath = Path.GetFullPath(args[0]);
                coreDirectory = Path.GetFullPath(args[1]);
                string backendName = args[2];
                if (!File.Exists(pluginPath)) throw new FileNotFoundException("Plugin DLL was not found.", pluginPath);

                AssemblyLoadContext.Default.Resolving += ResolveCoreAssembly;
                AssemblyLoadContext.Default.LoadFromAssemblyPath(
                   Path.Combine(coreDirectory, "XUnity.AutoTranslator.Plugin.Core.dll"));
                Assembly plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);

                using (LoopbackServer server = new LoopbackServer(backendName))
                {
                    object settings = CreateSettings(plugin, backendName, server.EndpointUrl);
                    object logger = CreateLogger(plugin);
                    string backendTypeName = BackendTypeName(backendName);
                    Type backendType = RequiredType(plugin, backendTypeName);
                    object backend = Activator.CreateInstance(
                       backendType,
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                       null,
                       new object[] { settings, logger },
                       null);

                    object prompt = CreatePrompt(plugin);
                    MethodInfo generate = backendType.GetMethod("Generate", BindingFlags.Instance | BindingFlags.Public);
                    if (generate == null) throw new MissingMethodException(backendType.FullName, "Generate");
                    string output = (string)generate.Invoke(backend, new object[] { prompt });
                    if (!string.Equals(output, "translated", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Unexpected generated text: " + output);
                    }
                    string requestBody = server.WaitForRequest();
                    string expectedPath = string.Equals(backendName, "Ollama", StringComparison.Ordinal)
                       ? "/api/chat"
                       : string.Equals(backendName, "OpenAI", StringComparison.Ordinal)
                          ? "/v1/chat/completions"
                          : "/v1/messages";
                    if (!string.Equals(server.RequestPath, expectedPath, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                           "Unexpected request path: " + server.RequestPath + ", expected " + expectedPath + ".");
                    }
                    if (requestBody.IndexOf("\"role\":\"user\"", StringComparison.Ordinal) < 0)
                    {
                        throw new InvalidOperationException("The request did not contain a user message.");
                    }
                    if (string.Equals(backendName, "Anthropic", StringComparison.Ordinal))
                    {
                        RequireHeader(server.RequestHeaders, "x-api-key: test-key");
                        RequireHeader(server.RequestHeaders, "anthropic-version: 2023-06-01");
                    }
                    else
                    {
                        RequireHeader(server.RequestHeaders, "Authorization: Bearer test-key");
                    }
                    if (string.Equals(backendName, "Anthropic", StringComparison.Ordinal))
                    {
                        if (requestBody.IndexOf("\"system\":", StringComparison.Ordinal) < 0 ||
                           requestBody.IndexOf("\"output_config\":", StringComparison.Ordinal) < 0)
                        {
                            throw new InvalidOperationException("The Anthropic request shape was incorrect.");
                        }
                    }
                    else if (requestBody.IndexOf("\"role\":\"system\"", StringComparison.Ordinal) < 0)
                    {
                        throw new InvalidOperationException("The request did not keep system and user messages separate.");
                    }
                    if (string.Equals(backendName, "Ollama", StringComparison.Ordinal) &&
                       requestBody.IndexOf("\"format\":{", StringComparison.Ordinal) < 0)
                    {
                        throw new InvalidOperationException("The Ollama request did not include a JSON Schema.");
                    }
                    if (string.Equals(backendName, "Ollama", StringComparison.Ordinal) &&
                       requestBody.IndexOf("\"num_ctx\"", StringComparison.Ordinal) >= 0)
                    {
                        throw new InvalidOperationException("The Ollama request unexpectedly included num_ctx.");
                    }
                    if (string.Equals(backendName, "OpenAI", StringComparison.Ordinal) &&
                       requestBody.IndexOf("\"response_format\":", StringComparison.Ordinal) < 0)
                    {
                        throw new InvalidOperationException("The OpenAI request did not include structured output.");
                    }
                }

                Console.WriteLine("PASS: " + backendName + " completed a single-DLL request on .NET " + Environment.Version);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
        }

        private static object CreateSettings(Assembly plugin, string backendName, string endpointUrl)
        {
            Type settingsType = RequiredType(plugin, "XUnity.AutoTranslator.LlmEndpoint.Configuration.LlmSettings");
            object settings = Activator.CreateInstance(settingsType, true);
            SetField(settingsType, settings, "EndpointUrl", endpointUrl);
            SetField(settingsType, settings, "Model", "smoke-test-model");
            SetField(settingsType, settings, "ApiKey", "test-key");
            SetField(settingsType, settings, "MaxParallelRequests", 1);
            SetField(settingsType, settings, "MaxRequestTokens", 8192);
            return settings;
        }

        private static object CreatePrompt(Assembly plugin)
        {
            Type promptType = RequiredType(plugin, "XUnity.AutoTranslator.LlmEndpoint.Prompts.PromptEnvelope");
            object prompt = Activator.CreateInstance(promptType, true);
            SetField(promptType, prompt, "SystemMessage", "Translate only the user payload.");
            SetField(promptType, prompt, "UserMessage", "{\"items\":[{\"id\":\"a\",\"text\":\"source\"}]}");
            SetField(promptType, prompt, "UseStructuredOutput", true);
            SetField(promptType, prompt, "ExpectedIds", new List<string>(new string[] { "a" }));
            SetField(promptType, prompt, "MaxOutputTokens", 128);
            return prompt;
        }

        private static object CreateLogger(Assembly plugin)
        {
            Type loggerType = RequiredType(plugin, "XUnity.AutoTranslator.LlmEndpoint.Logging.EndpointLogger");
            Type levelType = RequiredType(plugin, "XUnity.AutoTranslator.LlmEndpoint.Configuration.LogLevel");
            object off = Enum.Parse(levelType, "Off");
            return Activator.CreateInstance(
               loggerType,
               BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
               null,
               new object[] { off, string.Empty, string.Empty },
               null);
        }

        private static Assembly ResolveCoreAssembly(AssemblyLoadContext context, AssemblyName name)
        {
            if (string.IsNullOrEmpty(name.Name)) return null;
            string path = Path.Combine(coreDirectory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        }

        private static string BackendTypeName(string backendName)
        {
            if (string.Equals(backendName, "Ollama", StringComparison.Ordinal))
            {
                return "XUnity.AutoTranslator.LlmEndpoint.Backends.OllamaBackend";
            }
            if (string.Equals(backendName, "OpenAI", StringComparison.Ordinal))
            {
                return "XUnity.AutoTranslator.LlmEndpoint.Backends.OpenAiBackend";
            }
            if (string.Equals(backendName, "Anthropic", StringComparison.Ordinal))
            {
                return "XUnity.AutoTranslator.LlmEndpoint.Backends.AnthropicBackend";
            }
            throw new ArgumentException("Unknown backend: " + backendName);
        }

        private static Type RequiredType(Assembly assembly, string name)
        {
            return assembly.GetType(name, true, false);
        }

        private static void SetField(Type type, object instance, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            field.SetValue(instance, value);
        }

        private static void RequireHeader(string headers, string expected)
        {
            if (headers == null || headers.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("The request did not contain the expected authentication header.");
            }
        }

        private sealed class LoopbackServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly Task<string> requestTask;
            private readonly string responseBody;

            public LoopbackServer(string backendName)
            {
                if (string.Equals(backendName, "Ollama", StringComparison.Ordinal))
                {
                    responseBody = "{\"model\":\"smoke-test-model\",\"created_at\":\"2026-01-01T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"translated\"},\"done\":true}";
                }
                else if (string.Equals(backendName, "OpenAI", StringComparison.Ordinal))
                {
                    responseBody = "{\"id\":\"chatcmpl_test\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"smoke-test-model\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"translated\",\"refusal\":null},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";
                }
                else
                {
                    responseBody = "{\"id\":\"msg_test\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"smoke-test-model\",\"content\":[{\"type\":\"text\",\"text\":\"translated\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
                }
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                EndpointUrl = string.Equals(backendName, "OpenAI", StringComparison.Ordinal)
                   ? "http://localhost:" + port + "/v1"
                   : "http://localhost:" + port;
                requestTask = Task.Run(ServeOnce);
            }

            public string EndpointUrl { get; private set; }
            public string RequestPath { get; private set; }
            public string RequestHeaders { get; private set; }

            public string WaitForRequest()
            {
                return requestTask.GetAwaiter().GetResult();
            }

            public void Dispose()
            {
                listener.Stop();
            }

            private string ServeOnce()
            {
                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] data = ReadRequest(stream);
                    int headerEnd = FindHeaderEnd(data);
                    string requestHeaders = Encoding.ASCII.GetString(data, 0, headerEnd);
                    RequestHeaders = requestHeaders;
                    string requestLine = requestHeaders.Split(new string[] { "\r\n" }, StringSplitOptions.None)[0];
                    string[] requestLineParts = requestLine.Split(' ');
                    RequestPath = requestLineParts.Length > 1 ? requestLineParts[1] : string.Empty;
                    string body = Encoding.UTF8.GetString(data, headerEnd + 4, data.Length - headerEnd - 4);
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseBody);
                    string headers = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
                       responseBytes.Length + "\r\nConnection: close\r\n\r\n";
                    byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                    stream.Write(headerBytes, 0, headerBytes.Length);
                    stream.Write(responseBytes, 0, responseBytes.Length);
                    stream.Flush();
                    return body;
                }
            }

            private static byte[] ReadRequest(NetworkStream stream)
            {
                using (MemoryStream buffer = new MemoryStream())
                {
                    byte[] chunk = new byte[4096];
                    int expectedLength = -1;
                    while (true)
                    {
                        int read = stream.Read(chunk, 0, chunk.Length);
                        if (read <= 0) break;
                        buffer.Write(chunk, 0, read);
                        byte[] current = buffer.ToArray();
                        int headerEnd = FindHeaderEnd(current);
                        if (headerEnd >= 0 && expectedLength < 0)
                        {
                            string headers = Encoding.ASCII.GetString(current, 0, headerEnd);
                            expectedLength = headerEnd + 4 + GetContentLength(headers);
                        }
                        if (expectedLength >= 0 && buffer.Length >= expectedLength) return current;
                    }
                    return buffer.ToArray();
                }
            }

            private static int GetContentLength(string headers)
            {
                string[] lines = headers.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        return int.Parse(lines[i].Substring("Content-Length:".Length).Trim());
                    }
                }
                return 0;
            }

            private static int FindHeaderEnd(byte[] data)
            {
                for (int i = 0; i + 3 < data.Length; i++)
                {
                    if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10) return i;
                }
                return -1;
            }
        }
    }
}
