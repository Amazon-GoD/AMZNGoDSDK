#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>Чем снапшот мотивировал снятие права — источник причины события
    /// iap_access_revoked (IAP-31): по чеку с датой отмены или по исчезновению чека.</summary>
    internal enum IapSnapshotRevokeCause
    {
        ReceiptCancelled,
        ReceiptGone,
    }

    /// <summary>
    /// Трёхзначное состояние прав (ТЗ IAP-02) и его персист. Инвариант «legacy-bool никогда
    /// не отвечает false из-за НЕЗНАНИЯ» живёт целиком здесь.
    ///
    /// Знание и политика разведены:
    ///  • GetState — знание: Unknown, пока в ЭТОЙ сессии не было успешной полной сверки;
    ///  • GetEffectiveAccess — политика для IsSubscribed/HasReceipt: последнее сохранённое
    ///    значение, с грейсом GraceDays + срок периода подписки (база — выбранное значение,
    ///    ТЗ число не задаёт): без сверки дольше грейса доступ снимается, но по одной
    ///    неудаче — никогда.
    ///
    /// Хранимые значения: 'E' — entitled, 'N' — not entitled, 'G' — entitled, у которого
    /// вышел грейс (доступ снят, событие Revoked уже отправлено один раз — 'G' и есть
    /// защёлка от повторов на каждом запуске).
    ///
    /// NotEntitled право может стать ТОЛЬКО в ApplySnapshot — по успешной полной сверке.
    /// Любой сбой оставляет последнее известное значение (риск R2: массовое снятие прав
    /// у платящих из-за ветки отказа).
    ///
    /// IAP-26: право, выданное живой покупкой, защищено от снапшота отметкой на
    /// LiveGrantProtectionMinutes — история чеков Amazon доезжает с задержкой, и «нет чека»
    /// в первые минуты после покупки не означает «нет права».
    /// </summary>
    internal sealed class IapEntitlementStore
    {
        public const int GraceDays = 7;

        // IAP-26: защитное окно живой выдачи. История чеков у Amazon доезжает с
        // непредсказуемой задержкой: сверка, запущенная сразу после покупки, может ещё не
        // увидеть свежий чек — «нет чека — нет права» отобрало бы только что оплаченное.
        public const int LiveGrantProtectionMinutes = 10;

        private readonly Dictionary<string, char> _stored = new();
        private readonly HashSet<string> _everPurchased = new();

        // IAP-26: SKU → чек живой выдачи + её момент (системное UTC). Персистится:
        // перезапуск до распространения чека иначе снял бы право первой же сверкой на
        // старте. ReceiptId обязателен: чеки того же SKU от прежних ОТМЕНЁННЫХ подписок
        // лежат в истории всегда, и отметка, гасимая «любым чеком SKU», снималась бы ими
        // мгновенно — гонка воспроизводилась при каждой повторной покупке ранее
        // отменённого SKU (лог 2026-08-14).
        private readonly Dictionary<string, LiveGrantMark> _liveGrants = new();

        private readonly struct LiveGrantMark
        {
            public readonly string ReceiptId;
            public readonly DateTime AtUtc;

            public LiveGrantMark(string receiptId, DateTime atUtc)
            {
                ReceiptId = receiptId;
                AtUtc = atUtc;
            }
        }

        private DateTime _reconciledAtUtc = DateTime.MinValue;
        private bool _reconciledThisSession;
        private bool _dirty;

        public bool ReconciledThisSession => _reconciledThisSession;
        public DateTime ReconciledAtUtc => _reconciledAtUtc;

        public void Load()
        {
            _stored.Clear();
            _everPurchased.Clear();
            _liveGrants.Clear();

            var raw = PlayerPrefs.GetString(IapPrefsKeys.Entitlements, "");
            foreach (var line in raw.Split(new[] { IapPrefsKeys.LineSep }, StringSplitOptions.RemoveEmptyEntries))
            {
                int sep = line.IndexOf(IapPrefsKeys.FieldSep);
                if (sep <= 0 || sep != line.Length - 2)
                    continue;
                _stored[line.Substring(0, sep)] = line[line.Length - 1];
            }

            var rawEver = PlayerPrefs.GetString(IapPrefsKeys.EverPurchasedSkus, "");
            foreach (var sku in rawEver.Split(new[] { IapPrefsKeys.LineSep }, StringSplitOptions.RemoveEmptyEntries))
                _everPurchased.Add(sku);

            // Формат: sku SEP receiptId SEP timestamp. Строки старого двухполевого формата
            // молча отбрасываются — окно живёт 10 минут, миграция не нужна.
            var rawLive = PlayerPrefs.GetString(IapPrefsKeys.LiveGrantProtection, "");
            foreach (var line in rawLive.Split(new[] { IapPrefsKeys.LineSep }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(IapPrefsKeys.FieldSep);
                if (parts.Length != 3)
                    continue;
                if (DateTime.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                    _liveGrants[parts[0]] = new LiveGrantMark(parts[1], at.ToUniversalTime());
            }

            var storedAt = PlayerPrefs.GetString(IapPrefsKeys.EntitlementReconciledAt, "");
            if (!string.IsNullOrEmpty(storedAt))
            {
                // Инвариантная культура и RoundtripKind обязательны (IAP-10): голый TryParse
                // конвертирует в локальное время, а в части локалей вовсе проваливается.
                if (DateTime.TryParse(storedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                    _reconciledAtUtc = at.ToUniversalTime();
                else
                    Debug.LogWarning($"[AMZNGoDSDK] Corrupt reconciliation timestamp: '{storedAt}'");
            }
        }

        public IapEntitlementState GetState(string sku)
        {
            if (string.IsNullOrEmpty(sku))
                return IapEntitlementState.Unknown;
            if (!_reconciledThisSession)
                return IapEntitlementState.Unknown;
            return _stored.TryGetValue(sku, out var c) && c == 'E'
                ? IapEntitlementState.Entitled
                : IapEntitlementState.NotEntitled;
        }

        public bool GetEffectiveAccess(string sku)
        {
            if (string.IsNullOrEmpty(sku))
                return false;
            // Грейс уже применён (EvaluateGrace переводит просроченные E в G),
            // поэтому здесь достаточно последнего сохранённого значения.
            return _stored.TryGetValue(sku, out var c) && c == 'E';
        }

        public IapEntitlement GetEntitlement(string sku) =>
            new IapEntitlement(sku, GetState(sku), GetEffectiveAccess(sku), _reconciledAtUtc);

        public void MarkEverPurchased(string sku)
        {
            if (string.IsNullOrEmpty(sku))
                return;
            if (_everPurchased.Add(sku))
                _dirty = true;
        }

        public bool HasEverPurchased(string sku) =>
            !string.IsNullOrEmpty(sku) && _everPurchased.Contains(sku);

        /// <summary>SKU с действующим правом — для доигрывания поздним подписчикам и событий состояния.</summary>
        public IEnumerable<string> EntitledSkus
        {
            get
            {
                foreach (var kvp in _stored)
                    if (kvp.Value == 'E')
                        yield return kvp.Key;
            }
        }

        /// <summary>Когда-то купленные SKU без действующего права — для доигрывания события Revoked.</summary>
        public IEnumerable<string> RevokedOwnedSkus
        {
            get
            {
                foreach (var kvp in _stored)
                    if (kvp.Value != 'E' && _everPurchased.Contains(kvp.Key))
                        yield return kvp.Key;
            }
        }

        /// <summary>
        /// Грейс: доступ по сохранённому 'E' снимается, если успешной сверки не было дольше
        /// GraceDays + срок периода подписки этого SKU (termDaysBySku; 0 — разовые покупки
        /// и неизвестные SKU). Срок прибавляется к базе, а не заменяет её: при пороге ровно
        /// в TermDays недельная подписка снималась бы у игрока, ушедшего в офлайн на один
        /// оплаченный период, хотя он продолжает платить. Вызывается на форграунде и при
        /// исчерпании ретраев сверки (IAP-32: грейс означает «пытались проверить и не
        /// смогли», а не «прошло время» — на старте до сверки НЕ вызывается). Возвращает
        /// SKU, потерявшие доступ, — по одному событию Revoked на каждый ('E' → 'G' и есть
        /// защёлка).
        /// </summary>
        public List<string> EvaluateGrace(DateTime nowUtc, Func<string, int> termDaysBySku)
        {
            var revoked = new List<string>();

            if (_reconciledThisSession)
                return revoked;
            if (_reconciledAtUtc == DateTime.MinValue)
                return revoked;   // E без отметки не бывает: миграция и живая выдача пишут якорь вместе с ним

            double daysWithoutReconcile = (nowUtc - _reconciledAtUtc).TotalDays;
            if (daysWithoutReconcile <= GraceDays)
                return revoked;   // быстрый путь: порог любого SKU не меньше базы

            List<string> expired = null;
            foreach (var kvp in _stored)
                if (kvp.Value == 'E'
                    && daysWithoutReconcile > GraceDays + Math.Max(0, termDaysBySku?.Invoke(kvp.Key) ?? 0))
                    (expired ??= new List<string>()).Add(kvp.Key);

            if (expired == null)
                return revoked;

            foreach (var sku in expired)
            {
                _stored[sku] = 'G';
                revoked.Add(sku);
                _dirty = true;
            }

            Debug.LogWarning($"[AMZNGoDSDK] Entitlement grace expired ({GraceDays}d without reconciliation): {string.Join(", ", revoked)}");
            SaveIfDirty();
            return revoked;
        }

        public sealed class SnapshotDiff
        {
            /// <summary>Все действующие права после сверки — событие состояния, приходит каждый запуск.</summary>
            public readonly List<string> Entitled = new();

            /// <summary>Права, ПОТЕРЯННЫЕ этой сверкой (переход имевшегося доступа в
            /// отсутствие), с мотивом снапшота — причиной события iap_access_revoked (IAP-31).</summary>
            public readonly List<(string Sku, IapSnapshotRevokeCause Cause)> Revoked = new();
        }

        /// <summary>
        /// Применяет результат ПОЛНОЙ успешной сверки целиком (снапшотом): право активно,
        /// только если в полном ответе есть его чек с пустым CancelDate. Только здесь
        /// «нет чека» становится «нет права» — частичный ответ сюда не попадает по
        /// построению (сигнатура принимает результат завершённой сессии, не отдельный чек).
        /// </summary>
        public SnapshotDiff ApplySnapshot(IapReconcileResult result, ISet<string> configuredLongLived, DateTime nowUtc)
        {
            var diff = new SnapshotDiff();

            // SKU, чей чек присутствует в полном ответе В ЛЮБОМ виде (включая отменённый),
            // — для различения причин отзыва IAP-31; ReceiptId — для снятия отметок IAP-26.
            var seenSkus = new HashSet<string>();
            var seenReceiptIds = new HashSet<string>();
            foreach (var receipt in result.Receipts)
            {
                seenSkus.Add(receipt.Sku);
                seenReceiptIds.Add(receipt.ReceiptId);
            }

            // IAP-26: отметка снимается, когда до снапшота впервые доехал ИМЕННО чек живой
            // покупки (по ReceiptId — дальше SKU живёт по общим правилам, отменённый чек
            // честно снимет право), либо когда окно вышло. Сравнивать по SKU нельзя: чеки
            // прежних отменённых подписок того же SKU лежат в истории всегда и гасили бы
            // защиту мгновенно. Строго по SKU-ключу: сверка, запущенная одной покупкой,
            // законно снимает права по другим (параллельный рефанд).
            if (_liveGrants.Count > 0)
            {
                List<string> done = null;
                foreach (var kvp in _liveGrants)
                    if (seenReceiptIds.Contains(kvp.Value.ReceiptId)
                        || (nowUtc - kvp.Value.AtUtc).TotalMinutes > LiveGrantProtectionMinutes)
                        (done ??= new List<string>()).Add(kvp.Key);

                if (done != null)
                    foreach (var sku in done)
                    {
                        _liveGrants.Remove(sku);
                        _dirty = true;
                    }
            }

            foreach (var sku in configuredLongLived)
                ApplySnapshotSku(sku, result.ActiveSkus.Contains(sku), seenSkus, diff);

            // Сохранённые права по SKU, которых в конфиге уже нет (продукт удалили, а не
            // выключили): без переоценки такое 'E' жило бы вечно, включая после рефанда.
            // Критерий тот же — есть действующий чек в полном ответе (без фильтра по
            // конфигу): заплатившим доступ сохраняется, рефанд снимает.
            List<string> unconfigured = null;
            foreach (var sku in _stored.Keys)
                if (!configuredLongLived.Contains(sku))
                    (unconfigured ??= new List<string>()).Add(sku);

            if (unconfigured != null)
                foreach (var sku in unconfigured)
                    ApplySnapshotSku(sku, result.ActiveAnySkus.Contains(sku), seenSkus, diff);

            _reconciledAtUtc = nowUtc;
            _reconciledThisSession = true;
            _dirty = true;
            SaveIfDirty();
            return diff;
        }

        private void ApplySnapshotSku(string sku, bool active, HashSet<string> seenSkus, SnapshotDiff diff)
        {
            bool hadAccess = GetEffectiveAccess(sku);

            // IAP-26: свежая живая выдача, чека в снапшоте ещё нет — «нет чека — нет права»
            // не применяется, пока не выйдет защитное окно. Право остаётся как есть.
            if (!active && _liveGrants.ContainsKey(sku))
            {
                Debug.Log($"[AMZNGoDSDK] Snapshot has no receipt for freshly purchased '{sku}' — " +
                          $"keeping the live grant ({LiveGrantProtectionMinutes} min protection window)");
                if (hadAccess)
                    diff.Entitled.Add(sku);
                return;
            }

            _stored[sku] = active ? 'E' : 'N';

            if (active)
                diff.Entitled.Add(sku);
            else if (hadAccess)
                diff.Revoked.Add((sku, seenSkus.Contains(sku)
                    ? IapSnapshotRevokeCause.ReceiptCancelled
                    : IapSnapshotRevokeCause.ReceiptGone));
        }

        /// <summary>
        /// Живая покупка — единственное легальное исключение из «только снапшот»: свежий
        /// SUCCESSFUL-чек может ДОБАВИТЬ право, но никогда не снять. Полная сверка,
        /// запускаемая следом, подтвердит его штатно.
        /// </summary>
        public void ApplyLivePurchaseGrant(string sku, string receiptId)
        {
            if (string.IsNullOrEmpty(sku))
                return;

            // Якорь для грейса: если сверки не было ещё ни разу (свежая установка, купил при
            // падающей сети), без отметки времени EvaluateGrace никогда бы не сработал и
            // право зависло бы включённым. Это НЕ отметка сверки — ReconciledThisSession
            // остаётся false.
            if (_reconciledAtUtc == DateTime.MinValue)
            {
                _reconciledAtUtc = DateTime.UtcNow;
                _dirty = true;
            }

            // IAP-26: отметка защитного окна. Время сознательно системное, не доверенное:
            // игрок, переводящий часы вперёд, лишь укорачивает собственную защиту.
            _liveGrants[sku] = new LiveGrantMark(receiptId ?? "", DateTime.UtcNow);
            _dirty = true;

            if (!_stored.TryGetValue(sku, out var c) || c != 'E')
                _stored[sku] = 'E';

            SaveIfDirty();
        }

        public void SaveIfDirty()
        {
            if (!_dirty)
                return;
            _dirty = false;

            var sb = new StringBuilder();
            foreach (var kvp in _stored)
            {
                if (sb.Length > 0) sb.Append(IapPrefsKeys.LineSep);
                sb.Append(kvp.Key).Append(IapPrefsKeys.FieldSep).Append(kvp.Value);
            }
            PlayerPrefs.SetString(IapPrefsKeys.Entitlements, sb.ToString());
            PlayerPrefs.SetString(IapPrefsKeys.EverPurchasedSkus, string.Join(IapPrefsKeys.LineSep.ToString(), _everPurchased));

            var sbLive = new StringBuilder();
            foreach (var kvp in _liveGrants)
            {
                if (sbLive.Length > 0) sbLive.Append(IapPrefsKeys.LineSep);
                sbLive.Append(kvp.Key).Append(IapPrefsKeys.FieldSep)
                      .Append(kvp.Value.ReceiptId).Append(IapPrefsKeys.FieldSep)
                      .Append(kvp.Value.AtUtc.ToString("o"));
            }
            PlayerPrefs.SetString(IapPrefsKeys.LiveGrantProtection, sbLive.ToString());

            if (_reconciledAtUtc != DateTime.MinValue)
                PlayerPrefs.SetString(IapPrefsKeys.EntitlementReconciledAt, _reconciledAtUtc.ToString("o"));

            PlayerPrefs.Save();
        }
    }
}
#endif
