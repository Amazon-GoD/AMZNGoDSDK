#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Все PlayerPrefs-ключи IAP в одном месте. Разделитель полей — 0x1F (unit separator):
    /// receiptId и SKU — непрозрачные строки от Amazon и из конфига игры, в них может
    /// встретиться что угодно печатное.
    /// </summary>
    internal static class IapPrefsKeys
    {
        public const string SchemaVersion = "AMZN_IapSchemaVersion";
        public const string GrantedReceipts = "AMZN_GrantedReceipts";
        public const string PendingFulfillment = "AMZN_PendingFulfillment";
        public const string Entitlements = "AMZN_Entitlements";
        public const string EntitlementReconciledAt = "AMZN_EntitlementReconciledAt";
        public const string PeriodJournal = "AMZN_PeriodJournal";
        public const string EverPurchasedSkus = "AMZN_EverPurchasedSkus";
        public const string LiveGrantProtection = "AMZN_LiveGrantProtection";

        public const string LegacyFulfilledReceipts = "AMZN_FulfilledReceipts";
        public const string LegacySubscriptionExpiresPrefix = "SubscriptionExpires_";
        public const string LegacySubscriptionStatusPrefix = "SubscriptionStatus_";

        public const char FieldSep = '\u001f';
        public const char LineSep = '\n';
    }

    /// <summary>
    /// Персистентные журналы чеков. Три коллекции с разными ролями — их разведение и есть
    /// суть IAP-12 (раньше одно множество _fulfilledReceipts исполняло обе роли):
    ///  • GrantedReceipts — «право/товар по чеку уже выдан» (идемпотентность выдачи);
    ///  • PendingFulfillment — «выдано, но Amazon не подтвердил»: NotifyFulfillment
    ///    повторяется при каждом старте БЕЗ повторной выдачи, убирается только по успеху;
    ///  • PeriodJournal — последний выданный период подписки по ReceiptId (IAP-14).
    ///
    /// Всё копится в памяти и сохраняется одним PlayerPrefs.Save() по завершении обработки
    /// ответа и в OnApplicationPause — по чеку на запись было O(n²) по строкам и n записей
    /// на диск в главном потоке (IAP-24, фриз на первом запуске после переустановки).
    /// </summary>
    internal sealed class IapReceiptJournal
    {
        // IAP-24: верхняя граница журнала выдач. Журнал защищает от повторной выдачи чека,
        // который Amazon ещё возвращает в истории; подтверждённые расходуемые Amazon из
        // истории убирает, так что старые записи мертвы — режем с головы (порядок вставки),
        // не трогая pending (такой чек ещё вернётся) и подписочные из журнала периодов
        // (ReceiptId один на всю жизнь подписки).
        private const int MaxGrantedReceipts = 2000;

        private readonly HashSet<string> _granted = new();
        private readonly List<string> _grantedOrder = new();
        private readonly HashSet<string> _pendingFulfillment = new();
        private readonly Dictionary<string, int> _periods = new();
        private bool _dirty;

        public IReadOnlyCollection<string> PendingFulfillment => _pendingFulfillment;
        public int GrantedCount => _granted.Count;

        public void Load()
        {
            _granted.Clear();
            _grantedOrder.Clear();
            _pendingFulfillment.Clear();
            _periods.Clear();

            var rawGranted = PlayerPrefs.GetString(IapPrefsKeys.GrantedReceipts, "");
            if (!string.IsNullOrEmpty(rawGranted))
            {
                foreach (var id in rawGranted.Split(new[] { IapPrefsKeys.LineSep }, StringSplitOptions.RemoveEmptyEntries))
                    if (_granted.Add(id))
                        _grantedOrder.Add(id);
            }

            ParseSet(PlayerPrefs.GetString(IapPrefsKeys.PendingFulfillment, ""), _pendingFulfillment);

            var rawPeriods = PlayerPrefs.GetString(IapPrefsKeys.PeriodJournal, "");
            foreach (var line in rawPeriods.Split(new[] { IapPrefsKeys.LineSep }, StringSplitOptions.RemoveEmptyEntries))
            {
                int sep = line.IndexOf(IapPrefsKeys.FieldSep);
                if (sep <= 0)
                    continue;
                if (int.TryParse(line.Substring(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    _periods[line.Substring(0, sep)] = index;
            }
        }

        public bool IsGranted(string receiptId) =>
            !string.IsNullOrEmpty(receiptId) && _granted.Contains(receiptId);

        public void MarkGranted(string receiptId)
        {
            if (string.IsNullOrEmpty(receiptId))
                return;
            if (_granted.Add(receiptId))
            {
                _grantedOrder.Add(receiptId);
                _dirty = true;
            }
        }

        public void MarkPendingFulfillment(string receiptId)
        {
            if (string.IsNullOrEmpty(receiptId))
                return;
            if (_pendingFulfillment.Add(receiptId))
                _dirty = true;
        }

        public void ClearPendingFulfillment(string receiptId)
        {
            if (_pendingFulfillment.Remove(receiptId))
                _dirty = true;
        }

        public bool TryGetLastFiredPeriod(string receiptId, out int index) =>
            _periods.TryGetValue(receiptId, out index);

        public void SetLastFiredPeriod(string receiptId, int index)
        {
            if (string.IsNullOrEmpty(receiptId))
                return;
            _periods[receiptId] = index;
            _dirty = true;
        }

        public void SaveIfDirty()
        {
            if (!_dirty)
                return;
            _dirty = false;

            TrimGrantedIfNeeded();

            PlayerPrefs.SetString(IapPrefsKeys.GrantedReceipts, string.Join(IapPrefsKeys.LineSep.ToString(), _grantedOrder));
            PlayerPrefs.SetString(IapPrefsKeys.PendingFulfillment, string.Join(IapPrefsKeys.LineSep.ToString(), _pendingFulfillment));

            var sb = new StringBuilder();
            foreach (var kvp in _periods)
            {
                if (sb.Length > 0) sb.Append(IapPrefsKeys.LineSep);
                sb.Append(kvp.Key).Append(IapPrefsKeys.FieldSep).Append(kvp.Value.ToString(CultureInfo.InvariantCulture));
            }
            PlayerPrefs.SetString(IapPrefsKeys.PeriodJournal, sb.ToString());

            PlayerPrefs.Save();
        }

        private void TrimGrantedIfNeeded()
        {
            if (_grantedOrder.Count <= MaxGrantedReceipts)
                return;

            int removed = 0;
            for (int i = 0; i < _grantedOrder.Count && _grantedOrder.Count - removed > MaxGrantedReceipts;)
            {
                var id = _grantedOrder[i];
                if (_pendingFulfillment.Contains(id) || _periods.ContainsKey(id))
                {
                    i++;
                    continue;
                }
                _granted.Remove(id);
                _grantedOrder.RemoveAt(i);
                removed++;
            }

            if (removed > 0)
                Debug.Log($"[AMZNGoDSDK] Granted-receipt journal trimmed: {removed} oldest fulfilled entries dropped (cap {MaxGrantedReceipts})");
        }

        private static void ParseSet(string raw, HashSet<string> target)
        {
            if (string.IsNullOrEmpty(raw))
                return;
            foreach (var id in raw.Split(new[] { IapPrefsKeys.LineSep }, StringSplitOptions.RemoveEmptyEntries))
                target.Add(id);
        }

        /// <summary>
        /// Одноразовая миграция хранилища на схему v1. Идемпотентна (флаг SchemaVersion),
        /// запускается строго ДО первого обращения к Amazon. Образец механики —
        /// DataLoader.MigrateLegacyAnalytics.
        ///
        /// Критичное: AMZN_GrantedReceipts засевается содержимым старого
        /// AMZN_FulfilledReceipts. Без этого у каждого существующего игрока вся история
        /// расходуемых чеков посчиталась бы невыданной и выдалась ПОВТОРНО на первом же
        /// запуске новой версии. Сам старый ключ не удаляем один релиз — страховка отката.
        /// </summary>
        public static void MigrateIfNeeded(IEnumerable<string> longLivedSkus)
        {
            if (PlayerPrefs.GetInt(IapPrefsKeys.SchemaVersion, 0) >= 1)
                return;

            var legacy = PlayerPrefs.GetString(IapPrefsKeys.LegacyFulfilledReceipts, "");
            int seededReceipts = 0;
            if (!string.IsNullOrEmpty(legacy))
            {
                // Старый формат — receiptId через '\n', как и новый: переносим как есть.
                PlayerPrefs.SetString(IapPrefsKeys.GrantedReceipts, legacy);
                seededReceipts = legacy.Split(new[] { IapPrefsKeys.LineSep }, StringSplitOptions.RemoveEmptyEntries).Length;
            }

            // Засев прав из legacy-даты, чтобы подписчик не терял доступ на время первой
            // сверки. Значение локальное и Amazon его не подтверждал; вместе с ним пишем
            // отметку сверки «сейчас». Онлайн-экспозиция — секунды до первого ответа
            // GetPurchaseUpdates; в худшем случае (обновился и ушёл в офлайн) незаслуженный
            // доступ живёт до грейс-периода IapEntitlementStore.GraceDays — осознанный
            // трейдофф в пользу честного подписчика.
            var entitled = new List<string>();
            foreach (var sku in longLivedSkus)
            {
                var stored = PlayerPrefs.GetString(IapPrefsKeys.LegacySubscriptionExpiresPrefix + sku, "");
                if (!string.IsNullOrEmpty(stored)
                    && DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires)
                    && expires.ToUniversalTime() > DateTime.UtcNow)
                {
                    entitled.Add(sku);
                }

                PlayerPrefs.DeleteKey(IapPrefsKeys.LegacySubscriptionExpiresPrefix + sku);
                PlayerPrefs.DeleteKey(IapPrefsKeys.LegacySubscriptionStatusPrefix + sku);   // write-only ключ, IAP-23
            }

            if (entitled.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var sku in entitled)
                {
                    if (sb.Length > 0) sb.Append(IapPrefsKeys.LineSep);
                    sb.Append(sku).Append(IapPrefsKeys.FieldSep).Append('E');
                }
                PlayerPrefs.SetString(IapPrefsKeys.Entitlements, sb.ToString());
                PlayerPrefs.SetString(IapPrefsKeys.EntitlementReconciledAt, DateTime.UtcNow.ToString("o"));
            }

            PlayerPrefs.SetInt(IapPrefsKeys.SchemaVersion, 1);
            PlayerPrefs.Save();

            // INFO-лог в релизе намеренно: если засев не сработал, это видно с устройства.
            Debug.Log($"[AMZNGoDSDK] IAP storage migrated to v1: receipts={seededReceipts}, entitled seeded={entitled.Count}");
        }
    }
}
#endif
