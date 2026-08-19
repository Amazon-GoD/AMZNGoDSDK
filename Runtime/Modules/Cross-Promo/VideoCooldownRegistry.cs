#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public static class VideoCooldownRegistry
    {
        private const int COOLDOWN_SECONDS = 1200; // 20 минут
        private const string COOLDOWN_KEY_PREFIX = "CrossPromo_Cooldown_";
        private const string LAST_SHOWN_KEY = "CrossPromo_LastShown";

        /// <summary>Записывает текущий UTC timestamp и обновляет last shown title.</summary>
        public static void RecordShown(string title)
        {
            if (string.IsNullOrEmpty(title)) return;

            PlayerPrefs.SetString(COOLDOWN_KEY_PREFIX + title, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            PlayerPrefs.SetString(LAST_SHOWN_KEY, title);
            PlayerPrefs.Save();
        }

        /// <summary>Проверяет: если ключ существует и (UtcNow - stored) &lt; COOLDOWN_SECONDS — возвращает true.</summary>
        public static bool IsOnCooldown(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;

            var stored = PlayerPrefs.GetString(COOLDOWN_KEY_PREFIX + title, "");
            if (!long.TryParse(stored, out long storedTimestamp))
                return false;

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - storedTimestamp < COOLDOWN_SECONDS;
        }

        /// <summary>Удаляет cooldown ключи для всех titles кроме excludeTitle.</summary>
        public static void ClearAllCooldownsExcept(string excludeTitle, IEnumerable<string> allTitles)
        {
            foreach (var title in allTitles)
            {
                if (title != excludeTitle)
                    PlayerPrefs.DeleteKey(COOLDOWN_KEY_PREFIX + title);
            }
            PlayerPrefs.Save();
        }

        /// <summary>Возвращает сохранённый last shown title или null.</summary>
        public static string GetLastShownTitle()
        {
            var val = PlayerPrefs.GetString(LAST_SHOWN_KEY, "");
            return string.IsNullOrEmpty(val) ? null : val;
        }
    }
}
#endif
