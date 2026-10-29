namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Console ID игры, например <c>F000042-0</c>: буква — тип листинга в Amazon (F — free,
    /// P — paid), 6 цифр — номер игры (семейства) в общем счёте всех аккаунтов, после дефиса —
    /// номер копии (0 — оригинал, без ведущих нулей, до 999). Выдаётся только генератором в
    /// админке BI и вписывается в App title консоли Amazon и в SDK Settings → Analytics: по нему
    /// BI сам связывает бандл, токен Adjust и ASIN новой игры. У старых игр ID нет — поле пустое.
    ///
    /// Формат — <c>^[FP]\d{6}-(0|[1-9]\d{0,2})$</c>, но проверка ручная, а не Regex: в .NET
    /// <c>\d</c> пропускает не-ASCII цифры, а <c>$</c> — завершающий перевод строки, и такой ID
    /// бэкенд и BI уже не примут.
    /// </summary>
    public static class ConsoleId
    {
        /// <summary>Пример для подсказок и сообщений об ошибке.</summary>
        public const string Example = "F000042-0";

        public static bool IsValid(string id)
        {
            // Короче всего F000042-0 (9 символов), длиннее всего F000042-999 (11).
            if (id == null || id.Length < 9 || id.Length > 11)
                return false;
            if (id[0] != 'F' && id[0] != 'P')
                return false;
            for (int i = 1; i <= 6; i++)
            {
                if (!IsAsciiDigit(id[i]))
                    return false;
            }
            if (id[7] != '-')
                return false;

            // Номер копии: «0» только целиком, иначе 1–999 без ведущего нуля.
            if (id[8] == '0')
                return id.Length == 9;
            for (int i = 8; i < id.Length; i++)
            {
                if (!IsAsciiDigit(id[i]))
                    return false;
            }
            return true;
        }

        /// <summary>Буква ID для App Type: F для Free, P для Paid, '\0' для None.</summary>
        public static char LetterFor(AnalyticsAppType appType) => appType switch
        {
            AnalyticsAppType.Free => 'F',
            AnalyticsAppType.Paid => 'P',
            _ => '\0'
        };

        /// <summary>ID валиден и его буква совпадает с App Type (F ↔ Free, P ↔ Paid).</summary>
        public static bool MatchesAppType(string id, AnalyticsAppType appType) =>
            IsValid(id) && id[0] == LetterFor(appType);

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
