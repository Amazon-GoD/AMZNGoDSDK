#if AMZN_ANALYTICS_ENABLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AMZNGoDSDK.Runtime
{
    // Единственный владелец HTTP-канала /v1/events: first_open (paid/free) на инициализации,
    // cp_impression / cp_click из CrossPromoModule через делегаты в AmznGoDSDKCore,
    // mediation_impression / mediation_click из AppLovinAnalytics (показы медиации: у них нет
    // paid_app_id, поэтому схема своя),
    // iap_link из InAppPurchaseModule (связка device ↔ Amazon-покупатель, по завершении
    // полной сверки GetPurchaseUpdates) и attribution (источник трафика из Adjust).
    // Идемпотентность first_open и кэш device_id_hash через PlayerPrefs с историческими
    // ключами cp_* — это намеренно: апгрейд не должен повторять first_open.
    // Сохранённый device_id используется только после проверки текущего Fire ID.
    // С 1.0.9 баннерные показы (CP-баннер и MAX-баннер) не уходят по одному: они копятся в
    // BannerImpressionBatcher и отправляются одним событием с полем "n" не чаще раза в 5 минут на
    // ключ; до окна пачка закрывается только на смене суток UTC, при n = 1000 и на выходе.
    // Каждое событие несёт sdk_version и app_version.
    public class AnalyticsModule : ModuleBase
    {
        private const long MinTimestampMs = 1577836800000L;
        private const int DeviceIdMaxAttempts = 10;
        private const float DeviceIdRetryInterval = 2f;
        private const string Tag = "[Analytics]";

        // Потолок HTTP-запроса. Без него зависший сокет висит вечно и держит корутину.
        private const int HttpTimeoutSeconds = 15;

        // Сколько ждём резолва device_id перед отправкой события, попавшего в первые
        // мгновения после старта (до того как пришёл колбэк Fire ID). Обычно резолв
        // происходит за десятки миллисекунд, поэтому клик по CTA этим не тормозится.
        private const float DeviceIdWaitForEventSeconds = 6f;

        // Значение device_id_hash для устройств без Fire ID. Пустая строка на бэкенде
        // неотличима от потерянного поля, поэтому отсутствие идентификатора кодируется явно.
        private const string UnattributedDeviceId = "unattributed";

        // Идемпотентность iap_link: SHA-256 от device_id + amazon_user_id + отсортированного списка
        // receipt_ids последней ДОСТАВЛЕННОЙ связки. Пишется только по HTTP 2xx — тот же
        // принцип, что у флага first_open: Rejected/транзиент не должны хоронить связку.
        private const string IapLinkHashKey = "amzn_iap_link_hash";

        // Идемпотентность attribution: последняя ДОСТАВЛЕННАЯ пара Fire ID / TrackerToken.
        // Сброс Fire ID или реатрибуция требуют новой связки.
        private const string AttributionTokenKey = "amzn_attribution_tracker_token";

        // Атрибуция Adjust резолвится асинхронно, иногда спустя десятки секунд после
        // установки. Не успела за эти попытки — доберём на OnApplicationFocus и на
        // следующем запуске (Adjust кэширует атрибуцию нативно).
        private const int AttributionMaxAttempts = 15;
        private const float AttributionRetryIntervalSeconds = 2f;

        // Баннерные показы склеиваются (см. BannerImpressionBatcher). Буфер незакрытых пачек
        // лежит на диске так же, как очередь: показы переживают паузу, убийство процесса и
        // падение. Проверка окна — по таймеру, потому что скрытый баннер новых показов не даёт.
        private const string BannerPlacement = "banner";
        private const string BannerBatchesKey = "amzn_banner_impression_batches";
        private const float BannerBatchCheckIntervalSeconds = 5f;

        // Неизвестная выручка показа в протоколе — -1, как её отдаёт MAX.
        private const double UnknownRevenue = -1d;

        private string _baseUrl;
        private string _apiKey;
        private string _appType;
        private string _defaultPromotedAppId;
        private string _deviceIdHash;
        private bool _deviceIdResolved;
        private bool _flushing;
        private bool _firstOpenInFlight;
        private bool _attributionRoutineRunning;
        private bool _identityRefreshRunning;
        private bool _runtimeStarted;
        private bool _resumeAfterActivation;
        private readonly HashSet<string> _preparingEventBodies = new HashSet<string>(StringComparer.Ordinal);

        // Незакрытые пачки баннерных показов. Не транзиентное состояние: ResetTransientState их
        // не трогает, а с диска они читаются один раз за процесс.
        private readonly BannerImpressionBatcher _bannerBatches = new BannerImpressionBatcher();
        private bool _bannerBatchesLoaded;
        private bool _bannerBudgetFullLogged;

        // Связка, отправка которой уже в полёте: сверка IAP может завершиться несколько
        // раз подряд (init + после покупки), и без этой защёлки одна и та же связка ушла
        // бы параллельно дважды до того, как первая доставка запишет хеш в PlayerPrefs.
        private string _iapLinkInFlightHash;

#if AMZN_ADJUST_ENABLED
        private AdjustSdk.AdjustAttribution _pendingAttribution;
#endif

        private static string AppId => Application.identifier;

        private static string AppVersion => Application.version;

        // Отсутствующий Fire ID — легальное состояние: фолбэков на adid / android_id больше нет.
        // Готовность определяется фактом резолва, а не непустым хэшем, иначе такие устройства
        // молча перестали бы слать события; в payload вместо хэша уходит UnattributedDeviceId.
        public bool IsReady => Initialized && _deviceIdResolved;

        private string EventDeviceIdHash =>
            string.IsNullOrEmpty(_deviceIdHash) ? UnattributedDeviceId : _deviceIdHash;

        // Идентичность для баннерной пачки: null, пока device_id не резолвился, — такая пачка
        // привяжется к первому резолву (BannerImpressionBatcher.BindIdentity) и до него не уйдёт.
        private string BannerBatchIdentity => _deviceIdResolved ? EventDeviceIdHash : null;

        public void Construct(bool enable, string baseUrl, string apiKey, AnalyticsAppType appType, string defaultPromotedAppId)
        {
            Enabled = enable;
            _baseUrl = baseUrl?.TrimEnd('/');
            _apiKey = apiKey;
            _appType = appType == AnalyticsAppType.Paid ? "paid" : "free";
            _defaultPromotedAppId = defaultPromotedAppId;

            // В билд None попасть не может — его отсекает AnalyticsAppTypeBuildGuard. Остаётся
            // Play mode после старта редактора, когда тип ещё сброшен: слать события с
            // додуманным free/paid нельзя, они навсегда испортят разбивку на бэкенде.
            if (Enabled && appType == AnalyticsAppType.None)
            {
                Enabled = false;
                Debug.LogError($"{Tag} App Type не выбран — аналитика отключена для этого запуска. " +
                               "Выставь App Type в AMZN GoD/SDK Settings → Analytics.");
                return;
            }

            if (Enabled && string.IsNullOrWhiteSpace(_baseUrl))
                Debug.LogWarning($"{Tag} Construct: baseUrl is empty — events will not be sent");
            if (Enabled && string.IsNullOrWhiteSpace(_apiKey))
                Debug.LogWarning($"{Tag} Construct: apiKey is empty — events will not be sent");

            Debug.Log($"{Tag} Constructed — enabled={Enabled}, baseUrl={_baseUrl}, appId={AppId}, appType={_appType}, defaultPromotedAppId={_defaultPromotedAppId}");
        }

        public override void Initialize()
        {
            StopAllCoroutines();
            ResetTransientState();
            _runtimeStarted = true;
            _resumeAfterActivation = false;
            DeviceIdProvider.BeginSession();
            EnsureBannerBatchesLoaded();
            StartCoroutine(InitializeRoutine());
            StartCoroutine(BannerBatchRoutine());
        }

        public override void Cleanup()
        {
            _runtimeStarted = false;
            _resumeAfterActivation = false;
            StopAllCoroutines();
            ResetTransientState();
        }

        private void OnDisable()
        {
            // Component.enabled=false leaves coroutines running; SetActive(false)
            // stops them. Do not depend on iterator finally blocks for stopped work.
            if (gameObject.activeInHierarchy || !_runtimeStarted)
                return;
            StopAllCoroutines();
            ResetTransientState();
            _resumeAfterActivation = true;
        }

        private void OnEnable()
        {
            if (_resumeAfterActivation && Enabled)
                Initialize();
        }

        private void ResetTransientState()
        {
            // Disk-backed events survive; transient preparation/in-flight markers do not.
            _preparingEventBodies.Clear();
            _flushing = false;
            _firstOpenInFlight = false;
            _attributionRoutineRunning = false;
            _identityRefreshRunning = false;
            _iapLinkInFlightHash = null;
            _deviceIdHash = null;
            _deviceIdResolved = false;
        }

        private IEnumerator InitializeRoutine()
        {
            yield return ResolveDeviceId();

            if (string.IsNullOrEmpty(_deviceIdHash))
                Debug.LogWarning($"{Tag} Fire ID unavailable — events will be sent with device_id_hash='{UnattributedDeviceId}'");
            else
                Debug.Log($"{Tag} Device ID resolved: {_deviceIdHash}");

            _firstOpenInFlight = true;
            yield return TrySendFirstOpen();
            _firstOpenInFlight = false;

            // Отдельной корутиной: ожидание атрибуции Adjust (до десятков секунд) не должно
            // задерживать флаш очереди. first_open сознательно НЕ ждёт атрибуции — он уходит
            // рано и надёжно, источник трафика доезжает отдельным событием attribution.
            StartCoroutine(ResolveAndSendAttribution());

            yield return FlushQueue();
        }

        private IEnumerator ResolveDeviceId()
        {
            for (int i = 0; i < DeviceIdMaxAttempts; i++)
            {
                Debug.Log($"{Tag} Resolving device ID, attempt {i + 1}/{DeviceIdMaxAttempts}...");

                // null — колбэк Adjust ещё не пришёл, ждём и пробуем снова.
                // "" — Fire ID подтверждённо недоступен, это финальный ответ: шлём пустой идентификатор.
                _deviceIdHash = DeviceIdProvider.TryResolveAndCache();
                if (_deviceIdHash != null)
                {
                    _deviceIdResolved = true;
                    yield break;
                }

                // WaitForSecondsRealtime, а не WaitForSeconds: игра ставит timeScale=0 на
                // интерстишеле, и обычный WaitForSeconds там встал бы навсегда — device_id
                // не резолвился бы НИКОГДА, и все события молча выбрасывались.
                yield return new WaitForSecondsRealtime(DeviceIdRetryInterval);
            }

            // Колбэк так и не пришёл за отведённые попытки — считаем Fire ID недоступным,
            // но события всё равно отправляем, с идентификатором UnattributedDeviceId.
            Debug.LogWarning($"{Tag} No Fire ID after {DeviceIdMaxAttempts} attempts — falling back to device_id_hash='{UnattributedDeviceId}'");
            _deviceIdHash = string.Empty;
            _deviceIdResolved = true;
        }

        private IEnumerator TrySendFirstOpen()
        {
            string eventName = _appType == "free" ? "free_first_open" : "paid_first_open";
            string prefsKey = _appType == "free" ? "cp_free_first_open_sent" : "cp_paid_first_open_sent";

            if (PlayerPrefs.HasKey(prefsKey))
            {
                Debug.Log($"{Tag} {eventName} already sent (PlayerPrefs key '{prefsKey}' exists), skipping");
                yield break;
            }

            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
            {
                Debug.LogWarning($"{Tag} System clock appears incorrect (ts={ts}), skipping {eventName}");
                yield break;
            }

            string deviceIdHash = EventDeviceIdHash;
            string eventId = NewEventId();
            string json = BuildFirstOpenJson(eventName, AppId, _appType, deviceIdHash, ts, eventId);
            Debug.Log($"{Tag} Sending {eventName}: app_id={AppId}, app_type={_appType}, device_id_hash={deviceIdHash}, ts={ts}, event_id={eventId}");

            var outcome = SendOutcome.Retry;
            yield return SendEvent(json, o => outcome = o);

            switch (outcome)
            {
                case SendOutcome.Delivered:
                    // Флаг ставится ТОЛЬКО на подтверждённую доставку (HTTP 2xx). Раньше сюда
                    // попадал и любой 4xx — при ротации ключа бэкенд отдавал 401, событие
                    // считалось доставленным, и устройство навсегда выпадало из воронки.
                    PlayerPrefs.SetInt(prefsKey, 1);
                    PlayerPrefs.Save();
                    Debug.Log($"{Tag} {eventName} confirmed, saved PlayerPrefs key '{prefsKey}'");
                    break;

                case SendOutcome.Rejected:
                    // Бэкенд отверг событие по существу (400/404/422) — повтор не поможет.
                    // Флаг не ставим: если это окажется дефектом бэкенда, следующий запуск
                    // попробует снова, а не похоронит first_open навсегда.
                    Debug.LogError($"{Tag} {eventName} rejected by backend — not retrying this session, flag NOT set");
                    break;

                default:
                    // Транзиентный сбой. В общую очередь first_open НЕ кладём: он останется там
                    // и уйдёт при флаше, а следующий запуск (флаг-то не выставлен) отправит его
                    // ещё раз — на бэкенде получится дубль. Вместо этого просто пробуем снова
                    // при возврате фокуса и на следующем запуске.
                    Debug.LogWarning($"{Tag} {eventName} failed to send (transient) — will retry on focus / next launch");
                    break;
            }
        }

        public void TrackImpression(string paidAppId) => TrackImpression(paidAppId, null);

        /// <summary>
        /// Показ кросс-промо. Полноэкранный (и без плейсмента) уходит отдельным событием, как
        /// раньше; показ баннера (<c>placement = "banner"</c>) с 1.0.9 копится в пачке и уходит
        /// одним <c>cp_impression</c> с <c>n</c> (см. <see cref="BannerImpressionBatcher"/>).
        /// </summary>
        public void TrackImpression(string paidAppId, string placement)
        {
            // ts фиксируем СРАЗУ, в момент показа: если device_id ещё резолвится, отправка
            // подождёт его пару кадров, но время события останется настоящим, а не поздним.
            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                return;

            if (IsBannerPlacement(placement))
            {
                AddBannerImpression(paidAppId, ts);
                return;
            }

            StartCoroutine(TrackCrossPromoEvent("cp_impression", paidAppId, ts, placement));
        }

        public void TrackClick(string paidAppId) => TrackClick(paidAppId, null);

        public void TrackClick(string paidAppId, string placement)
        {
            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                return;
            StartCoroutine(TrackCrossPromoEvent("cp_click", paidAppId, ts, placement));
        }

        /// <summary>
        /// Awaitable-версия отправки клика — её ждёт оверлей перед открытием стора, чтобы
        /// запрос клика успел уйти до сворачивания приложения.
        /// </summary>
        public IEnumerator TrackClickRoutine(string paidAppId) => TrackClickRoutine(paidAppId, null);

        public IEnumerator TrackClickRoutine(string paidAppId, string placement)
        {
            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                yield break;
            yield return TrackCrossPromoEvent("cp_click", paidAppId, ts, placement);
        }

        /// <summary>
        /// Показ рекламы из медиации AppLovin (событие <c>mediation_impression</c>).
        ///
        /// <para>Отдельный тип события, а не <c>cp_impression</c>: у показа медиации нет
        /// промотируемого приложения, а <c>cp_impression</c> требует <c>paid_app_id</c> и без него
        /// вообще не отправляется. Подстановка <c>DefaultPromotedAppId</c> записала бы в отчёты
        /// кросс-промо показы приложения, которого никто не показывал.</para>
        ///
        /// <para>Зовётся из обработчика <c>OnAdRevenuePaidEvent</c>: MAX присылает его ровно один
        /// раз на показ, и это единственное событие, где есть выручка. Отправка по
        /// <c>OnAdDisplayedEvent</c> дала бы показ без денег и второй запрос ради revenue.</para>
        ///
        /// <para>Interstitial и rewarded — по событию на показ, как раньше. Показы MAX-баннера
        /// (<c>placement = "banner"</c>, каждый автоповтор) с 1.0.9 копятся в пачке: одно событие с
        /// <c>n</c> и суммой выручки этих n показов (см. <see cref="BannerImpressionBatcher"/>).</para>
        /// </summary>
        public void TrackMediationImpression(string network, string adUnit, string placement, double revenue, string precision)
        {
            // ts фиксируем сразу, как в TrackImpression: ожидание резолва device_id не должно
            // сдвигать время события.
            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                return;

            if (IsBannerPlacement(placement))
            {
                AddBannerMediationImpression(network, adUnit, revenue, precision, ts);
                return;
            }

            StartCoroutine(TrackMediationEvent("mediation_impression", network, adUnit, placement, revenue, precision, ts));
        }

        /// <summary>
        /// Клик по рекламе из медиации (событие <c>mediation_click</c>). Та же причина отдельного
        /// типа, что и у показа: <c>cp_click</c> завязан на <c>paid_app_id</c>.
        /// <para>
        /// Выручки у клика нет — шлём 0 и пустой precision, чтобы схема события совпадала с
        /// показом и бэкенду не приходилось разбирать два формата.
        /// </para>
        /// </summary>
        public void TrackMediationClick(string network, string adUnit, string placement)
        {
            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                return;

            StartCoroutine(TrackMediationEvent("mediation_click", network, adUnit, placement, 0d, null, ts));
        }

        private IEnumerator TrackMediationEvent(
            string eventName, string network, string adUnit, string placement, double revenue, string precision, long ts)
        {
            // Как и в кросс-промо: не теряем событие из первых секунд сессии, ждём резолва
            // device_id ограниченное время вместо раннего выхода.
            yield return WaitUntilDeviceIdResolved();

            string deviceIdHash = EventDeviceIdHash;
            string eventId = NewEventId();

            string revenueText = revenue.ToString("R", CultureInfo.InvariantCulture);
            Debug.Log($"{Tag} >>> {eventName}: network={network}, ad_unit={adUnit}, placement={placement}, " +
                      $"revenue={revenueText}, app_id={AppId}, " +
                      $"device_id_hash={deviceIdHash}, ts={ts}, event_id={eventId}");

            string json = BuildMediationEventJson(
                eventName, network, adUnit, placement, revenue, precision, AppId, deviceIdHash, ts, eventId);

            yield return SendEventWithRetry(json, eventId);
        }

        private IEnumerator TrackCrossPromoEvent(string eventName, string paidAppId, long ts, string placement)
        {
            string resolvedPaidAppId = !string.IsNullOrEmpty(paidAppId) ? paidAppId : _defaultPromotedAppId;
            if (string.IsNullOrEmpty(resolvedPaidAppId))
            {
                Debug.LogWarning($"{Tag} {eventName} skipped — no paid_app_id (param={paidAppId}, default={_defaultPromotedAppId})");
                yield break;
            }

            string eventId = NewEventId();
            string pendingJson = null;
            if (eventName == "cp_click")
            {
                // Store navigation may kill the process before the Fire ID arrives.
                // Persist a complete event before the first wait; a restarted process
                // can deliver this same event_id with the original identity snapshot.
                pendingJson = BuildCrossPromoEventJson(eventName, resolvedPaidAppId, AppId,
                    EventDeviceIdHash, ts, eventId, placement);
                if (!AnalyticsEventQueue.TryEnqueue(pendingJson))
                    yield break;
                _preparingEventBodies.Add(pendingJson);
            }

            string json;
            string deviceIdHash;
            try
            {
                yield return WaitUntilDeviceIdResolved();
                deviceIdHash = EventDeviceIdHash;
                json = BuildCrossPromoEventJson(eventName, resolvedPaidAppId, AppId,
                    deviceIdHash, ts, eventId, placement);
                if (pendingJson != null && !AnalyticsEventQueue.ReplaceExact(pendingJson, json))
                    yield break; // An evicted/completed event must not be reinserted.
            }
            finally
            {
                if (pendingJson != null)
                    _preparingEventBodies.Remove(pendingJson);
            }

            Debug.Log($"{Tag} >>> {eventName}: paid_app_id={resolvedPaidAppId}, donor_app_id={AppId}, placement={placement}, device_id_hash={deviceIdHash}, ts={ts}, event_id={eventId}");
            yield return SendEventWithRetry(json, eventId, enqueue: pendingJson == null);
        }

        private static bool IsBannerPlacement(string placement) =>
            placement != null && string.Equals(placement.Trim(), BannerPlacement, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Видимый показ CP-баннера (решение «засчитать» принимает CrossPromoBanner, здесь оно не
        /// меняется). Показ только добавляется в пачку; отправляет её BannerBatchRoutine или пауза.
        /// </summary>
        private void AddBannerImpression(string paidAppId, long ts)
        {
            // Выключенная аналитика ничего не копит: таймер пачек живёт только после Initialize.
            if (!Enabled)
                return;

            string resolvedPaidAppId = !string.IsNullOrEmpty(paidAppId) ? paidAppId : _defaultPromotedAppId;
            if (string.IsNullOrEmpty(resolvedPaidAppId))
            {
                Debug.LogWarning($"{Tag} cp_impression skipped — no paid_app_id (param={paidAppId}, default={_defaultPromotedAppId})");
                return;
            }

            EnsureBannerBatchesLoaded();
            WarnIfBannerBufferFull(_bannerBatches.AddCrossPromo(BannerBatchIdentity, resolvedPaidAppId, ts));
            SaveBannerBatches();
        }

        /// <summary>Показ MAX-баннера (колбэк выручки): в пачку вместе с выручкой.</summary>
        private void AddBannerMediationImpression(string network, string adUnit, double revenue, string precision, long ts)
        {
            if (!Enabled)
                return;

            EnsureBannerBatchesLoaded();
            WarnIfBannerBufferFull(_bannerBatches.AddMediation(BannerBatchIdentity, network, adUnit, precision, revenue, ts));
            SaveBannerBatches();
        }

        private static void WarnIfBannerBufferFull(int lostImpressions)
        {
            if (lostImpressions > 0)
                Debug.LogWarning($"{Tag} Pending banner batch buffer full ({BannerImpressionBatcher.MaxPendingBatches} batches), " +
                                 $"dropped the oldest batch ({lostImpressions} impression(s))");
        }

        /// <summary>
        /// Закрывает пачки по окну, в том числе когда баннер уже скрыт и новых показов нет.
        /// WaitForSecondsRealtime — timeScale=0 на интерстишеле не должен останавливать отправку.
        /// </summary>
        private IEnumerator BannerBatchRoutine()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(BannerBatchCheckIntervalSeconds);

                try
                {
                    FlushBannerBatches(BannerBatchFlush.Due, sendNow: true);
                }
                catch (Exception e)
                {
                    // Исключение не должно навсегда остановить таймер: пачки останутся в буфере.
                    Debug.LogWarning($"{Tag} Banner batch flush failed: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Забирает из буфера пачки (<paramref name="mode"/>), строит по событию на пачку и ставит
        /// их в очередь; при <paramref name="sendNow"/> сразу отправляет, как любое свежее событие.
        ///
        /// <para>Закрывает не больше пачек, чем есть свободных баннерных мест в очереди (10 минус
        /// уже стоящие там баннерные события), самые старые первыми; остальные ждут в буфере на
        /// диске. Иначе очередь молча вытеснила бы стоящие в ней пачки — до 1000 показов каждая.
        /// Пока в очереди есть недоставленное баннерное событие (в полёте или после транзиентной
        /// ошибки), пачки по окну не закрываются: показы копятся дальше, как без сети.</para>
        /// </summary>
        private void FlushBannerBatches(BannerBatchFlush mode, bool sendNow)
        {
            if (!Enabled)
                return;

            EnsureBannerBatchesLoaded();
            if (_bannerBatches.IsEmpty)
                return;

            bool changed = _deviceIdResolved && _bannerBatches.BindIdentity(EventDeviceIdHash);

            // Без сети по окну не закрываем: продолжаем копить в той же пачке.
            if (mode == BannerBatchFlush.Due && !IsInternetAvailable())
                mode = BannerBatchFlush.Required;

            long now = GetTimestampMs();
            List<BannerImpressionBatch> batches = null;
            if (_bannerBatches.CountDue(now, mode) > 0)
            {
                AnalyticsEventQueue.GetBannerBudget(out int queuedBanner, out int freeSlots);

                // Недоставленное баннерное событие в очереди: бэкенд не отвечает или ответ ещё в
                // пути. Закрывать по окну новые пачки незачем — копим в тех же.
                if (mode == BannerBatchFlush.Due && queuedBanner > 0)
                    mode = BannerBatchFlush.Required;

                if (freeSlots > 0)
                {
                    batches = _bannerBatches.TakeDue(now, mode, freeSlots);
                    _bannerBudgetFullLogged = false;
                }
                else if (!_bannerBudgetFullLogged && _bannerBatches.CountDue(now, mode) > 0)
                {
                    _bannerBudgetFullLogged = true;
                    Debug.Log($"{Tag} Banner queue budget full ({queuedBanner} undelivered), {_bannerBatches.Count} batch(es) wait in the pending store");
                }
            }

            if (batches == null || batches.Count == 0)
            {
                if (changed)
                    SaveBannerBatches();
                return;
            }

            var bodies = new List<string>(batches.Count);
            var eventIds = new List<string>(batches.Count);
            foreach (var batch in batches)
            {
                // Новый event_id на каждую пачку: повтор этой же пачки снимается дедупом бэкенда.
                string eventId = NewEventId();
                bodies.Add(BuildBannerBatchJson(batch, eventId));
                eventIds.Add(eventId);
                Debug.Log($"{Tag} >>> {batch.EventName} banner batch: n={batch.N}, " +
                          (batch.EventName == BannerImpressionBatcher.CrossPromoEventName
                              ? $"paid_app_id={batch.PaidAppId}, "
                              : $"network={batch.Network}, ad_unit={batch.AdUnit}, revenue_known={batch.RevenueKnown}, ") +
                          $"device_id_hash={batch.DeviceIdHash}, ts={batch.FirstTs}, event_id={eventId}");
            }

            // Убрать пачки из буфера и поставить события в очередь — одной записью на диск:
            // TryEnqueueAll сохраняет PlayerPrefs целиком, и падение между двумя шагами не
            // потеряет и не удвоит показы.
            SaveBannerBatches(save: false);
            int queued = AnalyticsEventQueue.TryEnqueueAll(bodies, out int evicted);
            if (queued < bodies.Count)
                Debug.LogWarning($"{Tag} Event queue full of regular events, {bodies.Count - queued} banner batch event(s) not queued");
            if (evicted > 0)
                Debug.LogWarning($"{Tag} Queuing banner batches evicted {evicted} queued event(s)");

            if (!sendNow || !IsInternetAvailable())
                return;

            for (int i = 0; i < bodies.Count; i++)
                StartCoroutine(SendEventWithRetry(bodies[i], eventIds[i], enqueue: false));
        }

        private void EnsureBannerBatchesLoaded()
        {
            if (_bannerBatchesLoaded)
                return;
            _bannerBatchesLoaded = true;

            string raw = PlayerPrefs.GetString(BannerBatchesKey, "");
            if (string.IsNullOrEmpty(raw))
                return;

            try
            {
                var state = JsonUtility.FromJson<BannerBatchState>(raw);
                // Пачка прошлого процесса без идентичности не получает идентичность новой сессии —
                // как ранний клик в очереди: уходит с 'unattributed'.
                int dropped = _bannerBatches.Restore(state?.Items, UnattributedDeviceId, MinTimestampMs);
                Debug.Log($"{Tag} Restored {_bannerBatches.Count} pending banner batch(es), dropped {dropped} (invalid or over the cap)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} Pending banner batches unreadable, dropped: {e.Message}");
                PlayerPrefs.DeleteKey(BannerBatchesKey);
                PlayerPrefs.Save();
            }
        }

        private void SaveBannerBatches(bool save = true)
        {
            if (_bannerBatches.IsEmpty)
                PlayerPrefs.DeleteKey(BannerBatchesKey);
            else
                PlayerPrefs.SetString(BannerBatchesKey,
                    JsonUtility.ToJson(new BannerBatchState { Items = _bannerBatches.Snapshot() }));

            if (save)
                PlayerPrefs.Save();
        }

        private static string BuildBannerBatchJson(BannerImpressionBatch batch, string eventId)
        {
            if (batch.EventName == BannerImpressionBatcher.MediationEventName)
            {
                // revenue — сумма за все n показов. Показы с неизвестной выручкой собраны в
                // отдельную пачку и уходят с -1, как одиночный показ MAX без выручки.
                double revenue = batch.RevenueKnown
                    ? BannerImpressionBatcher.ToUsd(batch.RevenuePicoUsd)
                    : UnknownRevenue;
                return BuildMediationEventJson(batch.EventName, batch.Network, batch.AdUnit, BannerPlacement,
                    revenue, batch.RevenuePrecision, AppId, batch.DeviceIdHash, batch.FirstTs, eventId, batch.N);
            }

            return BuildCrossPromoEventJson(batch.EventName, batch.PaidAppId, AppId, batch.DeviceIdHash,
                batch.FirstTs, eventId, BannerPlacement, batch.N);
        }

        /// <summary>
        /// Связка device ↔ Amazon-покупатель (ТЗ «связка покупок и источника трафика», задача 1).
        /// Вызывается из InAppPurchaseModule через AmznGoDSDKCore по завершении полной сверки
        /// GetPurchaseUpdates — receiptIds уже содержит чеки ВСЕХ страниц. Фоновая, ничего не
        /// блокирует; дедуп по хешу связки, отправка заново только при появлении нового чека.
        /// </summary>
        public void TrackIapLink(string amazonUserId, IReadOnlyList<string> receiptIds)
        {
            if (!Enabled)
                return;

            if (string.IsNullOrEmpty(amazonUserId))
            {
                Debug.LogWarning($"{Tag} iap_link skipped — amazon_user_id is empty");
                return;
            }

            // ts фиксируем сразу, как в TrackImpression: ожидание резолва device_id не
            // должно сдвигать время события.
            long ts = GetTimestampMs();
            if (ts < MinTimestampMs)
                return;

            // Копия + сортировка: канонический порядок нужен хешу идемпотентности, чтобы
            // одинаковый набор чеков в другом порядке страниц не считался новой связкой.
            var ids = new List<string>(receiptIds?.Count ?? 0);
            if (receiptIds != null)
                foreach (var id in receiptIds)
                    if (!string.IsNullOrEmpty(id))
                        ids.Add(id);
            ids.Sort(StringComparer.Ordinal);

            StartCoroutine(TrackIapLinkRoutine(amazonUserId, ids, ts));
        }

        private IEnumerator TrackIapLinkRoutine(string amazonUserId, List<string> receiptIds, long ts)
        {
            yield return WaitUntilDeviceIdResolved();

            // В отличие от cp-событий, с сентинелом НЕ отправляем: 'unattributed' общий для
            // многих устройств, такая связка бесполезна и вредна. Хеш не пишем — сверка
            // перезапустится (форграунд/следующий старт) и попробует снова.
            if (!_deviceIdResolved || string.IsNullOrEmpty(_deviceIdHash))
            {
                Debug.LogWarning($"{Tag} iap_link skipped — no Fire ID (would be '{UnattributedDeviceId}'), will retry on next reconcile");
                yield break;
            }

            string deviceIdHash = _deviceIdHash;
            string linkHash = ComputeIapLinkHash(deviceIdHash, amazonUserId, receiptIds);

            if (PlayerPrefs.GetString(IapLinkHashKey, "") == linkHash)
            {
                Debug.Log($"{Tag} iap_link already delivered (hash unchanged), skipping");
                yield break;
            }

            if (_iapLinkInFlightHash == linkHash)
                yield break;
            _iapLinkInFlightHash = linkHash;

            string eventId = NewEventId();
            string json = BuildIapLinkJson(amazonUserId, receiptIds, deviceIdHash, ts, eventId);
            Debug.Log($"{Tag} >>> iap_link: amazon_user_id={amazonUserId}, receipts={receiptIds.Count}, device_id_hash={deviceIdHash}, ts={ts}, event_id={eventId}");

            var outcome = SendOutcome.Retry;
            yield return SendEventWithRetry(json, eventId, o => outcome = o);
            if (_iapLinkInFlightHash == linkHash)
                _iapLinkInFlightHash = null;

            if (outcome == SendOutcome.Delivered)
            {
                PlayerPrefs.SetString(IapLinkHashKey, linkHash);
                PlayerPrefs.Save();
                Debug.Log($"{Tag} iap_link confirmed, idempotency hash saved");
            }
            else
            {
                // Транзиент: событие уже в очереди и доедет флашем, но хеш не записан —
                // следующая сверка отправит связку заново. Дубль снимает идемпотентный
                // апсерт на бэкенде. Rejected залогирован в SendEventWithRetry.
                Debug.LogWarning($"{Tag} iap_link not confirmed ({outcome}) — hash NOT saved, will resend");
            }
        }

        /// <summary>
        /// Источник трафика (ТЗ, задача 2): опрашивает Adjust.GetAttribution с ретраями —
        /// паттерн DeviceIdProvider.RequestDeviceId — и шлёт событие attribution, когда
        /// атрибуция появилась. Реатрибуция (смена TrackerToken) отправляется заново.
        /// </summary>
        private IEnumerator ResolveAndSendAttribution()
        {
            if (_attributionRoutineRunning)
                yield break;

            _attributionRoutineRunning = true;
            yield return ResolveAndSendAttributionInner();
            _attributionRoutineRunning = false;
        }

        private IEnumerator ResolveAndSendAttributionInner()
        {
#if AMZN_ADJUST_ENABLED
            var adjust = SdkModuleRegistry.Get<AdjustModule>();
            if (adjust == null || !adjust.Enabled || !adjust.IsSdkInitialized) yield break;
            yield return WaitUntilDeviceIdResolved();

            // Как у iap_link: с сентинелом не шлём, токен не пишем — добор на форграунде
            // и следующем запуске.
            if (!_deviceIdResolved || string.IsNullOrEmpty(_deviceIdHash))
            {
                Debug.LogWarning($"{Tag} attribution skipped — no Fire ID (would be '{UnattributedDeviceId}')");
                yield break;
            }

            for (int i = 0; i < AttributionMaxAttempts; i++)
            {
                RequestAttribution();

                // WaitForSecondsRealtime — по уроку ResolveDeviceId: timeScale=0 на
                // интерстишеле не должен вешать опрос.
                yield return new WaitForSecondsRealtime(AttributionRetryIntervalSeconds);

                var a = _pendingAttribution;
                if (a == null || (string.IsNullOrEmpty(a.TrackerToken) && string.IsNullOrEmpty(a.Network)))
                    continue;

                // Identity can be refreshed while Adjust is resolving attribution.
                if (!_deviceIdResolved || string.IsNullOrEmpty(_deviceIdHash))
                    continue;
                string deviceIdHash = _deviceIdHash;
                string token = a.TrackerToken ?? string.Empty;
                string attributionKey = deviceIdHash + "|" + token;
                if (PlayerPrefs.GetString(AttributionTokenKey, null) == attributionKey)
                {
                    Debug.Log($"{Tag} attribution already delivered (tracker_token unchanged), skipping");
                    yield break;
                }

                long ts = GetTimestampMs();
                if (ts < MinTimestampMs)
                {
                    Debug.LogWarning($"{Tag} System clock appears incorrect (ts={ts}), skipping attribution");
                    yield break;
                }

                string eventId = NewEventId();
                string json = BuildAttributionJson(
                    a.Network, a.Campaign, a.Adgroup, a.Creative,
                    a.TrackerName, a.TrackerToken, a.CostAmount, a.CostCurrency,
                    deviceIdHash, ts, eventId);
                Debug.Log($"{Tag} >>> attribution: network={a.Network}, campaign={a.Campaign}, tracker_token={a.TrackerToken}, " +
                          $"cost={(a.CostAmount.HasValue ? a.CostAmount.Value.ToString(CultureInfo.InvariantCulture) : "null")} {a.CostCurrency}, " +
                          $"device_id_hash={deviceIdHash}, ts={ts}, event_id={eventId}");

                var outcome = SendOutcome.Retry;
                yield return SendEventWithRetry(json, eventId, o => outcome = o);

                if (outcome == SendOutcome.Delivered)
                {
                    PlayerPrefs.SetString(AttributionTokenKey, attributionKey);
                    PlayerPrefs.Save();
                    Debug.Log($"{Tag} attribution confirmed, tracker_token saved");
                }
                else
                {
                    Debug.LogWarning($"{Tag} attribution not confirmed ({outcome}) — token NOT saved, will resend");
                }

                // A foreground identity refresh may finish while this HTTP send is
                // running. RetryPending then sees our guard; service the new ID here.
                if (_deviceIdResolved && !string.IsNullOrEmpty(_deviceIdHash) && _deviceIdHash != deviceIdHash)
                {
                    i = -1;
                    continue;
                }
                yield break;
            }

            Debug.Log($"{Tag} attribution not resolved after {AttributionMaxAttempts} attempts — will retry on focus / next launch");
#else
            // Без Adjust источника атрибуции нет — событие attribution недоступно в этой сборке.
            Debug.Log($"{Tag} Adjust module disabled — attribution event unavailable");
            yield break;
#endif
        }

#if AMZN_ADJUST_ENABLED
        private void RequestAttribution()
        {
            try
            {
                AdjustSdk.Adjust.GetAttribution(attribution =>
                {
                    // null — атрибуция ещё не готова (типично сразу после установки),
                    // вердикт выносит цикл ретраев, как в DeviceIdProvider.
                    if (attribution != null)
                        _pendingAttribution = attribution;
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} Adjust.GetAttribution failed: {e.Message}");
            }
        }
#endif

        /// <summary>
        /// Ждёт резолва device_id (не дольше <see cref="DeviceIdWaitForEventSeconds"/>). Если так
        /// и не резолвился — событие всё равно уйдёт, но с device_id_hash='unattributed': это
        /// лучше, чем потерять его целиком.
        /// </summary>
        private IEnumerator WaitUntilDeviceIdResolved()
        {
            if (_deviceIdResolved)
                yield break;

            float deadline = Time.realtimeSinceStartup + DeviceIdWaitForEventSeconds;
            while (!_deviceIdResolved && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!_deviceIdResolved)
                Debug.LogWarning($"{Tag} device_id not resolved after {DeviceIdWaitForEventSeconds}s — sending event with '{UnattributedDeviceId}'");
        }

        private IEnumerator SendEventWithRetry(string json, string eventId, Action<SendOutcome> onResult = null,
            bool enqueue = true)
        {
            // Сначала кладём на диск, потом отправляем. Тогда, чем бы отправка ни закончилась
            // — в том числе если приложение убьют на середине запроса (игрок ушёл в стор) —
            // событие не пропадёт: оно уже в очереди и уйдёт при следующем запуске.
            // Дубли на бэкенде отсекаются по event_id.
            if (enqueue)
                AnalyticsEventQueue.Enqueue(json);

            if (!IsInternetAvailable())
            {
                Debug.LogWarning($"{Tag} No internet, event kept in queue (event_id={eventId})");
                onResult?.Invoke(SendOutcome.Retry);
                yield break;
            }

            var outcome = SendOutcome.Retry;
            yield return SendEvent(json, o => outcome = o);

            if (outcome == SendOutcome.Delivered || outcome == SendOutcome.Rejected)
            {
                // Удаляем из очереди ТОЛЬКО когда точно решено: доставлено (2xx) или
                // отвергнуто по существу (повтор не поможет). Транзиентный сбой оставляет
                // событие в очереди для ретрая при следующем флаше.
                AnalyticsEventQueue.Remove(eventId);
                if (outcome == SendOutcome.Rejected)
                    Debug.LogError($"{Tag} Event rejected by backend, dropped from queue (event_id={eventId}): {json}");
            }
            else
            {
                Debug.LogWarning($"{Tag} Send failed (transient), event stays in queue for retry (event_id={eventId})");
            }

            // Исход нужен вызывающим, которые пишут флаги идемпотентности только по
            // подтверждённой доставке (iap_link, attribution). Доставка транзиентного
            // события ПОЗЖЕ через FlushQueue сюда не попадает — это осознанно: флаг не
            // запишется, событие уйдёт повторно, дубль снимет идемпотентный апсерт бэкенда.
            onResult?.Invoke(outcome);
        }

        /// <summary>Что делать с событием после попытки отправки.</summary>
        private enum SendOutcome
        {
            /// <summary>HTTP 2xx — бэкенд принял событие.</summary>
            Delivered,

            /// <summary>Транзиентный сбой (сеть, 5xx, 401/403/408/429) — событие нужно повторить.</summary>
            Retry,

            /// <summary>Бэкенд отверг событие по существу (400/404/422) — повтор не поможет, дропаем.</summary>
            Rejected
        }

        private IEnumerator SendEvent(string jsonBody, Action<SendOutcome> onResult)
        {
            if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_apiKey))
            {
                // Конфиг битый — повторы не помогут и только забьют очередь.
                Debug.LogError($"{Tag} SendEvent aborted — baseUrl or apiKey is empty");
                onResult?.Invoke(SendOutcome.Rejected);
                yield break;
            }

            Debug.Log($"{Tag} POST {_baseUrl}/v1/events — body: {jsonBody}");

            using (var request = new UnityWebRequest(_baseUrl + "/v1/events", "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-api-key", _apiKey);
                request.timeout = HttpTimeoutSeconds;   // иначе зависший сокет держит корутину вечно

                yield return request.SendWebRequest();

                // Попутное наблюдение доверенного времени для периодов подписки (ТЗ IAP-14):
                // заголовок Date есть в любом ответе по RFC, включая ошибочные.
                SdkTrustedTime.OfferHttpDate(request.GetResponseHeader("Date"));

                long code = request.responseCode;

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"{Tag} <<< HTTP {code} OK: {request.downloadHandler.text}");
                    onResult?.Invoke(SendOutcome.Delivered);
                }
                else if (IsTransientHttpError(code))
                {
                    // 401/403 — почти всегда ротация или опечатка в API-ключе, чинится на бэкенде;
                    // 408/429 — таймаут и рейт-лимит. Всё это лечится повтором, поэтому НЕ считаем
                    // доставкой (иначе один сломанный ключ обнуляет воронку по всем устройствам).
                    Debug.LogWarning($"{Tag} <<< HTTP {code} (transient, will retry): {request.downloadHandler?.text}");
                    onResult?.Invoke(SendOutcome.Retry);
                }
                else if (code >= 400 && code < 500)
                {
                    Debug.LogWarning($"{Tag} <<< HTTP {code} (client error, not retrying): {request.downloadHandler?.text}");
                    onResult?.Invoke(SendOutcome.Rejected);
                }
                else
                {
                    Debug.LogWarning($"{Tag} <<< HTTP failed ({request.result}, code={code}): {request.error}");
                    onResult?.Invoke(SendOutcome.Retry);
                }
            }
        }

        private static bool IsTransientHttpError(long code) =>
            code == 401 || code == 403 || code == 408 || code == 429;

        private IEnumerator FlushQueue()
        {
            if (_flushing)
                yield break;

            _flushing = true;

            // Сначала ЧИТАЕМ очередь, не очищая её. Событие удаляется поштучно и только
            // после подтверждённой доставки/отказа. Раньше очередь очищалась ДО отправки
            // (DequeueAll) — если приложение убивали в середине флаша, пропадала вся пачка.
            var events = AnalyticsEventQueue.Peek();
            if (events == null || events.Count == 0)
            {
                _flushing = false;
                yield break;
            }

            if (!IsInternetAvailable())
            {
                Debug.LogWarning($"{Tag} FlushQueue: no internet, {events.Count} events remain in queue");
                _flushing = false;
                yield break;
            }

            Debug.Log($"{Tag} FlushQueue: sending {events.Count} queued events...");

            int delivered = 0;
            int rejected = 0;
            int remaining = 0;

            for (int i = 0; i < events.Count; i++)
            {
                // The snapshot can outlive a click's identity enrichment or removal.
                if (_preparingEventBodies.Contains(events[i]) || !AnalyticsEventQueue.ContainsExact(events[i]))
                    continue;
                var outcome = SendOutcome.Retry;
                yield return SendEvent(events[i], o => outcome = o);

                switch (outcome)
                {
                    case SendOutcome.Delivered:
                        AnalyticsEventQueue.RemoveExact(events[i]);
                        delivered++;
                        break;
                    case SendOutcome.Rejected:
                        // Отвергнутое по существу выкидываем, иначе навсегда заблокирует очередь.
                        AnalyticsEventQueue.RemoveExact(events[i]);
                        rejected++;
                        break;
                    default:
                        // Первый транзиентный сбой останавливает флаш: сеть/бэкенд лежат.
                        // Остальные события остаются в очереди нетронутыми.
                        remaining = events.Count - i;
                        Debug.Log($"{Tag} FlushQueue stopped (transient): {delivered} sent, {rejected} rejected, {remaining} remaining");
                        _flushing = false;
                        yield break;
                }
            }

            Debug.Log($"{Tag} FlushQueue complete: {delivered} sent, {rejected} rejected, 0 remaining");
            _flushing = false;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Пачки баннерных показов на потере фокуса не закрываются. Каждая уже привязана к
            // идентичности момента показа, так что перепроверка Fire ID на возврате их не трогает,
            // и они на диске. Закрытие всех ключей на каждом интерстишеле, диалоге или шторке
            // давало бы по запросу на paid_app_id с n≈1 — почти столько же запросов, сколько в 1.0.8.
            if (hasFocus && IsReady && !_identityRefreshRunning)
            {
                // The user may have reset the advertising ID while outside the app.
                // Refresh even offline; a previously cached ID must not survive a reset.
                StartCoroutine(RefreshIdentityAndRetry());
            }
        }

        private IEnumerator RefreshIdentityAndRetry()
        {
            _identityRefreshRunning = true;
            try
            {
                _deviceIdHash = null;
                _deviceIdResolved = false;
                DeviceIdProvider.BeginSession();
                yield return ResolveDeviceId();
            }
            finally
            {
                _identityRefreshRunning = false;
            }
            if (IsInternetAvailable())
                yield return RetryPending();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            // Игрок уходит в стор → Android шлёт OnApplicationPause(true). Пробуем дослать
            // очередь прямо сейчас: возврата (focus) можно не дождаться — ушедший в стор
            // часто не возвращается. Даже если запрос не успеет — событие уже на диске
            // (enqueue-first), так что не потеряется.
            // Пачки баннерных показов с истёкшим окном закрываются ДО флаша, чтобы попасть в его
            // снимок очереди (таймер на паузе стоит). Остальные не закрываются досрочно: пауза
            // случается на каждом полноэкранном объявлении и покупке, а незакрытая пачка и так на
            // диске и уйдёт по окну после возврата или на следующем запуске.
            if (pauseStatus)
                FlushBannerBatches(BannerBatchFlush.Due, sendNow: false);

            if (pauseStatus && IsReady && IsInternetAvailable())
            {
                Debug.Log($"{Tag} App paused — flushing queued events");
                StartCoroutine(FlushQueue());
            }
        }

        private void OnApplicationQuit()
        {
            // Отправить уже не успеем: закрываем привязанные пачки в очередь, её дошлёт следующий
            // запуск. Непривязанные остаются в буфере на диске.
            FlushBannerBatches(BannerBatchFlush.All, sendNow: false);
        }

        /// <summary>
        /// Досылает всё, что не ушло: first_open (если флаг так и не выставлен) и очередь событий.
        /// </summary>
        private IEnumerator RetryPending()
        {
            if (!_firstOpenInFlight)
            {
                _firstOpenInFlight = true;
                yield return TrySendFirstOpen();
                _firstOpenInFlight = false;
            }

            // Добор атрибуции: на старте она могла не резолвиться (нет сети, Adjust не
            // успел). Корутина сама выйдет мгновенно, если токен уже доставлен.
            if (!_attributionRoutineRunning)
                StartCoroutine(ResolveAndSendAttribution());

            yield return FlushQueue();
        }

        private static bool IsInternetAvailable() =>
            Application.internetReachability != NetworkReachability.NotReachable;

        private static long GetTimestampMs() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Случайный идентификатор события: и для удаления из очереди, и для дедупа
        /// на бэкенде (в т.ч. чтобы enqueue-first не породил дубли при повторной отправке).</summary>
        private static string NewEventId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Версии сборки — в каждом событии с 1.0.9: по ним бэкенд и BI отличают сборки игр.
        /// Стоят непосредственно перед <c>ts</c>, который остаётся последним полем.
        /// </summary>
        private static string BuildVersionFields() =>
            $"\"sdk_version\":\"{EscapeJson(SdkPackageVersion.Value)}\"," +
            $"\"app_version\":\"{EscapeJson(AppVersion)}\",";

        /// <summary>Поле <c>n</c> склеенных баннерных показов; у одиночных событий его нет.</summary>
        private static string BuildCountField(int count) =>
            count > 0 ? $"\"n\":{count.ToString(CultureInfo.InvariantCulture)}," : string.Empty;

        private static string BuildFirstOpenJson(
            string eventName, string appId, string appType, string deviceIdHash, long ts, string eventId)
        {
            return "{" +
                   $"\"event_name\":\"{EscapeJson(eventName)}\"," +
                   $"\"event_id\":\"{EscapeJson(eventId)}\"," +
                   $"\"app_id\":\"{EscapeJson(appId)}\"," +
                   $"\"app_type\":\"{EscapeJson(appType)}\"," +
                   $"\"device_id_hash\":\"{EscapeJson(deviceIdHash)}\"," +
                   BuildVersionFields() +
                   $"\"ts\":{ts}" +
                   "}";
        }

        /// <param name="count">Число показов в склеенном баннерном событии (поле <c>n</c>); 0 — одиночное событие без поля.</param>
        private static string BuildCrossPromoEventJson(
            string eventName, string paidAppId, string donorAppId, string deviceIdHash, long ts, string eventId, string placement,
            int count = 0)
        {
            // Старые вызовы без плейсмента сохраняют прежний payload. Очередь хранит
            // готовый JSON, поэтому плейсмент баннера сохранится и при повторной отправке.
            string placementField = string.IsNullOrWhiteSpace(placement)
                ? string.Empty
                : $"\"placement\":\"{EscapeJson(placement)}\",";

            return "{" +
                   $"\"event_name\":\"{EscapeJson(eventName)}\"," +
                   $"\"event_id\":\"{EscapeJson(eventId)}\"," +
                   $"\"paid_app_id\":\"{EscapeJson(paidAppId)}\"," +
                   $"\"donor_app_id\":\"{EscapeJson(donorAppId)}\"," +
                   $"\"device_id_hash\":\"{EscapeJson(deviceIdHash)}\"," +
                   placementField +
                   BuildCountField(count) +
                   BuildVersionFields() +
                   $"\"ts\":{ts}" +
                   "}";
        }

        /// <summary>
        /// Схема показа/клика медиации. <c>revenue</c> — числом, а не строкой: на бэкенде это
        /// сумма, и приводить её из строки к числу пришлось бы в каждом запросе.
        /// InvariantCulture обязателен — локаль с запятой сломала бы JSON.
        /// У склеенного показа MAX-баннера (<paramref name="count"/> &gt; 0) <c>revenue</c> — сумма за все n показов.
        /// </summary>
        private static string BuildMediationEventJson(
            string eventName, string network, string adUnit, string placement, double revenue, string precision,
            string appId, string deviceIdHash, long ts, string eventId, int count = 0)
        {
            // Форматируем до интерполяции — как в BuildAttributionJson: InvariantCulture
            // обязателен, локаль с запятой в разделителе сломала бы JSON. NaN и бесконечность
            // (возможны только из ручного вызова) дали бы голый литерал и невалидный JSON —
            // в протоколе неизвестная выручка это -1, как у MAX.
            string revenueText = double.IsNaN(revenue) || double.IsInfinity(revenue)
                ? UnknownRevenue.ToString("R", CultureInfo.InvariantCulture)
                : revenue.ToString("R", CultureInfo.InvariantCulture);

            return "{" +
                   $"\"event_name\":\"{EscapeJson(eventName)}\"," +
                   $"\"event_id\":\"{EscapeJson(eventId)}\"," +
                   $"\"app_id\":\"{EscapeJson(appId)}\"," +
                   $"\"device_id_hash\":\"{EscapeJson(deviceIdHash)}\"," +
                   $"\"network\":\"{EscapeJson(network ?? string.Empty)}\"," +
                   $"\"ad_unit\":\"{EscapeJson(adUnit ?? string.Empty)}\"," +
                   $"\"placement\":\"{EscapeJson(placement ?? string.Empty)}\"," +
                   $"\"revenue\":{revenueText}," +
                   $"\"revenue_precision\":\"{EscapeJson(precision ?? string.Empty)}\"," +
                   BuildCountField(count) +
                   BuildVersionFields() +
                   $"\"ts\":{ts}" +
                   "}";
        }

        private static string BuildIapLinkJson(
            string amazonUserId, List<string> receiptIds, string deviceIdHash, long ts, string eventId)
        {
            return "{" +
                   "\"event_name\":\"iap_link\"," +
                   $"\"event_id\":\"{EscapeJson(eventId)}\"," +
                   $"\"device_id_hash\":\"{EscapeJson(deviceIdHash)}\"," +
                   $"\"app_id\":\"{EscapeJson(AppId)}\"," +
                   $"\"amazon_user_id\":\"{EscapeJson(amazonUserId)}\"," +
                   $"\"receipt_ids\":{BuildJsonStringArray(receiptIds)}," +
                   BuildVersionFields() +
                   $"\"ts\":{ts}" +
                   "}";
        }

        private static string BuildAttributionJson(
            string network, string campaign, string adgroup, string creative,
            string trackerName, string trackerToken, double? costAmount, string costCurrency,
            string deviceIdHash, long ts, string eventId)
        {
            // cost_amount: голое null, не строка "null" и не 0 — ноль означал бы бесплатный
            // инсталл. InvariantCulture обязателен: локаль с запятой сломала бы JSON.
            string cost = costAmount.HasValue
                ? costAmount.Value.ToString("R", CultureInfo.InvariantCulture)
                : "null";

            return "{" +
                   "\"event_name\":\"attribution\"," +
                   $"\"event_id\":\"{EscapeJson(eventId)}\"," +
                   $"\"device_id_hash\":\"{EscapeJson(deviceIdHash)}\"," +
                   $"\"app_id\":\"{EscapeJson(AppId)}\"," +
                   $"\"network\":{JsonStringOrNull(network)}," +
                   $"\"campaign\":{JsonStringOrNull(campaign)}," +
                   $"\"adgroup\":{JsonStringOrNull(adgroup)}," +
                   $"\"creative\":{JsonStringOrNull(creative)}," +
                   $"\"tracker_name\":{JsonStringOrNull(trackerName)}," +
                   $"\"tracker_token\":{JsonStringOrNull(trackerToken)}," +
                   $"\"cost_amount\":{cost}," +
                   $"\"cost_currency\":{JsonStringOrNull(costCurrency)}," +
                   BuildVersionFields() +
                   $"\"ts\":{ts}" +
                   "}";
        }

        /// <summary>Пустой список — как "[]", а не пропуск поля (требование ТЗ).</summary>
        private static string BuildJsonStringArray(List<string> items)
        {
            if (items == null || items.Count == 0)
                return "[]";

            var sb = new StringBuilder(items.Count * 24 + 2);
            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append('"').Append(EscapeJson(items[i])).Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>Отсутствующее строковое поле — голое null, а не пустая строка (требование ТЗ).</summary>
        private static string JsonStringOrNull(string s) =>
            string.IsNullOrEmpty(s) ? "null" : "\"" + EscapeJson(s) + "\"";

        /// <summary>Канонический хеш связки: receiptIds уже отсортированы вызывающим.</summary>
        private static string ComputeIapLinkHash(string deviceIdHash, string amazonUserId, List<string> receiptIds)
        {
            var sb = new StringBuilder(deviceIdHash.Length + amazonUserId.Length + receiptIds.Count * 24 + 2);
            sb.Append(deviceIdHash).Append('|').Append(amazonUserId).Append('|');
            for (int i = 0; i < receiptIds.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(receiptIds[i]);
            }

            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 2);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Формат буфера незакрытых баннерных пачек в PlayerPrefs (<see cref="BannerBatchesKey"/>).</summary>
        [Serializable]
        private class BannerBatchState
        {
            public List<BannerImpressionBatch> Items = new();
        }
    }
}
#endif
