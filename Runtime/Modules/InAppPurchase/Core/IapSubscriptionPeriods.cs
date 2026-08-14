#if AMZN_IAP_ENABLED
using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>
    /// Чистый калькулятор оплаченных периодов подписки (ТЗ IAP-14). Продления у Amazon
    /// НЕ наблюдаются: при продлении непрерывной подписки не меняется ни одно поле чека
    /// (ReceiptId один на всю подписку, purchaseDate — дата ПЕРВОЙ покупки, cancelDate
    /// пуст). Поэтому периоды считаются: якорь — PurchaseDate чека, длина — TermDays из
    /// настроек, «сейчас» — доверенное время (SdkTrustedTime, не часы устройства).
    /// Срок принимается в днях и может быть дробным: тестовый режим (TestTermMinutes)
    /// выражает «подписку на 10 минут» как 10/1440 дня — математика и так на double.
    ///
    /// Граница периода N наступает в момент PurchaseDate + N × TermDays; период N считается
    /// оплаченным, если его граница уже наступила и лежит СТРОГО раньше потолков:
    /// CancelDate (после отмены периоды не начисляются) и, защитно, DeferredDate (смена
    /// тарифа: после неё период считается от нового SKU; по данным портфеля ни одна из 750
    /// подписок тариф не меняла — страховка, а не рабочий сценарий).
    ///
    /// Класс без состояния и без Unity API — гоняется таблицей без редактора.
    /// </summary>
    internal static class IapSubscriptionPeriods
    {
        /// <summary>
        /// Индексы периодов к выдаче, по возрастанию. Индекс 0 — первый оплаченный период.
        ///
        /// Правило засева (ТЗ IAP-14 п.4): если журнал ещё не знает этот чек
        /// (journalHasEntry == false — чистая установка или первая покупка), выдаётся ТОЛЬКО
        /// текущий период, история не выдаётся: фарм через переустановку невыгоден, а
        /// оплаченный сейчас период игрок получить должен. Причём выдаётся, только если он
        /// действительно ТЕКУЩИЙ — «сейчас» лежит внутри него. У отменённого чека индекс
        /// упирается в потолок CancelDate и лежит целиком в прошлом: такой период при
        /// засеве не выдаётся — иначе каждая чистая установка выдавала бы по последнему
        /// оплаченному периоду за КАЖДЫЙ отменённый чек истории (лог 2026-08-14: два
        /// отменённых чека → «+2 периода» на старте, и это же — фарм переустановкой).
        /// Иначе — всё в (lastFiredIndex, current].
        /// </summary>
        public static List<int> PeriodsToFire(
            long purchaseDateMs,
            double termDays,
            long cancelDateMs,
            long deferredDateMs,
            DateTime trustedNowUtc,
            bool journalHasEntry,
            int lastFiredIndex)
        {
            var result = new List<int>();

            if (termDays <= 0 || purchaseDateMs <= 0)
                return result;

            int current = CurrentPeriodIndex(purchaseDateMs, termDays, cancelDateMs, deferredDateMs, trustedNowUtc);
            if (current < 0)
                return result;

            if (!journalHasEntry)
            {
                // «Сейчас» внутри периода current ⇔ индекс по одному только времени равен
                // индексу с потолками (CancelDate/DeferredDate его не срезали).
                int byTimeOnly = (int)((trustedNowUtc - FromUnixMs(purchaseDateMs)).TotalDays / termDays);
                if (current == byTimeOnly)
                    result.Add(current);
                return result;
            }

            for (int i = lastFiredIndex + 1; i <= current; i++)
                result.Add(i);

            return result;
        }

        /// <summary>Номер последнего наступившего оплаченного периода; -1, если ни одна граница не валидна.</summary>
        public static int CurrentPeriodIndex(
            long purchaseDateMs,
            double termDays,
            long cancelDateMs,
            long deferredDateMs,
            DateTime trustedNowUtc)
        {
            var purchaseUtc = FromUnixMs(purchaseDateMs);
            if (trustedNowUtc < purchaseUtc)
                return -1;   // доверенное время раньше якоря — не начисляем и журнал не трогаем

            int index = (int)((trustedNowUtc - purchaseUtc).TotalDays / termDays);
            index = Math.Min(index, LastIndexBeforeCeiling(purchaseUtc, termDays, cancelDateMs));
            index = Math.Min(index, LastIndexBeforeCeiling(purchaseUtc, termDays, deferredDateMs));
            return index;
        }

        public static DateTime PeriodStartUtc(long purchaseDateMs, double termDays, int index) =>
            FromUnixMs(purchaseDateMs).AddDays(termDays * index);

        /// <summary>
        /// Последний индекс, чья граница наступает СТРОГО раньше потолка. Граница ровно в
        /// момент потолка уже не оплачена: отменённая в точный конец периода подписка не
        /// получает следующий.
        /// </summary>
        private static int LastIndexBeforeCeiling(DateTime purchaseUtc, double termDays, long ceilingMs)
        {
            if (ceilingMs <= 0)
                return int.MaxValue;   // потолка нет

            var ceilingUtc = FromUnixMs(ceilingMs);
            if (ceilingUtc <= purchaseUtc)
                return -1;

            double days = (ceilingUtc - purchaseUtc).TotalDays;
            return (int)Math.Ceiling(days / termDays) - 1;
        }

        private static DateTime FromUnixMs(long unixMs) =>
            DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
    }
}
#endif
