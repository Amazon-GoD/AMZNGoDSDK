# Dev Crew report

**Дата:** 2026-09-22
**Задача:** JSON-креатив вместо неготового AppLovin; пользователь подтвердил показ сверх cap.

## Поведение

ShowVideoPromo, ShowInterstitial и ShowRewarded сначала выбирают Cross-Promo в пределах cap, затем соответствующий формат MAX. Если MAX не принимает запрос из-за неготовности, запускается JSON-креатив сверх cap. Счётчики сохраняются; готовый MAX получает приоритет на следующем запросе после исчерпания обычного JSON-пула.

Полный пул сохраняется до удаления исчерпанных креативов. Fallback использует прежние позиции, веса, кулдауны, фильтры собственной/установленных игр и общую очередь. Предзагрузка готовит резервный JSON-креатив. Награда, CTA и завершение проходят через существующий путь показа.

Активный показ MAX блокирует JSON-fallback повторного запроса. Состояние показа снимается до пользовательских callbacks при закрытии/ошибке и при Cleanup. Асинхронный display_failed дополнительную рекламу не запускает.

## Файлы

Все пути относительно Assets/AMZNGoDSDK:

- Runtime/Core/AmznGoDSDKCore.cs: маршрутизация и проверка активного MAX.
- Runtime/Modules/AppLovin/AppLovinModule.cs: состояние активного показа; AppLovinAnalytics.cs: актуализированные комментарии.
- Runtime/Modules/Cross-Promo/CrossPromoModule.cs: fallback и предзагрузка.
- Runtime/Modules/Cross-Promo/CrossPromoPositionRotation.cs: явный обход cap при сохранении очереди.
- Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Scripts/CrossPromoConfigurationManager.cs: сохранение полного пула и фильтрация для обычного/fallback выбора.
- Runtime/Modules/Cross-Promo/README.md: правила нового поведения.

## Проверки

Ревью: обнаруженный показ JSON поверх активного MAX исправлен; повторное ревью одобрено, замечаний нет.

Компиляция отдельных AppLovin/CrossPromo/Core штатным Roslyn Unity 2022.3.60f1 успешна для четырёх комбинаций: оба модуля, только Cross-Promo, только AppLovin, ни один. Использована существующая MAX DLL из кэша Android-сборки. Предупреждения CS0618 (SetSdkKey) и CS0414 (_firstWarmupTriggered) совпадают с исходной версией; USG0001 — информационное сообщение изолированного запуска генератора.

Одноразовые проверки реальных исходников вне Editor с заглушками Unity/MAX: выбор креативов — 19/19; активный показ, закрытие, ошибки, порядок награды/callbacks и Cleanup — 22/22. Проверены восстановление capped-пула, исходные веса, кулдауны, Copy, self/installed-фильтры, общий курсор позиций и выключенный MAX. Артефакты находятся в игнорируемой Temp~/AppLovinJsonFallback.

git diff --check с cr-at-eol пройден. Tester и Unity Test Framework отключены и не запускались. Editor этого проекта не был подключён; доступный MCP относится к другому проекту. Воспроизведение рекламы на устройстве и реальная сеть MAX не проверялись.

## Git

Изменения подготовлены в tmp/applovin-json-fallback, созданной от safety. После завершения выполняется локальный commit и обычный merge в safety; push не выполняется.
