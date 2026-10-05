# Исправления по ревью после v1.0.7

Дата: **5 октября 2026 года**. Запрос: исправить все выявленные проблемы, кроме legacy Unity Video Player. Основание — [историческое ревью](REPORT.md) состояния `ac39f9c` после стабильной версии `v1.0.7`. Ниже описано состояние после исправлений; старые номера строк в историческом отчёте относятся к прежнему коду.

**Результат:** исправлены 14 согласованных пунктов: 11 функциональных дефектов (1 P1 и 10 P2), 1 ошибка документации P3 и 2 условные проблемы интеграции. E05, связанный с выдачей награды в legacy Unity Video Player, оставлен по прямому указанию пользователя. Три независимых направления ревью одобрили итоговый код; финальная изолированная компиляция Run3 прошла **30/30 без ошибок**. Это не означает выполнения сценариев на устройствах.

Исправления подготовлены в `tmp/review-fixes-v107`, созданной от `safety` на `ac39f9c4d4ae788ae40e45aa09a89347efd75916`. Push не выполнялся. Замороженная `main` не использовалась. `CrossPromoVideoOverlay.cs` и `CrossPromoModule.cs`, включая legacy-ветку награды, не изменены.

## Матрица закрытия

| ID | Исходный приоритет | Итог | Изменение |
|---|---|---|---|
| R01 | P1 | Исправлен | Экспортируемый wrapper получает Gradle 8.13 и согласованный SHA-256. |
| R02 | P2 | Исправлен | Финальный Android-профиль применяется после сохранённых overrides MAX. |
| R03 | P2 | Исправлен | Полноэкранная реклама скрывает оба вида баннера и останавливает CP-ротацию. |
| R04 | P2 | Исправлен | Banner impressions ограничены 10 из 50 записей и не вытесняют обычные события. |
| R05 | P2 | Исправлен | Учтена видимость Unity UI; служебный CanvasGroup отделён от группы игры. |
| R06 | P2 | Исправлен | iOS Adjust getters сопоставляют ответы отдельным request ID. |
| E01 | P2 | Исправлен | Пустой `scopedRegistries` обрабатывается без лишней запятой, JSON проверяется. |
| E02 | P2 | Исправлен | AppHud template читается по физическому пути Assets/UPM. |
| E03 | P2 | Исправлен | Always-compiled helper управляет AppHud XML при выключении SDK/module/feature. |
| E04 | P2 | Исправлен | Fire ID проверяется заново; IAP/attribution dedup учитывает текущую identity. |
| E06 | P2 | Исправлен | Backend-клик сохраняется до ожидания ID и не ждёт сетевой запрос Adjust. |
| D01 | P3 | Исправлен | Документация отражает 16 адаптеров, AdMob и настройку Android App ID. |
| C01 | Условный риск | Исправлен | IAP сохраняет абсолютные deadlines и восстанавливает ожидание после `SetActive`. |
| C02 | Условный риск | Исправлен в доступных границах | Модуль сети не присваивает существующую паузу и не затирает новое ненулевое время. |
| E05 | P2 | Исключён пользователем | Legacy Unity Video Player и его обработка награды не менялись. |

«Исправлен» означает реализацию изменения, одобрение статическим ревью и успешную доступную компиляцию. Сценарии приёмки ниже **не выполнены** и перечислены для последующей проверки интеграции.

## R01 — совместимый Gradle wrapper

**Код:** [AndroidGradleToolchain.cs:78](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/SdkModulesSettings/AndroidGradleToolchain.cs:78), [AndroidToolchainSettings.cs:26](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/SdkModulesSettings/AndroidToolchainSettings.cs:26).

`PrepareWrapper` включает `gradle/wrapper/gradle-wrapper.properties` в подготовку сгенерированного проекта. URL дистрибутива и SHA-256 теперь берутся из тех же констант, что использует установщик Gradle: `gradle-8.13-bin.zip`, `20f1b1176237254a6fc204d8434196fa11a4cfb387567519c61556e8710aed78`. Прежний checksum заменяется вместе с URL; остальные свойства wrapper сохраняются. Отсутствующий wrapper при Export Project вызывает явную ошибку; для прямой сборки Unity без wrapper допустим прежний путь.

**Совместимость:** AGP остаётся 8.13.2, Gradle — 8.13, JDK — 17. Изменения применяются к сгенерированному Android-проекту. Обработка properties учитывает `=`, `:`, пробельный разделитель и продолжения строк, чтобы старое значение не оставалось вторым действующим определением.

**Приёмка:** экспортировать чистый проект и проект со старым wrapper/checksum; проверить версии, сохранение пользовательских timeout/cache properties и отсутствие дублирующих значений. Запустить именно экспортированный `gradlew --version` и сборку через wrapper. Повторить обычную сборку из Unity и экспорт с отсутствующим wrapper: в последнем случае ожидается понятная ошибка.

## R02 — окончательный профиль после MAX

**Код:** [AndroidGradleToolchain.cs:20](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/SdkModulesSettings/AndroidGradleToolchain.cs:20).

Порядок callback изменён с `2500` на `int.MaxValue - 2`: после MAX (`int.MaxValue - 10`), перед завершающей очисткой запрещённых компонентов (`int.MaxValue - 1`). Сохранённый MAX override больше не является последней записью AGP поверх согласованного SDK-профиля. Это также гарантирует подготовку wrapper из R01 на том же завершающем этапе.

**Совместимость:** пользовательская настройка MAX не переписывается в исходном asset; финальный сгенерированный проект получает профиль SDK. Вывод относится к установленному MAX 8.6.6; порядок vendor callbacks нужно перепроверять при его обновлении.

**Приёмка:** сохранить в MAX `CustomGradleToolsVersion = 7.4.2`, выполнить экспорт и обычную сборку. После всех callbacks в проекте должны остаться AGP 8.13.2, wrapper 8.13 и прежние согласованные SDK/JDK настройки. Повторить без override.

## R03 — баннеры под полноэкранной рекламой

**Код:** [CrossPromoBanner.cs:442](<D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Banner/CrossPromoBanner.cs:442>), [проверка перед impression:246](<D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Banner/CrossPromoBanner.cs:246>), [управление ротацией:511](<D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Banner/CrossPromoBanner.cs:511>).

Общая проверка `CanDisplayBanner` применяется до выбора CP/MAX и непосредственно перед отправкой показа. Открытые CP video и MAX fullscreen запрещают оба вида баннера. Скрытие CP останавливает ротацию; после закрытия fullscreen обычная проверка снова выбирает допустимого провайдера. Обновление ссылки на MAX выполняется до проверки видимости, чтобы корректно скрывать прежнего владельца нативного баннера.

**Совместимость:** сохраняются проверки no-ads, явного скрытия, caps и готовности MAX. Не изменялись видеопроигрыватель, маршрутизация награды или prefab-иерархия.

**Приёмка:** при CP-баннере открыть ExoPlayer interstitial на 20–30 секунд; за это время не должно быть `cp_impression` с placement `banner`. Повторить с MAX fullscreen, исчерпанием последнего cap во время видео и no-ads. После закрытия проверить единственное возобновление ротации и правильный выбор CP/MAX.

## R04 — защита очереди от потока баннерных показов

**Код:** [AnalyticsEventQueue.cs:13](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/AnalyticsEventQueue.cs:13), [правила вытеснения:193](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/AnalyticsEventQueue.cs:193).

Общий предел остаётся 50 записей, но `cp_impression` с `placement = "banner"` занимает не более 10. Новый такой показ вытесняет старый баннерный показ; если очередь заполнена только обычными событиями, он не сохраняется. Обычный event сначала вытесняет баннерный, и только при отсутствии баннерных — самый старый обычный. Политика применяется при enqueue, чтении, сохранении и requeue, в том числе к очередям предыдущей версии.

**Совместимость:** ключ PlayerPrefs `cp_event_queue`, оболочка JSON и существующий `Enqueue` сохранены. Неизвестные, legacy и неразобранные payload не приравниваются к низкоприоритетным показам. `TryEnqueue`, `ReplaceExact` и `ContainsExact` поддерживают безопасное обогащение сохранённого клика из E06. Это ограниченная очередь: 51-е обычное событие всё ещё может вытеснить самое старое обычное.

**Приёмка:** при ответах backend 503 накопить клики, revenue и более 50 banner impressions; после перезапуска должно остаться не больше 10 баннерных записей, а обычные не должны быть вытеснены ими. Повторить для старой переполненной очереди, requeue, полностью заполненной обычными событиями очереди и восстановления сети. Проверить сохранение `event_id` и серверную дедупликацию повторов.

## R05 — видимость Unity UI и CanvasGroup игры

**Код:** [CrossPromoBanner.cs:51](<D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Banner/CrossPromoBanner.cs:51>), [IsBannerUiVisible:458](<D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Banner/CrossPromoBanner.cs:458>).

Для переключения CP/MAX создаётся отдельный служебный CanvasGroup. Первая группа остаётся интерфейсом игры, поэтому её alpha, interactable и blocksRaycasts не перезаписываются каждый кадр. Проверяются Image, его alpha/CanvasRenderer, активные Canvas и CanvasGroup по иерархии с учётом `ignoreParentGroups`. Собственная служебная группа исключена из проверки входной видимости: её alpha=0 при показе MAX не блокирует сам MAX.

**Совместимость:** prefab не переподчиняется новому объекту; `GetComponent<CanvasGroup>()` продолжает находить группу игры. Нативный MAX поддерживает показ/скрытие; частичная Unity alpha не превращается в плавную прозрачность нативного view. Проверка относится к перечисленным состояниям UI и fullscreen, а не к произвольному перекрытию посторонним интерфейсом игры.

**Приёмка:** для CP и MAX проверить alpha 0/0.5/1, выключение Image и родительского Canvas, разные CanvasGroup на нескольких уровнях, `ignoreParentGroups`, interactable/blocksRaycasts и деактивацию объекта. Fade игры не должен сбрасываться после Update; при полностью скрытом UI нативный баннер и CP impressions отсутствуют. После повторного включения баннер должен восстановиться без новой иерархии или лишнего служебного владельца.

## R06 — отдельные callbacks iOS Adjust

**Код:** [AdjustiOS.cs:565](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Adjust/Adjust/Scripts/AdjustiOS.cs:565), [регистрация и извлечение:811](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Adjust/Adjust/Scripts/AdjustiOS.cs:811), [AdjustUnity.mm:426](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Adjust/Adjust/Native/iOS/AdjustUnity.mm:426), [AdjustUnity.h:16](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Adjust/Adjust/Native/iOS/AdjustUnity.h:16).

Обычные и timeout getters adid/attribution, а также third-party-sharing getter получают отдельный числовой ID. ID проходит через P/Invoke, native completion и AOT callback. Вместо общего списка используются типизированные словари; на главном потоке извлекается и удаляется только соответствующий обработчик перед вызовом пользовательского кода. Повторный или поздний ответ уже завершённого запроса не потребляет callbacks других запросов.

**Совместимость:** публичные C# сигнатуры getters не изменены. Внутренний native ABI изменён согласованно в трёх файлах; частичное обновление только C# или только `.mm/.h` недопустимо. Локальный vendor patch и требование сохранять его при обновлении Adjust описаны в [README Adjust](../../../Runtime/Modules/Adjust/Adjust/README.md).

**Приёмка:** выполнить Xcode/IL2CPP сборку и на iOS одновременно вызвать обычный getter и два timeout getter с разными сроками. Первый timeout должен завершить только свой вызов. Повторить обратный порядок ответов, null, повторный native response и запуск нового getter из callback для adid, attribution и third-party-sharing. Сейчас проверены C#-компиляция и согласованность ABI по исходникам; native сборка не выполнялась.

## E01 — корректная вставка scoped registry

**Код:** [AppLovinPackageInstaller.cs:1017](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/SdkDependencies/AppLovinPackageInstaller.cs:1017), [ManifestJson.cs:19](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/SdkDependencies/ManifestJson.cs:19).

Перед изменением разбирается весь manifest; поле ищется именно в корневом JSON-объекте. Для пустого массива запятая после добавленной записи не вставляется. Корректно обрабатываются отсутствие поля и пустой корневой объект. Проверка наличия registry ищет URL в соответствующей структуре, а не произвольное совпадение текста в manifest. Итоговый JSON повторно проверяется перед записью.

**Совместимость:** существующие данные и форматирование сохраняются вставкой текста. Невалидный JSON, неправильный тип `scopedRegistries`, дубли корневых полей, trailing comma и недопустимые числовые литералы дают ошибку до записи. Новый parser не требует дополнительного пакета; его `.meta` включён.

**Приёмка:** установка в `{}`, manifest без поля, с `[]`, непустым массивом и уже существующим AppLovin registry. Повторная установка не должна дублировать запись. URL в посторонней строке не должен считаться registry; повреждённый JSON не должен перезаписываться. После успешной вставки Unity Package Manager должен прочитать manifest.

## E02 — AppHud в UPM

**Код:** [AppMetricaAppHudDependencies.cs:35](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/ConditionalCompilation/AppMetricaAppHudDependencies.cs:35), [EdmDependencyGenerator.cs:148](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/ConditionalCompilation/EdmDependencyGenerator.cs:148).

AppHud использует общий resolver шаблонов: SDK root преобразуется `FileUtil.GetPhysicalPath`, после чего проверяется существование физического файла. Больше нет попытки прочитать виртуальный `Packages/...` через `File.ReadAllBytes` напрямую. Сгенерированный XML сохраняется в consumer `Assets/Editor/AppMetricaAppHudAdapterDependencies.xml`; package cache остаётся источником шаблона.

**Совместимость:** путь внутри SDK и место вывода сохраняются; поддерживается расположение SDK в Assets и через UPM. Отсутствующий шаблон становится диагностируемой ошибкой синхронизации/валидации.

**Приёмка:** включить AppHud в consumer с SDK в Assets, затем в Git/registry UPM-установке с immutable PackageCache. В каждом случае проверить создание одинаковых Maven/Pod dependencies, повторное сохранение без лишних изменений и отсутствие записи в пакет.

## E03 — очистка AppHud после отключения

**Код:** [AppMetricaAppHudDependencies.cs:19](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/ConditionalCompilation/AppMetricaAppHudDependencies.cs:19), [владение файлом:90](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/ConditionalCompilation/AppMetricaAppHudDependencies.cs:90), [DisabledModuleBuildGuard.cs:39](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Editor/ConditionalCompilation/DisabledModuleBuildGuard.cs:39).

Управление вынесено в always-compiled Editor helper, вызванный общей регенерацией EDM. Он учитывает одновременно SDK, AppMetrica и feature AppHud; поэтому отключение define модуля больше не удаляет сам механизм очистки. SDK-owned XML удаляется через AssetDatabase вместе с meta. Build guard проверяет отсутствие лишнего файла при выключении, наличие при включении и соответствие текущему шаблону.

**Совместимость:** новые файлы получают явный маркер владения SDK. Автоматически мигрируются только два точных legacy payload с vendor-маркером: Android/iOS `8.5.1/1.1.2` и `7.12.0/1.0.0`. Изменённый вручную legacy-файл сохраняется и выдаёт ошибку с инструкцией вынести собственные зависимости в отдельный XML. SDK-owned файл является генерируемым; пользовательские зависимости нельзя добавлять внутрь него.

**Приёмка:** включить feature, затем отдельно выключить feature, AppMetrica и весь SDK, включая domain reload. После каждого выключения XML/meta и разрешённая из них native dependency должны исчезать. Повторить на старых точных payload, изменённом legacy XML, malformed XML и недоступном шаблоне: чужие данные должны сохраняться, а проблема блокировать соответствующую сборку понятной диагностикой.

## E04 — актуальная identity и её дедупликация

**Код:** [DeviceIdProvider.cs:47](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/DeviceIdProvider.cs:47), [проверка источника:74](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/DeviceIdProvider.cs:74), [Adjust fallback:232](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/DeviceIdProvider.cs:232), [Analytics foreground:818](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/AnalyticsModule.cs:818).

Каждый старт и возвращение в foreground начинают новую проверку Fire ID. Сохранённый hash не принимается без свежего чтения системы или Adjust fallback. Отсутствующий/нулевой ID очищает raw/hash/param; неудачное системное чтение также не разрешает использовать непроверенный старый кэш. Fallback остаётся повторяемым: запоздалый успешный ответ текущей identity-сессии принимается, ответ прежней сессии игнорируется; старый ответ не снимает флаг нового запроса.

**Связанные изменения:** [IAP dedup:453](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/AnalyticsModule.cs:453) и [attribution dedup:532](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/AnalyticsModule.cs:532) включают валидированную identity. HTTP payload и dedup-marker используют один её снимок; завершение старой отправки не снимает защиту новой IAP-связки. Attribution, отправлявшаяся во время foreground refresh, затем обслуживает уже обновлённый ID. При деактивации Analytics сбрасываются временные флаги корутин, при повторной активации инициализация возобновляется, дисковая очередь сохраняется.

**Совместимость:** сторонний device identifier не подставляется; отсутствие Fire ID представляется `unattributed` там, где это поддерживает событие. Исторические ключи очереди/first_open сохраняются; `first_open` остаётся один раз на установку/app type. Старые IAP/attribution markers могут привести к одной дополнительной отправке после обновления; серверная идемпотентность нужна по-прежнему. Уже сохранённые события не перепривязываются к identity следующей сессии.

**Приёмка:** обновиться со старым нормальным и all-zero кэшем, сбросить Fire ID между запусками и в фоне, перейти на child profile/отсутствующий ID, повторить без сети и без Adjust. Задержать callback fallback дольше 1.5 секунды: успешный ответ текущей сессии должен приниматься; callback предыдущей сессии — игнорироваться. Во время attribution HTTP сменить ID; следующая связка должна использовать новый ID, не повторяя first_open. Деактивировать/активировать Analytics во время подготовки клика и flush: очередь должна продолжить отправку.

## E06 — сохранение ExoPlayer-клика до внешнего ожидания

**Код:** [CrossPromoExoNativeOverlay.cs:471](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Cross-Promo/VideoPlayer/CrossPromoExoNativeOverlay.cs:471), [AnalyticsModule.cs:364](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/AnalyticsModule.cs:364), [flush snapshot guard:778](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/Analytics/AnalyticsModule.cs:778).

Backend и внешнее Adjust tracking запускаются независимо на CrossPromoModule; медленный Adjust не задерживает старт backend-ветки. Analytics для `cp_click` создаёт полный payload и сохраняет его до первого ожидания Fire ID. После разрешения ID заменяется только ещё существующая запись с тем же `event_id`; удалённая или вытесненная запись не создаётся заново. Пока выполняется подготовка, flush пропускает её; устаревший снимок очереди после замены payload не отправляется.

**Совместимость:** ExoPlayer открывает магазин не позднее своего прежнего предела ожидания **4 секунды**; баннер сохраняет отдельный предел **1.5 секунды**. Сохраняется по одному backend-клику на вызов. Смерть процесса до разрешения ID оставляет доставляемое событие `unattributed`; следующий запуск не подменяет его identity. Persistence через PlayerPrefs и bounded queue уменьшает прежнюю потерю, но не даёт гарантии exactly-once: backend должен дедуплицировать `event_id`.

**Приёмка:** задержать Adjust GET на 15 секунд и Fire ID более чем на 4 секунды, кликнуть CTA и закрыть процесс после открытия магазина. После запуска должен остаться тот же сохранённый `cp_click`, а не новый ID события. Проверить 503/retry, конкурентный flush во время обогащения identity, деактивацию Analytics, уничтожение overlay и восстановление сети. Отдельно подтвердить отсутствие двойного `cp_click` и неизменность времени перехода в магазин.

## D01 — состав адаптеров и миграция AdMob

**Код и документация:** [ANDROID-DEPENDENCIES.md](../../../Documentation~/ANDROID-DEPENDENCIES.md), [CHANGELOG.md](../../../CHANGELOG.md), [инструкции потребителя](../../../Documentation~/README.md).

Число разрешённых обязательных Android-адаптеров исправлено с 15 на **16**, отдельно от MAX core. В таблицу добавлен Google AdMob: UPM `25050000.0.0`, native `25.5.0.0`. Добавлена миграция существующего consumer: сохранение SDK Settings устанавливает adapter, затем нужен Android App ID в разделе AppLovin MAX. Пример формата различает App ID `ca-app-pub-…~…` и ad unit ID.

**Совместимость:** пустое поле SDK сохраняет уже заданный ID в MAX; preflight проверяет итоговый ID. Google Ad Manager остаётся исключённым. Исторические сведения о сентябрьской APK-проверке отмечены как исторические и не выдаются за проверку этой серии исправлений.

**Приёмка:** обновить consumer, созданный до включения AdMob в обязательный набор; сохранить SDK Settings, проверить MAX core + 16 adapters и синхронизацию App ID. С отсутствующим/ошибочным итоговым ID Android build должен остановиться до сборки; с валидным ID — пройти этот preflight.

## C01 — IAP после деактивации SDK-объекта

**Код:** [IapRetryScheduler.cs:50](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase/Core/IapRetryScheduler.cs:50), [жизненный цикл scheduler:131](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase/Core/IapRetryScheduler.cs:131), [приём ответа:605](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase/InAppPurchaseModule.cs:605), [callbacks:805](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/InAppPurchase/InAppPurchaseModule.cs:805).

Watchdog и retry хранят абсолютные deadlines отдельно от Coroutine handle. `SetActive(false)` приостанавливает handles, сохраняя намерение повторить; `OnEnable` восстанавливает ожидание или немедленно обрабатывает уже истёкший срок. Новый Restore на неактивном объекте остаётся ожидающим, а не пытается запустить корутину там. Просроченные native responses проверяются до применения receipts; RequestId-фильтрация старых прогонов сохраняется.

**Защита повторных входов:** callbacks завершённого неуспешного прогона отделяются до внешних событий, поэтому Restore, вызванный из игрового callback, не завершается результатом старого прогона. После полного получения ответа таймаут ожидания отключается, но single-flight сохраняется на время применения результата. Cleanup сначала снимает готовность сервиса и listeners, затем завершает ожидания: новый вызов из callback не стартует в очищаемом сервисе.

**Совместимость:** предел ожидания ответа остаётся 120 секунд; backoff — 2/8/30 секунд. Отключение только `component.enabled` отделено от деактивации GameObject: первое само по себе не останавливает Unity-корутины. Изменение не является обещанием исполнения кода, пока весь объект неактивен; оно обеспечивает корректное восстановление/истечение срока после активации и при native ответе.

**Приёмка:** деактивировать SDK-объект в середине многостраничной сверки и каждого retry, вернуть до и после 120 секунд; просроченные receipts не применять. Выполнить Restore на неактивном объекте, вызвать новый Restore из success/failure/exhaustion callback и одновременно с Cleanup. Каждый исходный callback должен получить ровно свой результат; новый прогон не должен зависнуть, наследовать старый timeout или обработать чужой RequestId.

## C02 — владение паузой игры

**Код:** [InternetConnectionModule.cs:180](D:/Projects/amzngodsdke/Assets/AMZNGoDSDK/Runtime/Modules/InternetConnection/InternetConnectionModule.cs:180), [интеграционные указания](../../../Documentation~/README.md).

Если `Time.timeScale` уже равен нулю, модуль не объявляет себя владельцем паузы. Если он сам установил ноль, восстановление прежнего значения выполняется только пока текущее значение всё ещё ноль. Ненулевое значение, выставленное игрой во время отсутствия сети, сохраняется при reconnect/cleanup.

**Граница исправления:** две записи одного и того же нуля разными системами неразличимы по одному `Time.timeScale`. Для игры с общим pause controller документация рекомендует `PauseGameWhenOffline = false` и подключение `OnInternetLost`/`OnInternetAvailable` к этому controller. Это оставшееся ограничение интерфейса, а не скрытая гарантия распознавания всех владельцев паузы.

**Приёмка:** начать с timeScale 1 и 0.5, потерять/вернуть сеть — восстановить исходное значение. Начать с игровой паузы 0, потерять сеть, затем возобновить игру до reconnect — повторно не остановить её. Пока SDK держит паузу, установить из игры ненулевое значение — сохранить его. Отдельно проверить consumer с отключённой автоматической паузой и обработчиками событий.

## Итог независимого ревью

Проверены три направления: Android/Editor и зависимости; CP/Analytics/runtime lifecycle; iOS Adjust bridge. После обнаруженных в ходе исправлений пограничных сценариев проведены повторные ревью: задержанный Fire ID callback, перекрытие attribution с identity refresh, reentrant Restore, timeout во время применения готового ответа и восстановление Analytics после деактивации. Итоговый код одобрен без оставленных новых замечаний в согласованной области. Точное число внутренних итераций ревью не протоколировалось; известно три прохода компиляции.

По первоначальной классификации закрыто: **P1 — 1, P2 — 10, P3 — 1; C01/C02 — 2 условных пункта**. **E05 P2 остаётся вне объёма по решению пользователя.** Результат ревью не является доказательством отсутствия иных дефектов и не заменяет приёмку описанных platform/lifecycle сценариев.

## Проверка компиляции

Финальное доказательство — **Run3**, а не прошедшие ранее Run1/Run2. Unity **2022.3.60f1** Roslyn: **30 запусков, 0 ошибок**. SHA-256 всех **330 C# source/asmdef** совпали до и после основной компиляции и вариантов с отключёнными модулями. Текущие файлы и asmdef включены в response files, SDK-сборки пересобраны по зависимостям с run-local references; старые SDK DLL из Bee не использовались вместо новой компиляции.

| Конфигурация | Число запусков | Результат |
|---|---:|---|
| Editor Android, все SDK module defines включены | 13 | PASS |
| Android Player, все SDK module defines включены | 11 | PASS |
| iOS Adjust C#, без Android/Editor defines | 1 | PASS |
| Editor Android, AppMetrica выключена | 1 | PASS |
| Editor Android, SDK/module defines выключены | 1 | PASS |
| Android Analytics без Adjust define/direct references | 1 | PASS |
| Android Cross-Promo без AppLovin define/direct reference | 1 | PASS |
| Android Cross-Promo без AppLovin/Analytics defines/direct references | 1 | PASS |

В логах сохранены warnings: существующие CS0168 в Editor, CS0618 `SetSdkKey`, CS0414 `_firstWarmupTriggered`; в варианте без Adjust — неиспользуемые `_requestInFlight`/`_lastRequestTime`. Они не объявляются исправленными и не скрываются утверждением «без ошибок».

Локальные подробности: [COMPILATION.md](../../../Temp~/ReviewFixes20261005/COMPILATION.md), [Run3 Summary](../../../Temp~/ReviewFixes20261005/Run3/Summary.json), [toggle Summary](../../../Temp~/ReviewFixes20261005/Run3/ToggleVariants/Summary.json), [source hashes](../../../Temp~/ReviewFixes20261005/Run3/SourceHashesAfter.json). `Temp~` игнорируется Git/Unity; эти ссылки действуют в проверенном рабочем проекте, не в опубликованном пакете. Машинные артефакты Run1/Run2 сохранены как история, но не подтверждают финальный код.

## Что не проверялось выполнением

**Testing disabled — skipped:** тестер отключён в AGENTS.md. Не запускались Unity Test Framework, Unity Editor, Android export/Gradle build, устройство, Xcode/Objective-C compilation/linking и IL2CPP. C#-вариант iOS использовал доступные managed engine references Android Player с iOS defines; это проверка C# синтаксиса/типов bridge, а не iOS player build. Native ABI сопоставлен статически.

Внешние engine/package references взяты из существующего Bee окружения. Editor-варианты без defines сохраняют DLL references и доказывают доступность always-compiled helper, но не фактическую очистку Player dependencies или lifecycle toggles. GUID двух новых Editor `.cs.meta` уникальны; legacy video-файлы отсутствуют в diff. Окончательная приёмка на consumer должна включать сценарии каждого ID выше.

## Изменённые файлы

| Файл/группа | Назначение |
|---|---|
| `Editor/SdkModulesSettings/AndroidGradleToolchain.cs` | Завершающий callback, wrapper и свойства Gradle. |
| `Editor/SdkModulesSettings/AndroidToolchainSettings.cs`, `AndroidToolchainInstaller.cs` | Единые URL/checksum дистрибутива. |
| `Editor/SdkDependencies/AppLovinPackageInstaller.cs` | Корректная вставка и поиск scoped registry. |
| `Editor/SdkDependencies/ManifestJson.cs` + `.meta` | Новый parser/validator manifest. |
| `Editor/ConditionalCompilation/AppMetricaAppHudDependencies.cs` + `.meta` | Always-compiled AppHud ownership/synchronization/validation. |
| `Editor/ConditionalCompilation/EdmDependencyGenerator.cs`, `DisabledModuleBuildGuard.cs` | Включение helper в генерацию и preflight. |
| `Editor/Modules/Appmetrica/Editor/AppMetricaResolver.cs` | Делегирование AppHud общей реализации. |
| `Runtime/Modules/Analytics/AnalyticsEventQueue.cs` | Приоритеты и квота очереди, замена/проверка точного payload. |
| `Runtime/Modules/Analytics/AnalyticsModule.cs`, `DeviceIdProvider.cs` | Durable click, fresh identity, dedup, lifecycle resume. |
| `Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Banner/CrossPromoBanner.cs` | Общая видимость CP/MAX и отдельная presentation group. |
| `Runtime/Modules/Cross-Promo/VideoPlayer/CrossPromoExoNativeOverlay.cs` | Независимый backend/external click tracking. |
| `Runtime/Modules/Adjust/Adjust/Scripts/AdjustiOS.cs`, `Native/iOS/AdjustUnity.h`, `AdjustUnity.mm` | Request ID в согласованном managed/native bridge. |
| `Runtime/Modules/InAppPurchase/Core/IapRetryScheduler.cs`, `InAppPurchaseModule.cs` | Deadlines, активация, ответы и reentrant Restore. |
| `Runtime/Modules/InternetConnection/InternetConnectionModule.cs` | Владение timeScale-паузой. |
| `Documentation~/ANDROID-DEPENDENCIES.md`, `Documentation~/README.md`, `CHANGELOG.md` | Миграция AdMob и новые контракты поведения. |
| `Runtime/Modules/Adjust/Adjust/README.md` | Сопровождение локального iOS vendor patch. |
| `Documentation~/Reviews/2026-10-05/FIXES.md`, корневой `REPORT.md` | Подробный результат и краткая запись с сохранением прежней истории. |

## Как принять результат

В consumer сохранить SDK Settings, проверить Android App ID и сгенерированные зависимости. Выполнить Android export с окончательным wrapper и сборку; затем проверить баннеры, отказ backend/перезапуск, сброс Fire ID, IAP deactivate/resume и pause controller по указанным сценариям. Для поставки на iOS отдельно собрать согласованный bridge в Xcode/IL2CPP и проверить конкурентные getters. Legacy Unity Video Player остаётся вне этой серии исправлений.
