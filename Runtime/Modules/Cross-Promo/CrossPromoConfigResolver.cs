#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Разбирает прямой конфиг креативов либо выбирает его URL из мастер-конфига.
    /// Общая логика для рантайма и инструментов редактора; сеть и кэш здесь не используются.
    /// </summary>
    public static class CrossPromoConfigResolver
    {
        [Serializable]
        private class JsonString
        {
            public string Value;
        }

        [Serializable]
        private class ConfigDocument
        {
            public float Weight;
            public List<PromoConfiguration> Videos;
            public List<PackageRoute> Packages;
        }

        [Serializable]
        private class PackageRoute
        {
            public string PackageName;
            public string ConfigUrl;
        }

        /// <summary>
        /// При успехе заполняет либо <paramref name="config"/> (креативы), либо
        /// <paramref name="resolvedUrl"/> (маршрут текущего package name).
        /// Для документа по выбранному маршруту передай allowMaster = false:
        /// цепочки мастер-конфигов не поддерживаются.
        /// </summary>
        public static bool TryParse(string json, string packageName, bool allowMaster,
            out PromosConfigurationInfo config, out string resolvedUrl, out string error)
        {
            config = null;
            resolvedUrl = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON is empty.";
                return false;
            }

            string documentJson = json.Trim();
            if (!documentJson.StartsWith("{", StringComparison.Ordinal) ||
                !documentJson.EndsWith("}", StringComparison.Ordinal))
            {
                error = "JSON root must be an object.";
                return false;
            }

            ConfigDocument document;
            bool hasPackages;
            bool hasVideos;
            try
            {
                // Unity может создать отсутствующие List пустыми (UUM-99471).
                // Определяем формат по корневым полям исходного JSON, а не по null в DTO.
                if (!TryReadRootArrays(documentJson, out hasPackages, out hasVideos, out error))
                    return false;
                document = JsonUtility.FromJson<ConfigDocument>(documentJson);
            }
            catch (Exception ex)
            {
                error = $"Malformed JSON: {ex.Message}";
                return false;
            }

            if (!hasPackages && !hasVideos)
            {
                error = "JSON must contain a Packages array or a Videos array.";
                return false;
            }

            if (document == null || (hasPackages && document.Packages == null) ||
                (hasVideos && document.Videos == null))
            {
                error = "Could not deserialize the root Packages or Videos array.";
                return false;
            }

            if (hasPackages)
            {
                if (!allowMaster)
                {
                    error = "Nested master JSON is not supported; ConfigUrl must point to a creative config.";
                    return false;
                }

                if (hasVideos)
                {
                    error = "JSON cannot contain both Packages and Videos arrays.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(packageName))
                {
                    error = "Current application package name is empty.";
                    return false;
                }

                PackageRoute selectedRoute = null;
                foreach (var route in document.Packages)
                {
                    if (route == null || !string.Equals(route.PackageName, packageName, StringComparison.Ordinal))
                        continue;

                    if (selectedRoute != null)
                    {
                        error = $"Master JSON contains duplicate entries for package '{packageName}'.";
                        return false;
                    }

                    selectedRoute = route;
                }

                if (selectedRoute == null)
                {
                    error = $"Master JSON has no entry for package '{packageName}'.";
                    return false;
                }

                if (!Uri.TryCreate(selectedRoute.ConfigUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                    string.IsNullOrEmpty(uri.Host))
                {
                    error = $"ConfigUrl for package '{packageName}' must be an absolute HTTP(S) URL.";
                    return false;
                }

                // Возвращаем исходную строку: нормализация URI может повредить подписанный URL.
                resolvedUrl = selectedRoute.ConfigUrl;
                return true;
            }

            document.Videos.RemoveAll(video => video == null);
            config = new PromosConfigurationInfo
            {
                Weight = document.Weight,
                Videos = document.Videos
            };
            return true;
        }

        // Это проверка присутствия корневых массивов, а не замена JSON-парсера.
        // Содержимое и синтаксис документа по-прежнему разбирает JsonUtility.
        private static bool TryReadRootArrays(string json, out bool hasPackages, out bool hasVideos,
            out string error)
        {
            hasPackages = false;
            hasVideos = false;
            error = null;
            int depth = 0;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0 && i == json.Length - 1)
                        return true;
                    if (depth <= 0)
                    {
                        error = "JSON contains data after the root object.";
                        return false;
                    }
                }
                else if (c == '"')
                {
                    int start = i++;
                    while (i < json.Length && json[i] != '"')
                    {
                        if (json[i] == '\\') i++;
                        i++;
                    }
                    if (i >= json.Length)
                    {
                        error = "JSON contains an unterminated string.";
                        return false;
                    }
                    if (depth != 1)
                        continue;

                    int next = i + 1;
                    while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                    if (next >= json.Length || json[next] != ':')
                        continue;

                    string token = json.Substring(start, i - start + 1);
                    string name = JsonUtility.FromJson<JsonString>("{\"Value\":" + token + "}")?.Value;
                    if (name != "Packages" && name != "Videos")
                        continue;

                    if ((name == "Packages" && hasPackages) || (name == "Videos" && hasVideos))
                    {
                        error = $"JSON contains a duplicate root '{name}' field.";
                        return false;
                    }

                    next++;
                    while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                    if (next >= json.Length || json[next] != '[')
                    {
                        error = $"Root '{name}' must be a JSON array.";
                        return false;
                    }

                    if (name == "Packages") hasPackages = true;
                    else hasVideos = true;
                }
            }

            error = "JSON root object is not closed.";
            return false;
        }
    }
}
#endif
