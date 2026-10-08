using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Склейка баннерных показов в одно событие с полем <c>n</c> (SDK 1.0.9).
    ///
    /// <para>Баннер кросс-промо засчитывает показ на каждой видимой ротации (раз в 8 секунд),
    /// MAX-баннер — на каждом колбэке выручки (каждый автоповтор). Раньше каждый такой показ был
    /// отдельным HTTP-запросом и отдельной записью в очереди; теперь показы копятся здесь по
    /// ключу и уходят одним <c>cp_impression</c> / <c>mediation_impression</c> с <c>n</c>
    /// не чаще раза в <see cref="BatchWindowMs"/> на ключ.</para>
    ///
    /// <para>Окно — 5 минут, а не минута: баннер крутит все креативы конфига (обычно 7–10 разных
    /// paid_app_id), и каждый ключ видит показ лишь раз в K×8 секунд. С минутным окном пачка
    /// закрывалась бы с n≈1–2, и запросов было бы почти столько же, сколько в 1.0.8. Пять минут дают
    /// около K/5 запросов в минуту (8 креативов — 1,6/мин против 7,5/мин в 1.0.8) без риска потерь:
    /// незакрытые пачки лежат на диске.</para>
    ///
    /// <para>Ключ кросс-промо — (device_id_hash, paid_app_id); donor_app_id у приложения один.
    /// Ключ MAX — (device_id_hash, network, ad_unit, revenue_precision, выручка известна или нет):
    /// известная выручка суммируется, а показы с неизвестной (MAX шлёт -1) копятся отдельно и уходят
    /// с <c>revenue:-1</c>, поэтому сумма всегда относится ко всем n показам пачки и полю
    /// <c>revenue_n</c> взяться неоткуда. Пачка никогда не пересекает границу суток UTC: ts пачки —
    /// время первого показа, и все n попадают в тот же день, что и раньше.</para>
    ///
    /// <para>Идентичность фиксируется в момент показа. Показ до резолва device_id попадает в
    /// непривязанную пачку (пустой <see cref="BannerImpressionBatch.DeviceIdHash"/>), которая
    /// привязывается к первому резолву этой же сессии (<see cref="BindIdentity"/>) и до этого не
    /// отправляется.</para>
    ///
    /// <para>Чистый C# без UnityEngine: время, идентичность и персистентность передаёт вызывающий
    /// (<c>AnalyticsModule</c>), поэтому логику можно проверить без движка.</para>
    /// </summary>
    internal sealed class BannerImpressionBatcher
    {
        public const string CrossPromoEventName = "cp_impression";
        public const string MediationEventName = "mediation_impression";

        /// <summary>Верхняя граница <c>n</c>, которую принимает бэкенд. Полная пачка закрывается сразу.</summary>
        public const int MaxImpressionsPerBatch = 1000;

        /// <summary>Не чаще одного события на ключ за это время (от первого показа пачки).</summary>
        public const long BatchWindowMs = 300000L;

        /// <summary>
        /// Предел числа незакрытых пачек в буфере. Обычно их не больше числа ключей (креативы + MAX),
        /// но пачки, которые некуда закрыть (очередь занята, бэкенд недоступен днями), копятся по
        /// дням. Сверх предела отбрасывается самая старая пачка: буфер сохраняется на каждом показе
        /// и не должен расти без границы.
        /// </summary>
        public const int MaxPendingBatches = 64;

        /// <summary>
        /// Порог правдоподобия выручки одного показа (10 USD = eCPM 10 000 USD) — тот же, что у
        /// бэкенда. Всё, что вне [0, 10], а также NaN и бесконечности считается неизвестной выручкой.
        /// </summary>
        public const double MaxRevenuePerImpressionUsd = 10d;

        // Выручка копится в целых пико-долларах: сумма точная и без потерь переживает
        // сохранение в PlayerPrefs (JsonUtility и long). 1000 показов по 10 USD = 1e16 < long.MaxValue.
        private const double PicoUsdPerUsd = 1e12;
        private const long MsPerDay = 86400000L;

        private readonly List<BannerImpressionBatch> _batches = new List<BannerImpressionBatch>();

        public int Count => _batches.Count;

        public bool IsEmpty => _batches.Count == 0;

        /// <summary>
        /// Показ баннера кросс-промо. <paramref name="deviceIdHash"/> null/"" — device_id ещё не резолвился.
        /// Возвращает число показов, потерянных из-за предела буфера (<see cref="MaxPendingBatches"/>); обычно 0.
        /// </summary>
        public int AddCrossPromo(string deviceIdHash, string paidAppId, long tsMs)
        {
            int lost = 0;
            var batch = FindOpen(CrossPromoEventName, deviceIdHash, paidAppId, null, null, null, false, tsMs);
            if (batch == null)
            {
                lost = MakeRoom();
                batch = new BannerImpressionBatch
                {
                    EventName = CrossPromoEventName,
                    DeviceIdHash = deviceIdHash ?? string.Empty,
                    PaidAppId = paidAppId ?? string.Empty,
                    Network = string.Empty,
                    AdUnit = string.Empty,
                    RevenuePrecision = string.Empty,
                    FirstTs = tsMs,
                };
                _batches.Add(batch);
            }

            AddImpression(batch, tsMs, 0L);
            return lost;
        }

        /// <summary>
        /// Показ MAX-баннера (колбэк выручки). Неизвестная выручка копится в отдельной пачке.
        /// Возвращает число показов, потерянных из-за предела буфера; обычно 0.
        /// </summary>
        public int AddMediation(string deviceIdHash, string network, string adUnit, string precision,
            double revenueUsd, long tsMs)
        {
            int lost = 0;
            bool known = IsKnownRevenue(revenueUsd);
            var batch = FindOpen(MediationEventName, deviceIdHash, null, network, adUnit, precision, known, tsMs);
            if (batch == null)
            {
                lost = MakeRoom();
                batch = new BannerImpressionBatch
                {
                    EventName = MediationEventName,
                    DeviceIdHash = deviceIdHash ?? string.Empty,
                    PaidAppId = string.Empty,
                    Network = network ?? string.Empty,
                    AdUnit = adUnit ?? string.Empty,
                    RevenuePrecision = precision ?? string.Empty,
                    RevenueKnown = known,
                    FirstTs = tsMs,
                };
                _batches.Add(batch);
            }

            AddImpression(batch, tsMs, known ? ToPicoUsd(revenueUsd) : 0L);
            return lost;
        }

        /// <summary>
        /// Привязывает пачки, набранные до резолва device_id, к резолвнутой идентичности; при
        /// совпадении ключа сливает их с уже привязанной пачкой. true — состояние изменилось.
        /// </summary>
        public bool BindIdentity(string deviceIdHash)
        {
            if (string.IsNullOrEmpty(deviceIdHash))
                return false;

            bool changed = false;
            for (int i = _batches.Count - 1; i >= 0; i--)
            {
                var unbound = _batches[i];
                if (!string.IsNullOrEmpty(unbound.DeviceIdHash))
                    continue;

                changed = true;
                var target = FindMergeTarget(unbound, deviceIdHash);
                if (target == null)
                {
                    unbound.DeviceIdHash = deviceIdHash;
                    continue;
                }

                target.N += unbound.N;
                target.RevenuePicoUsd += unbound.RevenuePicoUsd;
                if (unbound.FirstTs < target.FirstTs)
                    target.FirstTs = unbound.FirstTs;
                _batches.RemoveAt(i);
            }

            return changed;
        }

        /// <summary>
        /// Забирает пачки для отправки и удаляет их из буфера. Непривязанные пачки не отдаются
        /// никогда. <see cref="BannerBatchFlush.Required"/> — только те, что нельзя держать дальше
        /// (полные, из прошлых суток UTC, часы ушли назад); <see cref="BannerBatchFlush.Due"/> — плюс
        /// те, чьё окно истекло; <see cref="BannerBatchFlush.All"/> — все привязанные.
        /// </summary>
        public List<BannerImpressionBatch> TakeDue(long nowMs, BannerBatchFlush mode) =>
            TakeDue(nowMs, mode, int.MaxValue);

        /// <summary>
        /// То же, но не больше <paramref name="maxCount"/> пачек, самые старые (по первому показу)
        /// первыми. Остальные остаются в буфере: на диске они в безопасности, а в очереди лишнее
        /// баннерное событие вытеснило бы уже стоящее там (у баннерных событий 10 мест).
        /// </summary>
        public List<BannerImpressionBatch> TakeDue(long nowMs, BannerBatchFlush mode, int maxCount)
        {
            var taken = new List<BannerImpressionBatch>();
            if (maxCount <= 0)
                return taken;

            var candidates = new List<int>();
            for (int i = 0; i < _batches.Count; i++)
            {
                if (ShouldTake(_batches[i], nowMs, mode))
                    candidates.Add(i);
            }

            if (candidates.Count == 0)
                return taken;

            // Старые первыми; при равном ts — в порядке буфера (List.Sort нестабилен).
            candidates.Sort((a, b) =>
            {
                int byTs = _batches[a].FirstTs.CompareTo(_batches[b].FirstTs);
                return byTs != 0 ? byTs : a.CompareTo(b);
            });
            if (candidates.Count > maxCount)
                candidates.RemoveRange(maxCount, candidates.Count - maxCount);

            var takeIndex = new bool[_batches.Count];
            foreach (int i in candidates)
            {
                takeIndex[i] = true;
                taken.Add(_batches[i]);
            }

            int kept = 0;
            for (int i = 0; i < _batches.Count; i++)
            {
                if (!takeIndex[i])
                    _batches[kept++] = _batches[i];
            }

            _batches.RemoveRange(kept, _batches.Count - kept);
            return taken;
        }

        /// <summary>Сколько пачек забрал бы <see cref="TakeDue(long, BannerBatchFlush)"/>, ничего не меняя.</summary>
        public int CountDue(long nowMs, BannerBatchFlush mode)
        {
            int count = 0;
            foreach (var batch in _batches)
            {
                if (ShouldTake(batch, nowMs, mode))
                    count++;
            }

            return count;
        }

        /// <summary>Копия буфера для сохранения.</summary>
        public List<BannerImpressionBatch> Snapshot()
        {
            var copy = new List<BannerImpressionBatch>(_batches.Count);
            foreach (var batch in _batches)
                copy.Add(batch.Clone());
            return copy;
        }

        /// <summary>
        /// Восстанавливает буфер, сохранённый прошлым процессом. Пачка без идентичности привязывается к
        /// <paramref name="unboundIdentity"/>: идентичность новой сессии ей не принадлежит (тот же
        /// принцип, что у раннего клика: в очереди не переназначается). Битые записи отбрасываются,
        /// как и самые старые сверх <see cref="MaxPendingBatches"/>. Возвращает число отброшенных записей.
        /// </summary>
        public int Restore(List<BannerImpressionBatch> saved, string unboundIdentity, long minTsMs)
        {
            int dropped = 0;
            if (saved == null)
                return 0;

            foreach (var item in saved)
            {
                if (!IsRestorable(item, minTsMs))
                {
                    dropped++;
                    continue;
                }

                var batch = item.Clone();
                if (string.IsNullOrEmpty(batch.DeviceIdHash))
                    batch.DeviceIdHash = unboundIdentity ?? string.Empty;
                batch.PaidAppId = batch.PaidAppId ?? string.Empty;
                batch.Network = batch.Network ?? string.Empty;
                batch.AdUnit = batch.AdUnit ?? string.Empty;
                batch.RevenuePrecision = batch.RevenuePrecision ?? string.Empty;
                if (batch.N > MaxImpressionsPerBatch)
                    batch.N = MaxImpressionsPerBatch;
                if (!batch.RevenueKnown || batch.RevenuePicoUsd < 0)
                    batch.RevenuePicoUsd = 0;
                _batches.Add(batch);
            }

            while (_batches.Count > MaxPendingBatches)
            {
                _batches.RemoveAt(OldestIndex());
                dropped++;
            }

            return dropped;
        }

        /// <summary>Выручка, которую можно суммировать: конечная и в пределах [0, 10] USD.</summary>
        public static bool IsKnownRevenue(double revenueUsd) =>
            !double.IsNaN(revenueUsd) && !double.IsInfinity(revenueUsd)
            && revenueUsd >= 0d && revenueUsd <= MaxRevenuePerImpressionUsd;

        /// <summary>Сумма выручки пачки в USD для поля <c>revenue</c>.</summary>
        public static double ToUsd(long picoUsd) => picoUsd / PicoUsdPerUsd;

        private static long ToPicoUsd(double revenueUsd) => (long)Math.Round(revenueUsd * PicoUsdPerUsd);

        private static long UtcDay(long tsMs) => tsMs >= 0 ? tsMs / MsPerDay : (tsMs - MsPerDay + 1) / MsPerDay;

        /// <summary>
        /// Освобождает место под новую пачку, если буфер полон: отбрасывает самую старую (обычно
        /// пачку прошлых суток, которую так и не удалось закрыть). Возвращает её число показов.
        /// </summary>
        private int MakeRoom()
        {
            int lost = 0;
            while (_batches.Count >= MaxPendingBatches)
            {
                int oldest = OldestIndex();
                lost += _batches[oldest].N;
                _batches.RemoveAt(oldest);
            }

            return lost;
        }

        private int OldestIndex()
        {
            int oldest = 0;
            for (int i = 1; i < _batches.Count; i++)
            {
                if (_batches[i].FirstTs < _batches[oldest].FirstTs)
                    oldest = i;
            }

            return oldest;
        }

        private static void AddImpression(BannerImpressionBatch batch, long tsMs, long revenuePicoUsd)
        {
            batch.N++;
            batch.RevenuePicoUsd += revenuePicoUsd;
            if (tsMs < batch.FirstTs)
                batch.FirstTs = tsMs;
        }

        private static bool ShouldTake(BannerImpressionBatch batch, long nowMs, BannerBatchFlush mode)
        {
            if (string.IsNullOrEmpty(batch.DeviceIdHash))
                return false;

            switch (mode)
            {
                case BannerBatchFlush.All:
                    return true;
                case BannerBatchFlush.Due:
                    return MustClose(batch, nowMs) || nowMs - batch.FirstTs >= BatchWindowMs;
                default:
                    return MustClose(batch, nowMs);
            }
        }

        /// <summary>Пачку нельзя держать дальше: она полная, из прошлых суток UTC или часы ушли назад.</summary>
        private static bool MustClose(BannerImpressionBatch batch, long nowMs) =>
            batch.N >= MaxImpressionsPerBatch || nowMs < batch.FirstTs || UtcDay(nowMs) != UtcDay(batch.FirstTs);

        private BannerImpressionBatch FindOpen(string eventName, string deviceIdHash, string paidAppId,
            string network, string adUnit, string precision, bool revenueKnown, long tsMs)
        {
            string identity = deviceIdHash ?? string.Empty;
            foreach (var batch in _batches)
            {
                if (batch.N >= MaxImpressionsPerBatch || UtcDay(batch.FirstTs) != UtcDay(tsMs))
                    continue;
                if (batch.EventName != eventName || batch.DeviceIdHash != identity)
                    continue;
                if (SameKey(batch, paidAppId ?? string.Empty, network ?? string.Empty, adUnit ?? string.Empty,
                        precision ?? string.Empty, revenueKnown))
                    return batch;
            }

            return null;
        }

        private BannerImpressionBatch FindMergeTarget(BannerImpressionBatch unbound, string deviceIdHash)
        {
            foreach (var batch in _batches)
            {
                if (ReferenceEquals(batch, unbound) || batch.DeviceIdHash != deviceIdHash
                    || batch.EventName != unbound.EventName
                    || UtcDay(batch.FirstTs) != UtcDay(unbound.FirstTs)
                    || batch.N + unbound.N > MaxImpressionsPerBatch)
                    continue;
                if (SameKey(batch, unbound.PaidAppId, unbound.Network, unbound.AdUnit, unbound.RevenuePrecision,
                        unbound.RevenueKnown))
                    return batch;
            }

            return null;
        }

        private static bool SameKey(BannerImpressionBatch batch, string paidAppId, string network, string adUnit,
            string precision, bool revenueKnown)
        {
            if (batch.EventName == CrossPromoEventName)
                return batch.PaidAppId == paidAppId;

            return batch.Network == network && batch.AdUnit == adUnit
                && batch.RevenuePrecision == precision && batch.RevenueKnown == revenueKnown;
        }

        private static bool IsRestorable(BannerImpressionBatch item, long minTsMs)
        {
            if (item == null || item.N <= 0 || item.FirstTs < minTsMs)
                return false;
            if (item.EventName == CrossPromoEventName)
                return !string.IsNullOrEmpty(item.PaidAppId);
            return item.EventName == MediationEventName;
        }
    }

    /// <summary>Какие пачки забрать из буфера (см. <see cref="BannerImpressionBatcher.TakeDue"/>).</summary>
    internal enum BannerBatchFlush
    {
        /// <summary>Только пачки, которые нельзя держать дальше (полные, прошлые сутки UTC, часы назад).</summary>
        Required,

        /// <summary>Плюс пачки, у которых истекло окно <see cref="BannerImpressionBatcher.BatchWindowMs"/>.</summary>
        Due,

        /// <summary>Все привязанные пачки: выход из приложения.</summary>
        All
    }

    /// <summary>
    /// Пачка баннерных показов. Поля публичные — формат сохранения через JsonUtility
    /// (PlayerPrefs-ключ буфера в <c>AnalyticsModule</c>).
    /// </summary>
    [Serializable]
    internal sealed class BannerImpressionBatch
    {
        /// <summary><c>cp_impression</c> или <c>mediation_impression</c>.</summary>
        public string EventName;

        /// <summary>Пусто — показ до резолва device_id, пачка ещё не привязана.</summary>
        public string DeviceIdHash;

        public string PaidAppId;
        public string Network;
        public string AdUnit;
        public string RevenuePrecision;
        public bool RevenueKnown;

        /// <summary>Unix ms первого показа пачки — уходит как <c>ts</c> события.</summary>
        public long FirstTs;

        /// <summary>Число показов — уходит как <c>n</c>.</summary>
        public int N;

        /// <summary>Сумма известной выручки в 1e-12 USD; 0 при <see cref="RevenueKnown"/> = false.</summary>
        public long RevenuePicoUsd;

        public BannerImpressionBatch Clone() => (BannerImpressionBatch)MemberwiseClone();
    }
}
