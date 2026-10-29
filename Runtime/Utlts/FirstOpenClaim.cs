using System.Text;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Поля, которыми <c>free_first_open</c> / <c>paid_first_open</c> «заявляют» игру для BI
    /// (с 1.0.10): <c>console_id</c>, <c>adjust_app_token</c>, <c>is_debug</c>, <c>installer</c>.
    /// По ним BI сам связывает бандл новой игры с её Console ID, токеном Adjust и ASIN
    /// (см. <see cref="ConsoleId"/>). Других событий это не касается.
    ///
    /// Бэкенд хранит эти поля в <c>events_raw.extra</c> по своему allowlist'у и с теми же
    /// правилами значений, поэтому значение, не прошедшее проверку здесь, просто не отправляется:
    /// событие уходит без поля, как у старых игр. Лежит в Utlts без #if, как <see cref="SdkJson"/>:
    /// модуль Analytics отключаемый, а проверки нужны и окну настроек, и build-гарду.
    /// </summary>
    public static class FirstOpenClaim
    {
        /// <summary>App token Adjust: ровно 12 символов, строчная латиница и цифры.</summary>
        public static bool IsValidAdjustAppToken(string token)
        {
            if (token == null || token.Length != 12)
                return false;
            foreach (char c in token)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9'))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Имя установщика (<c>Application.installerName</c>, например <c>com.amazon.venezia</c>):
        /// 1–64 символа <c>[0-9A-Za-z._+:-]</c> — то же правило, что у коротких строк в extra на бэкенде.
        /// </summary>
        public static bool IsValidInstaller(string installer)
        {
            if (string.IsNullOrEmpty(installer) || installer.Length > 64)
                return false;
            foreach (char c in installer)
            {
                bool allowed = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                               c == '.' || c == '_' || c == '+' || c == ':' || c == '-';
                if (!allowed)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Фрагмент JSON с полями заявки, каждое с запятой на конце; встаёт в first_open перед
        /// <c>sdk_version</c>. Невалидные значения пропускаются, <c>is_debug</c> есть всегда.
        /// </summary>
        /// <param name="adjustAppToken">Ключ Adjust или null, если модуль Adjust выключен.</param>
        /// <param name="isDebug"><c>Debug.isDebugBuild</c>: development-сборка или редактор.</param>
        public static string BuildJsonFields(string consoleId, string adjustAppToken, bool isDebug, string installer)
        {
            var sb = new StringBuilder(128);
            // Escape по значениям, прошедшим проверку выше, ничего не меняет; он здесь, чтобы
            // ослабленная когда-нибудь проверка не превратилась в битый JSON.
            if (ConsoleId.IsValid(consoleId))
                sb.Append("\"console_id\":\"").Append(SdkJson.Escape(consoleId)).Append("\",");
            if (IsValidAdjustAppToken(adjustAppToken))
                sb.Append("\"adjust_app_token\":\"").Append(SdkJson.Escape(adjustAppToken)).Append("\",");
            sb.Append("\"is_debug\":").Append(isDebug ? "true" : "false").Append(',');
            if (IsValidInstaller(installer))
                sb.Append("\"installer\":\"").Append(SdkJson.Escape(installer)).Append("\",");
            return sb.ToString();
        }
    }
}
