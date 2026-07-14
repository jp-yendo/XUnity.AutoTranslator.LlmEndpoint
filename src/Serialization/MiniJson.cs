using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XUnity.AutoTranslator.LlmEndpoint.Serialization
{
    internal static class MiniJson
    {
        public static string Serialize(object value)
        {
            StringBuilder builder = new StringBuilder();
            WriteValue(builder, value);
            return builder.ToString();
        }

        public static object Deserialize(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            Parser parser = new Parser(json);
            object value = parser.ParseValue();
            parser.SkipWhiteSpace();
            if (!parser.IsAtEnd) throw new FormatException("Unexpected data after the JSON value.");
            return value;
        }

        public static bool TryDeserializeObject(string text, out Dictionary<string, object> value, out string error)
        {
            value = null;
            error = null;
            if (text == null)
            {
                error = "The response was null.";
                return false;
            }

            string candidate = text.Trim();
            int first = candidate.IndexOf('{');
            int last = candidate.LastIndexOf('}');
            if (first < 0 || last < first)
            {
                error = "The response did not contain a JSON object.";
                return false;
            }
            candidate = candidate.Substring(first, last - first + 1);
            try
            {
                value = Deserialize(candidate) as Dictionary<string, object>;
                if (value == null)
                {
                    error = "The JSON root was not an object.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Invalid JSON: " + ex.Message;
                return false;
            }
        }

        public static string GetString(Dictionary<string, object> value, string key)
        {
            if (value == null) return null;
            object item;
            if (!value.TryGetValue(key, out item)) return null;
            return item as string;
        }

        public static Dictionary<string, object> GetObject(Dictionary<string, object> value, string key)
        {
            if (value == null) return null;
            object item;
            if (!value.TryGetValue(key, out item)) return null;
            return item as Dictionary<string, object>;
        }

        public static List<object> GetArray(Dictionary<string, object> value, string key)
        {
            if (value == null) return null;
            object item;
            if (!value.TryGetValue(key, out item)) return null;
            return item as List<object>;
        }

        private static void WriteValue(StringBuilder builder, object value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            string text = value as string;
            if (text != null)
            {
                WriteString(builder, text);
                return;
            }

            if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
                return;
            }

            IDictionary dictionary = value as IDictionary;
            if (dictionary != null)
            {
                WriteObject(builder, dictionary);
                return;
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                WriteArray(builder, enumerable);
                return;
            }

            if (value is char)
            {
                WriteString(builder, value.ToString());
                return;
            }

            if (IsNumeric(value))
            {
                IFormattable formattable = value as IFormattable;
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            }

            WriteString(builder, value.ToString());
        }

        private static void WriteObject(StringBuilder builder, IDictionary dictionary)
        {
            builder.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!first) builder.Append(',');
                first = false;
                WriteString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                builder.Append(':');
                WriteValue(builder, entry.Value);
            }
            builder.Append('}');
        }

        private static void WriteArray(StringBuilder builder, IEnumerable values)
        {
            builder.Append('[');
            bool first = true;
            foreach (object value in values)
            {
                if (!first) builder.Append(',');
                first = false;
                WriteValue(builder, value);
            }
            builder.Append(']');
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 32 || character == '\u2028' || character == '\u2029')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private static bool IsNumeric(object value)
        {
            return value is byte || value is sbyte || value is short || value is ushort ||
               value is int || value is uint || value is long || value is ulong ||
               value is float || value is double || value is decimal;
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                this.json = json;
            }

            public bool IsAtEnd { get { return index >= json.Length; } }

            public void SkipWhiteSpace()
            {
                while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            }

            public object ParseValue()
            {
                SkipWhiteSpace();
                if (IsAtEnd) throw new FormatException("Unexpected end of JSON.");
                char token = json[index];
                if (token == '{') return ParseObject();
                if (token == '[') return ParseArray();
                if (token == '"') return ParseString();
                if (token == 't') { ExpectLiteral("true"); return true; }
                if (token == 'f') { ExpectLiteral("false"); return false; }
                if (token == 'n') { ExpectLiteral("null"); return null; }
                if (token == '-' || (token >= '0' && token <= '9')) return ParseNumber();
                throw new FormatException("Unexpected JSON token at index " + index + ".");
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
                index++;
                SkipWhiteSpace();
                if (Take('}')) return result;
                while (true)
                {
                    SkipWhiteSpace();
                    if (IsAtEnd || json[index] != '"') throw new FormatException("Expected an object key at index " + index + ".");
                    string key = ParseString();
                    SkipWhiteSpace();
                    Require(':');
                    object value = ParseValue();
                    result[key] = value;
                    SkipWhiteSpace();
                    if (Take('}')) return result;
                    Require(',');
                }
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                index++;
                SkipWhiteSpace();
                if (Take(']')) return result;
                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhiteSpace();
                    if (Take(']')) return result;
                    Require(',');
                }
            }

            private string ParseString()
            {
                Require('"');
                StringBuilder builder = new StringBuilder();
                while (index < json.Length)
                {
                    char character = json[index++];
                    if (character == '"') return builder.ToString();
                    if (character != '\\')
                    {
                        if (character < 32) throw new FormatException("Unescaped control character in JSON string.");
                        builder.Append(character);
                        continue;
                    }
                    if (index >= json.Length) throw new FormatException("Incomplete JSON escape sequence.");
                    char escape = json[index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u': builder.Append(ParseUnicodeEscape()); break;
                        default: throw new FormatException("Unsupported JSON escape sequence.");
                    }
                }
                throw new FormatException("Unterminated JSON string.");
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > json.Length) throw new FormatException("Incomplete JSON unicode escape.");
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    char character = json[index++];
                    value <<= 4;
                    if (character >= '0' && character <= '9') value += character - '0';
                    else if (character >= 'a' && character <= 'f') value += character - 'a' + 10;
                    else if (character >= 'A' && character <= 'F') value += character - 'A' + 10;
                    else throw new FormatException("Invalid JSON unicode escape.");
                }
                return (char)value;
            }

            private object ParseNumber()
            {
                int start = index;
                if (json[index] == '-') index++;
                ReadDigits();
                bool floatingPoint = false;
                if (index < json.Length && json[index] == '.')
                {
                    floatingPoint = true;
                    index++;
                    ReadDigits();
                }
                if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
                {
                    floatingPoint = true;
                    index++;
                    if (index < json.Length && (json[index] == '+' || json[index] == '-')) index++;
                    ReadDigits();
                }
                string value = json.Substring(start, index - start);
                if (!floatingPoint)
                {
                    long integer;
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return integer;
                }
                double number;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
                throw new FormatException("Invalid JSON number.");
            }

            private void ReadDigits()
            {
                int start = index;
                while (index < json.Length && json[index] >= '0' && json[index] <= '9') index++;
                if (start == index) throw new FormatException("Expected a digit in JSON number.");
            }

            private void ExpectLiteral(string literal)
            {
                if (index + literal.Length > json.Length ||
                   !string.Equals(json.Substring(index, literal.Length), literal, StringComparison.Ordinal))
                {
                    throw new FormatException("Invalid JSON literal.");
                }
                index += literal.Length;
            }

            private bool Take(char character)
            {
                if (index < json.Length && json[index] == character)
                {
                    index++;
                    return true;
                }
                return false;
            }

            private void Require(char character)
            {
                if (!Take(character)) throw new FormatException("Expected '" + character + "' at index " + index + ".");
            }
        }
    }
}
