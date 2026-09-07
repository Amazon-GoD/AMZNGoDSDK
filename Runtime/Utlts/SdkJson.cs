using System.Text;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Общий JSON-эскейп для событий аналитики (ТЗ IAP-19). Раньше в SDK жили две версии:
    /// полная в AppmetricaModule и урезанная (только слеш и кавычка) в IAP — из-за неё
    /// один перевод строки в названии товара из конфига приводил к тому, что AppMetrica
    /// молча выбрасывала событие. Лежит в Utlts без #if: и AppMetrica, и IAP — отключаемые
    /// модули с переименовываемыми папками, а эскейп нужен обоим.
    /// </summary>
    public static class SdkJson
    {
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
