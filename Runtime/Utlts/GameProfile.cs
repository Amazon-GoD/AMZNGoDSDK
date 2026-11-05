using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Профиль игры в <c>free_first_open</c> / <c>paid_first_open</c> (с 1.0.11): ключи, которые
    /// реально лежат в сборке. BI сверяет их с карточкой игры в админке («Ключи сборки»), так
    /// ловится клон шаблона с чужим Firebase, AppMetrica или AppLovin.
    ///
    /// Значения берутся только из статики сборки: ресурсы и манифест APK (см. <see cref="AndroidAppInfo"/>)
    /// и SDK Settings. Новых настроек нет. Ключ AppMetrica не отправляется, только его отпечаток
    /// (<see cref="KeyFingerprint"/>). Правила значений те же, что на бэкенде и в админке: не
    /// прошедшее проверку значение не отправляется, событие уходит без поля. Лежит в Utlts без #if,
    /// как <see cref="FirstOpenClaim"/>.
    /// </summary>
    public static class GameProfile
    {
        public const string FirebaseProjectIdKey = "firebase_project_id";
        public const string FirebaseAppIdKey = "firebase_app_id";
        public const string AppMetricaKeyFingerprintKey = "appmetrica_key_fp";
        public const string GoogleAppIdKey = "applovin_google_app_id";
        public const string InterUnitKey = "applovin_inter_unit";
        public const string RewardUnitKey = "applovin_reward_unit";
        public const string BannerUnitKey = "applovin_banner_unit";

        /// <summary>Ключи профиля в порядке <see cref="BuildJsonFields"/>.</summary>
        public static readonly IReadOnlyList<string> AllKeys = new[]
        {
            FirebaseProjectIdKey,
            FirebaseAppIdKey,
            AppMetricaKeyFingerprintKey,
            GoogleAppIdKey,
            InterUnitKey,
            RewardUnitKey,
            BannerUnitKey,
        };

        /// <summary>Длина отпечатка ключа AppMetrica: первые 12 hex SHA-256.</summary>
        public const int KeyFingerprintLength = 12;

        private const string Tag = "[GameProfile]";
        private const string GoogleAppIdPrefix = "ca-app-pub-";
        private const string FirebaseAppIdPlatform = ":android:";

        /// <summary>
        /// Что сборка знает о своих ключах. Пусто или null — значения нет (модуль выключен, ресурса
        /// или поля нет). Поля заполняет провайдер в <c>AmznGoDSDKCore</c>, проверяет их
        /// <see cref="BuildJsonFields"/>.
        /// </summary>
        public sealed class Values
        {
            /// <summary>Строковый ресурс <c>project_id</c> (Firebase, из google-services.json).</summary>
            public string FirebaseProjectId;

            /// <summary>Строковый ресурс <c>google_app_id</c> (Firebase, <c>1:&lt;номер&gt;:android:&lt;hex&gt;</c>).</summary>
            public string FirebaseAppId;

            /// <summary>Ключ AppMetrica из SDK Settings. В событие уходит только отпечаток.</summary>
            public string AppMetricaKey;

            /// <summary>AdMob App ID из манифеста (<c>com.google.android.gms.ads.APPLICATION_ID</c>).</summary>
            public string GoogleAppId;

            public string InterUnit;
            public string RewardUnit;
            public string BannerUnit;
        }

        /// <summary>ID проекта Firebase (GCP): <c>^[a-z][a-z0-9-]{4,28}[a-z0-9]$</c>.</summary>
        public static bool IsValidFirebaseProjectId(string value)
        {
            if (value == null || value.Length < 6 || value.Length > 30)
                return false;
            if (!IsLowerLetter(value[0]))
                return false;
            char last = value[value.Length - 1];
            if (!IsLowerLetter(last) && !IsDigit(last))
                return false;
            for (int i = 1; i < value.Length - 1; i++)
            {
                char c = value[i];
                if (!IsLowerLetter(c) && !IsDigit(c) && c != '-')
                    return false;
            }
            return true;
        }

        /// <summary>
        /// App ID приложения в Firebase: <c>^1:[0-9]{1,20}:android:[0-9a-f]{1,40}$</c>.
        /// </summary>
        public static bool IsValidFirebaseAppId(string value)
        {
            if (value == null || !value.StartsWith("1:", StringComparison.Ordinal))
                return false;
            int platform = value.IndexOf(FirebaseAppIdPlatform, 2, StringComparison.Ordinal);
            if (platform < 0)
                return false;
            int numberLength = platform - 2;
            if (numberLength < 1 || numberLength > 20 || !AllDigits(value, 2, numberLength))
                return false;
            int hexStart = platform + FirebaseAppIdPlatform.Length;
            int hexLength = value.Length - hexStart;
            return hexLength >= 1 && hexLength <= 40 && AllLowerHex(value, hexStart, hexLength);
        }

        /// <summary>
        /// Ключ AppMetrica: UUID в нижнем регистре,
        /// <c>^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$</c>.
        /// </summary>
        public static bool IsValidAppMetricaKey(string value)
        {
            if (value == null || value.Length != 36)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                bool dash = i == 8 || i == 13 || i == 18 || i == 23;
                if (dash ? value[i] != '-' : !IsLowerHex(value[i]))
                    return false;
            }
            return true;
        }

        /// <summary>Отпечаток ключа AppMetrica: <c>^[0-9a-f]{12}$</c>.</summary>
        public static bool IsValidKeyFingerprint(string value) =>
            value != null && value.Length == KeyFingerprintLength && AllLowerHex(value, 0, value.Length);

        /// <summary>AdMob App ID: <c>^ca-app-pub-[0-9]{16}~[0-9]{10}$</c>.</summary>
        public static bool IsValidGoogleAppId(string value)
        {
            if (value == null || value.Length != GoogleAppIdPrefix.Length + 27 ||
                !value.StartsWith(GoogleAppIdPrefix, StringComparison.Ordinal))
                return false;
            int publisher = GoogleAppIdPrefix.Length;
            return AllDigits(value, publisher, 16) && value[publisher + 16] == '~' &&
                   AllDigits(value, publisher + 17, 10);
        }

        /// <summary>Ad unit AppLovin MAX: <c>^[0-9a-f]{16}$</c>.</summary>
        public static bool IsValidAdUnit(string value) =>
            value != null && value.Length == 16 && AllLowerHex(value, 0, value.Length);

        /// <summary>
        /// Отпечаток ключа AppMetrica: первые 12 hex <c>sha256(utf8(lower(trim(key))))</c>, та же
        /// формула, что в админке и в SQL BI. Не UUID — null.
        /// </summary>
        public static string KeyFingerprint(string key)
        {
            string normalized = AsciiLower(key?.Trim());
            if (!IsValidAppMetricaKey(normalized))
                return null;

            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));

            var sb = new StringBuilder(KeyFingerprintLength);
            for (int i = 0; i < KeyFingerprintLength / 2; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// Фрагмент JSON с полями профиля, каждое с запятой на конце; встаёт в first_open сразу после
        /// полей <see cref="FirstOpenClaim"/>, перед <c>sdk_version</c>. Порядок фиксирован. Пробелы
        /// по краям срезаются, unit и app id Firebase приводятся к нижнему регистру; неверное или
        /// пустое значение пропускается. null — пустая строка.
        /// </summary>
        public static string BuildJsonFields(Values values)
        {
            if (values == null)
                return string.Empty;

            var sb = new StringBuilder(384);
            // Escape по значениям, прошедшим проверку, ничего не меняет; он здесь, чтобы ослабленная
            // когда-нибудь проверка не превратилась в битый JSON (как в FirstOpenClaim).
            AppendIfValid(sb, FirebaseProjectIdKey, values.FirebaseProjectId?.Trim(), IsValidFirebaseProjectId);
            AppendIfValid(sb, FirebaseAppIdKey, AsciiLower(values.FirebaseAppId?.Trim()), IsValidFirebaseAppId);
            AppendIfValid(sb, AppMetricaKeyFingerprintKey, KeyFingerprint(values.AppMetricaKey), IsValidKeyFingerprint);
            AppendIfValid(sb, GoogleAppIdKey, values.GoogleAppId?.Trim(), IsValidGoogleAppId);
            AppendIfValid(sb, InterUnitKey, AsciiLower(values.InterUnit?.Trim()), IsValidAdUnit);
            AppendIfValid(sb, RewardUnitKey, AsciiLower(values.RewardUnit?.Trim()), IsValidAdUnit);
            AppendIfValid(sb, BannerUnitKey, AsciiLower(values.BannerUnit?.Trim()), IsValidAdUnit);
            return sb.ToString();
        }

        /// <summary>
        /// Только имена полей, которые <see cref="BuildJsonFields"/> отправит, через запятую, или
        /// <c>none</c>. Для лога: значения в лог не выводим.
        /// </summary>
        public static string DescribePresent(Values values)
        {
            string fields = BuildJsonFields(values);
            if (fields.Length == 0)
                return "none";

            var names = new StringBuilder(160);
            foreach (string key in AllKeys)
            {
                if (fields.IndexOf("\"" + key + "\":", StringComparison.Ordinal) < 0)
                    continue;
                if (names.Length > 0)
                    names.Append(',');
                names.Append(key);
            }
            return names.ToString();
        }

        /// <summary>
        /// Вызывает провайдер профиля. Любое исключение — null и предупреждение только с типом
        /// исключения (текст исключения может содержать значение). null-провайдер — null.
        /// </summary>
        public static Values SafeCollect(Func<Values> provider)
        {
            if (provider == null)
                return null;
            try
            {
                return provider();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} Profile read failed ({e.GetType().Name}) — first_open goes without the profile");
                return null;
            }
        }

        private static void AppendIfValid(StringBuilder sb, string key, string value, Func<string, bool> isValid)
        {
            if (!isValid(value))
                return;
            sb.Append('"').Append(key).Append("\":\"").Append(SdkJson.Escape(value)).Append("\",");
        }

        // Только A-Z: ToLowerInvariant превратил бы, например, знак кельвина в латинскую k.
        private static string AsciiLower(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= 'A' && chars[i] <= 'Z')
                    chars[i] = (char)(chars[i] + ('a' - 'A'));
            }
            return new string(chars);
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static bool IsLowerLetter(char c) => c >= 'a' && c <= 'z';

        private static bool IsLowerHex(char c) => IsDigit(c) || (c >= 'a' && c <= 'f');

        private static bool AllDigits(string value, int start, int length)
        {
            for (int i = start; i < start + length; i++)
            {
                if (!IsDigit(value[i]))
                    return false;
            }
            return true;
        }

        private static bool AllLowerHex(string value, int start, int length)
        {
            for (int i = start; i < start + length; i++)
            {
                if (!IsLowerHex(value[i]))
                    return false;
            }
            return true;
        }
    }
}
