using System;
using System.Collections.Generic;

namespace XUnity.AutoTranslator.LlmEndpoint.Backends
{
    internal static class JsonSchemaFactory
    {
        public static Dictionary<string, object> CreateTranslationSchema(IList<string> expectedIds)
        {
            return CreateTranslationSchema(expectedIds, true);
        }

        public static Dictionary<string, object> CreateTranslationSchema(
           IList<string> expectedIds,
           bool includeArrayCountConstraints)
        {
            Dictionary<string, object> idSchema = Object();
            idSchema["type"] = "string";
            if (expectedIds != null && expectedIds.Count > 0)
            {
                List<object> values = new List<object>();
                for (int i = 0; i < expectedIds.Count; i++) values.Add(expectedIds[i]);
                idSchema["enum"] = values;
            }

            Dictionary<string, object> translationSchema = Object();
            translationSchema["type"] = "string";

            Dictionary<string, object> itemProperties = Object();
            itemProperties["id"] = idSchema;
            itemProperties["translation"] = translationSchema;

            Dictionary<string, object> itemSchema = Object();
            itemSchema["type"] = "object";
            itemSchema["additionalProperties"] = false;
            itemSchema["properties"] = itemProperties;
            itemSchema["required"] = new List<object>(new object[] { "id", "translation" });

            Dictionary<string, object> arraySchema = Object();
            arraySchema["type"] = "array";
            arraySchema["items"] = itemSchema;
            int count = expectedIds == null ? 0 : expectedIds.Count;
            if (count > 0 && includeArrayCountConstraints)
            {
                arraySchema["minItems"] = count;
                arraySchema["maxItems"] = count;
            }

            Dictionary<string, object> properties = Object();
            properties["items"] = arraySchema;

            Dictionary<string, object> root = Object();
            root["type"] = "object";
            root["additionalProperties"] = false;
            root["properties"] = properties;
            root["required"] = new List<object>(new object[] { "items" });
            return root;
        }

        private static Dictionary<string, object> Object()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }
}
