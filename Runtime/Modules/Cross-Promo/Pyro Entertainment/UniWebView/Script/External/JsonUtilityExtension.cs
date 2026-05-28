using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniWebViewExternal {
    // A JSON utility extension which supports top-level simple types, arrays, and generic lists.
    public static class UniWebViewJsonUtility {
        
        public static T FromJson<T>(string json) {
            if (json == null) {
                return default(T);
            }

            json = json.Trim();
            Type type = typeof(T);
            
            if (IsSimpleType(type)) {
                return ParseSimpleType<T>(json);
            }

            if (type.IsArray) {
                return ParseArray<T>(json);
            }

            if (IsGenericList(type)) {
                return ParseList<T>(json);
            }

            try {
                return JsonUtility.FromJson<T>(json);
            } catch {
                // 如果失败，尝试包装后解析
                return ParseWrappedValue<T>(json);
            }
        }

        public static string ToJson(object obj, bool prettyPrint = false) {
            if (obj == null) {
                return "null";
            }

            Type type = obj.GetType();
            
            if (IsSimpleType(type)) {
                return ConvertSimpleTypeToJson(obj);
            }

            if (type.IsArray) {
                return ConvertArrayToJson(obj, prettyPrint);
            }

            if (IsGenericList(type)) {
                return ConvertListToJson(obj, prettyPrint);
            }

            // Use optimized approach: try Unity first, fallback to reflection if needed
            return ToJsonOptimized(obj, prettyPrint);
        }

        private static bool IsSimpleType(Type type) {
            return type == typeof(string) ||
                   type == typeof(int) ||
                   type == typeof(float) ||
                   type == typeof(double) ||
                   type == typeof(bool) ||
                   type == typeof(long) ||
                   type == typeof(short) ||
                   type == typeof(byte) ||
                   type == typeof(char);
        }

        private static bool IsGenericList(Type type) {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        // Cache for type serialization capability to avoid repeated reflection
        private static readonly System.Collections.Generic.Dictionary<System.Type, bool> _typeSerializabilityCache 
            = new System.Collections.Generic.Dictionary<System.Type, bool>();

        private static string ToJsonOptimized(object obj, bool prettyPrint) {
            Type type = obj.GetType();
            
            // Check cache first
            bool canUseUnityJson;
            if (_typeSerializabilityCache.TryGetValue(type, out canUseUnityJson)) {
                if (canUseUnityJson) {
                    return JsonUtility.ToJson(obj, prettyPrint);
                } else {
                    return ConvertObjectWithReflection(obj);
                }
            }
            
            // Not in cache, determine serializability using fast checks first
            bool fastCheck = CanUseUnityJsonUtilityFast(type);
            if (fastCheck) {
                // Fast check says it should work, use Unity JsonUtility
                _typeSerializabilityCache[type] = true;
                return JsonUtility.ToJson(obj, prettyPrint);
            }
            
            // Fast check failed, try Unity JsonUtility and verify result
            string unityResult = JsonUtility.ToJson(obj, prettyPrint);
            if (unityResult != "{}" || !HasSerializableContent(obj)) {
                // Unity succeeded or object is actually empty
                _typeSerializabilityCache[type] = true;
                return unityResult;
            }
            
            // Unity failed, use reflection and cache the result
            _typeSerializabilityCache[type] = false;
            return ConvertObjectWithReflection(obj);
        }

        private static bool CanUseUnityJsonUtilityFast(System.Type type) {
            // Fast checks without serialization test
            
            // Check if type has [Serializable] attribute
            if (System.Attribute.IsDefined(type, typeof(System.SerializableAttribute))) {
                return true;
            }
            
            // Check if it's a MonoBehaviour or ScriptableObject
            if (typeof(MonoBehaviour).IsAssignableFrom(type) || 
                typeof(ScriptableObject).IsAssignableFrom(type)) {
                return true;
            }
            
            // Check if it's a struct (structs are serializable by default in Unity)
            if (type.IsValueType && !type.IsEnum && !type.IsPrimitive) {
                return true;
            }
            
            // Anonymous types and non-serializable classes will fail fast check
            return false;
        }

        
        private static bool HasSerializableContent(object obj) {
            if (obj == null) return false;
            
            Type type = obj.GetType();
            
            // Check if object has any public fields or properties
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            
            foreach (var prop in properties) {
                if (prop.CanRead && prop.GetIndexParameters().Length == 0) {
                    try {
                        var value = prop.GetValue(obj);
                        if (value != null) return true;
                    } catch {
                        // Skip properties that can't be read
                    }
                }
            }
            
            foreach (var field in fields) {
                try {
                    var value = field.GetValue(obj);
                    if (value != null) return true;
                } catch {
                    // Skip fields that can't be read
                }
            }
            
            return false;
        }

        private static T ParseSimpleType<T>(string json) {
            Type type = typeof(T);
            json = json.Trim();

            try {
                if (type == typeof(string)) {
                    // 移除引号
                    if (json.StartsWith("\"") && json.EndsWith("\"")) {
                        json = json.Substring(1, json.Length - 2);
                        
                        json = json.Replace("\\\"", "\"")
                            .Replace("\\\\", "\\")
                            .Replace("\\n", "\n")
                            .Replace("\\r", "\r")
                            .Replace("\\t", "\t");
                    }

                    return (T)(object)json;
                } else if (type == typeof(int)) {
                    return (T)(object)int.Parse(json);
                } else if (type == typeof(float)) {
                    return (T)(object)float.Parse(json);
                } else if (type == typeof(double)) {
                    return (T)(object)double.Parse(json);
                } else if (type == typeof(bool)) {
                    return (T)(object)bool.Parse(json);
                } else if (type == typeof(long)) {
                    return (T)(object)long.Parse(json);
                } else if (type == typeof(short)) {
                    return (T)(object)short.Parse(json);
                } else if (type == typeof(byte)) {
                    return (T)(object)byte.Parse(json);
                } else if (type == typeof(char)) {
                    if (json.StartsWith("\"") && json.EndsWith("\"") && json.Length == 3) {
                        return (T)(object)json[1];
                    }

                    return (T)(object)char.Parse(json);
                }
            } catch (Exception e) {
                UniWebViewLogger.Instance.Critical($"Failed to parse simple type {type.Name}: {e.Message}");
            }

            return default(T);
        }

        private static T ParseArray<T>(string json) {
            Type elementType = typeof(T).GetElementType();
            
            string wrappedJson = $"{{\"items\":{json}}}";
            
            Type wrapperType = typeof(ArrayWrapper<>).MakeGenericType(elementType);
            object wrapper = JsonUtility.FromJson(wrappedJson, wrapperType);

            var itemsField = wrapperType.GetField("items");
            return (T)itemsField.GetValue(wrapper);
        }

        private static T ParseList<T>(string json) {
            Type elementType = typeof(T).GetGenericArguments()[0];
            
            string wrappedJson = $"{{\"items\":{json}}}";
            
            Type wrapperType = typeof(ArrayWrapper<>).MakeGenericType(elementType);
            object wrapper = JsonUtility.FromJson(wrappedJson, wrapperType);
            
            var itemsField = wrapperType.GetField("items");
            Array array = (Array)itemsField.GetValue(wrapper);
            
            Type listType = typeof(List<>).MakeGenericType(elementType);
            var list = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");

            foreach (var item in array) {
                addMethod.Invoke(list, new[] { item });
            }

            return (T)list;
        }

        private static T ParseWrappedValue<T>(string json) {
            string wrappedJson = $"{{\"value\":{json}}}";

            try {
                var wrapper = JsonUtility.FromJson<ValueWrapper<T>>(wrappedJson);
                return wrapper.value;
            } catch {
                UniWebViewLogger.Instance.Critical($"Failed to parse JSON: {json}");
                return default(T);
            }
        }

        private static string ConvertSimpleTypeToJson(object obj) {
            Type type = obj.GetType();

            if (type == typeof(string)) {
                string str = (string)obj;

                str = str.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
                return $"\"{str}\"";
            } else if (type == typeof(bool)) {
                return obj.ToString().ToLower();
            } else if (type == typeof(float) || type == typeof(double)) {
                return obj.ToString().Replace(",", ".");
            } else if (type == typeof(char)) {
                return $"\"{obj}\"";
            } else {
                return obj.ToString();
            }
        }

        private static string ConvertArrayToJson(object obj, bool prettyPrint) {
            Array array = (Array)obj;
            Type elementType = obj.GetType().GetElementType();
            
            // Handle object arrays or mixed-type arrays using reflection
            if (elementType == typeof(object) || !CanUseUnityJsonUtilityFast(elementType)) {
                var sb = new System.Text.StringBuilder("[");
                for (int i = 0; i < array.Length; i++) {
                    if (i > 0) sb.Append(",");
                    sb.Append(ConvertObjectWithReflection(array.GetValue(i)));
                }
                sb.Append("]");
                return sb.ToString();
            }
            
            // Use Unity's JsonUtility for homogeneous typed arrays
            Type wrapperType = typeof(ArrayWrapper<>).MakeGenericType(elementType);
            object wrapper = Activator.CreateInstance(wrapperType);
            
            var itemsField = wrapperType.GetField("items");
            itemsField.SetValue(wrapper, array);
            
            string json = JsonUtility.ToJson(wrapper, prettyPrint);
            
            int start = json.IndexOf("[");
            int end = json.LastIndexOf("]") + 1;

            return json.Substring(start, end - start);
        }

        private static string ConvertListToJson(object obj, bool prettyPrint) {
            Type elementType = obj.GetType().GetGenericArguments()[0];

            // 转换 List 为数组
            var toArrayMethod = obj.GetType().GetMethod("ToArray");
            Array array = (Array)toArrayMethod.Invoke(obj, null);

            return ConvertArrayToJson(array, prettyPrint);
        }

        private static string ConvertObjectWithReflection(object obj) {
            if (obj == null) return "null";
            
            Type type = obj.GetType();
            
            // Handle basic types that might not be caught by IsSimpleType
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) {
                return ConvertSimpleTypeToJson(obj);
            }
            
            // Handle collections
            if (obj is System.Collections.IEnumerable enumerable && !(obj is string)) {
                var sb = new System.Text.StringBuilder("[");
                bool flag = true;
                foreach (var item in enumerable) {
                    if (!flag) sb.Append(",");
                    sb.Append(ConvertObjectWithReflection(item));
                    flag = false;
                }
                sb.Append("]");
                return sb.ToString();
            }
            
            // Handle objects using reflection
            var json = new System.Text.StringBuilder("{");
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            
            bool first = true;
            
            // Serialize properties
            foreach (var prop in properties) {
                if (prop.CanRead && prop.GetIndexParameters().Length == 0) { // Skip indexers
                    try {
                        var value = prop.GetValue(obj);
                        if (!first) json.Append(",");
                        json.Append("\"").Append(prop.Name).Append("\":");
                        json.Append(ConvertObjectWithReflection(value));
                        first = false;
                    } catch {
                        // Skip properties that can't be read
                    }
                }
            }
            
            // Serialize fields
            foreach (var field in fields) {
                try {
                    var value = field.GetValue(obj);
                    if (!first) json.Append(",");
                    json.Append("\"").Append(field.Name).Append("\":");
                    json.Append(ConvertObjectWithReflection(value));
                    first = false;
                } catch {
                    // Skip fields that can't be read
                }
            }
            
            json.Append("}");
            return json.ToString();
        }

        [Serializable]
        private class ArrayWrapper<T> {
            public T[] items;
        }

        [Serializable]
        private class ValueWrapper<T> {
            public T value;
        }
    }
}
