# Dev Crew report

**Date:** 2026-09-07
**Task:** Добавить мастер JSON Cross-promo для выбора JSON креативов по package name и смены ссылки при следующем запуске игры без пересборки.

## What was done
- ✅ Добавлен мастер `Packages[{PackageName, ConfigUrl}]` с точным выбором по `Application.identifier`; прямой формат `Weight`/`Videos` сохранён.
- ✅ Встроена загрузка мастер → креативы: один уровень, пять попыток всей цепочки, таймаут запроса 15 секунд и `Cache-Control: no-cache`.
- ✅ Добавлены ошибки маршрутизации и формата; существующие веса, лимиты показов и фильтры сохранены.
- ✅ Обновлены инструменты Cross-Promo Caps, подсказки настроек, документация и пример мастер-файла.
- ✅ Завершены ревью, проверки компиляции и одноразовая проверка парсера в Unity.

## Architecture
`CrossPromoConfigResolver` определяет формат по корневым полям исходного JSON, затем использует `JsonUtility` и возвращает конфиг либо URL нужной игры. Это учитывает поведение Unity 2022.3.60f1, создающей пустые списки для отсутствующих полей.
Рантайм `CrossPromoConfigurationManager` и редакторский `CrossPromoCapDebug` используют общий resolver; сетевые запросы выполняют собственными средствами. Успешно загруженный конфиг действует до конца сессии; следующий запуск заново читает мастер.

## Files created/modified
- `Runtime/Modules/Cross-Promo/CrossPromoConfigResolver.cs` и `.meta` — общий разбор и проверка маршрутов.
- `Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Scripts/CrossPromoConfigurationManager.cs` — загрузка цепочки в рантайме.
- `Editor/SdkModulesSettings/CrossPromoCapDebug.cs` — чтение мастера инструментами счётчиков показов.
- `Editor/SdkModulesSettings/ModulesSettings/CrossPromoSettingData.cs`, `Runtime/DataLoader/ModulesSettings/CrossPromoSettingData.cs` — подсказки поля URL.
- `Editor/Windows/SDKSettingsWindow.cs` — объяснение настройки мастера и момента обновления.
- `README.md` — ссылка на инструкцию Cross-promo.
- `Runtime/Modules/Cross-Promo/README.md` и `.meta` — формат, внедрение, кеширование и диагностика.
- `Runtime/Modules/Cross-Promo/master.example.json` и `.meta` — пример для размещения на сервере. Пути выше относительно `Assets/AMZNGoDSDK`.

## Review results
Финальный вердикт: approved, `issues=[]`; открытых critical/high/medium/low: 0/0/0/0.
Две завершённые итерации: выявленная high-проблема определения формата через списки `JsonUtility` исправлена проверкой корневых полей.

## Tests
Testing disabled — skipped: Tester и Unity Test Framework отключены в `AGENTS.md` и не запускались.
Одноразовая компиляция реальных Runtime и Editor с Cross-promo и без него: 4/4 успешно; новых предупреждений нет, прежние CS0414 и CS0168 сохранены.
Изолированный Unity 2022.3.60f1 batchmode: `ROUTING_SMOKE:29/29 passed, failures=0`, код выхода 0; использованы реальные resolver и manager.

## Known limitations
HTTP-загрузка и полный показ на устройстве не проверялись; в проверке парсера внешние `AppChecker` и `VideoCooldownRegistry` заменены заглушками.
Обновления успешного конфига внутри сессии и вложенные мастера не поддерживаются; существующие повторы после неудачной начальной загрузки сохраняются.
Старым играм нужен один релиз с новым SDK; актуальность ответа зависит также от настроек кеширования сервера/CDN.

## How to use
Разместите `master.example.json` на сервере, заменив примеры своими `PackageName` и полными HTTP(S) URL креативов.
Укажите URL мастера в `AMZN GoD → SDK Settings → Cross Promo → Config URL` и один раз выпустите обновлённую игру.
Для последующих переключений меняйте `ConfigUrl` игры в мастере; настройте проверку актуальности или инвалидацию CDN. Новая ссылка применяется при следующем запуске.
