#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Трёхзначное состояние прав (ТЗ IAP-02) и его персист. Инвариант «legacy-bool никогда
    /// не отвечает false из-за НЕЗНАНИЯ» живёт целиком здесь.
    ///
    /// Знание и политика разведены:
    ///  • GetState — знание: Unknown, пока в ЭТОЙ сессии не было успешной полной сверки;
    ///  • GetEffectiveAccess — политика для IsSubscribed/HasReceipt: последнее сохранённое
    ///    значение, с грейсом 7 суток (выбранное значение, ТЗ число не задаёт): без сверки
    ///    дольше грейса доступ снимается, но по одной неудаче — никогда.
    ///
    /// Хранимые значения: 'E' — entitled, 'N' — not entitled, 'G' — entitled, у которого
    /// вышел грейс (доступ снят, событие Revoked уже отправлено один раз — 'G' и есть
    /// защёлка от повторов на каждом запуске).
    ///
    /// NotEntitled право может стать ТОЛЬКО в ApplySnapshot — по успешной полной сверке.
    /// Любой сбой оставляет последнее известное значение (риск R2: массовое снятие прав
    /// у платящих из-за ветки отказа).
    /// </summary>
    internal sealed class IapEntitlementStore
    {
        public const int GraceDays = 7;

        private readonly Dictionary<string, char> _stored = new();
        private readonly HashSet<string> _everPurchased = new();
        private DateTime _reconciledAtUtc = DateTime.MinValue;
        private bool _reconciledThisSession;
        private bool _dirty;

        public bool ReconciledThisSession => _reconciledThisSession;
        public DateTime ReconciledAtUtc => _reconciledAtUtc;

        public void Load()
        {
            _stored.Clear();
            _everPurchased.Clear();

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
        /// Грейс: если успешной сверки не было дольше GraceDays, доступ по сохранённым E
        /// снимается. Вызывается на старте и на форграунде, не по таймеру. Возвращает SKU,
        /// потерявшие доступ, — по одному событию Revoked на каждый ('E' → 'G' и есть защёлка).
        /// </summary>
        public List<string> EvaluateGrace(DateTime nowUtc)
        {
            var revoked = new List<string>();

            if (_reconciledThisSession)
                return revoked;
            if (_reconciledAtUtc == DateTime.MinValue)
                return revoked;   // E без отметки сверки не бывает: миграция пишет их вместе
            if ((nowUtc - _reconciledAtUtc).TotalDays <= GraceDays)
                return revoked;

            List<string> expired = null;
            foreach (var kvp in _stored)
                if (kvp.Value == 'E')
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

            /// <summary>Права, ПОТЕРЯННЫЕ этой сверкой (переход имевшегося доступа в отсутствие).</summary>
            public readonly List<string> Revoked = new();
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

            foreach (var sku in configuredLongLived)
            {
                bool active = result.ActiveSkus.Contains(sku);
                bool hadAccess = GetEffectiveAccess(sku);

                _stored[sku] = active ? 'E' : 'N';

                if (active)
                    diff.Entitled.Add(sku);
                else if (hadAccess)
                    diff.Revoked.Add(sku);
            }

            _reconciledAtUtc = nowUtc;
            _reconciledThisSession = true;
            _dirty = true;
            SaveIfDirty();
            return diff;
        }

        /// <summary>
        /// Живая покупка — единственное легальное исключение из «только снапшот»: свежий
        /// SUCCESSFUL-чек может ДОБАВИТЬ право, но никогда не снять. Полная сверка,
        /// запускаемая следом, подтвердит его штатно.
        /// </summary>
        public void ApplyLivePurchaseGrant(string sku)
        {
            if (string.IsNullOrEmpty(sku))
                return;
            if (_stored.TryGetValue(sku, out var c) && c == 'E')
                return;
            _stored[sku] = 'E';
            _dirty = true;
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

            if (_reconciledAtUtc != DateTime.MinValue)
                PlayerPrefs.SetString(IapPrefsKeys.EntitlementReconciledAt, _reconciledAtUtc.ToString("o"));

            PlayerPrefs.Save();
        }
    }
}
#endif
