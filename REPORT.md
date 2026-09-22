# Dev Crew report

**Дата:** 2026-09-22
**Задача:** Ключ AppLovin для сборки и готовое управление Adjust через Firebase при установке SDK.

## Изменения

AppLovin Quality Service читает ключ из AppLovinSettings Integration Manager, тогда как SDK Settings ранее сохранял его только в runtime JSON. Добавлен AppLovinSettingsSynchronizer: непустой ключ переносится через публичный API MAX при сохранении SDK Settings, после загрузки Editor и перед Android/iOS-сборкой. Reflection сохраняет необязательность MAX; пустое поле SDK не удаляет ключ Integration Manager. Quality Service не отключается, ключ не выводится в логи.

Remote Config включён по умолчанию в новых editor/runtime настройках Firebase. Мастер первой установки наследует этот default. Встроенный adjust_enable уже регистрируется до старта Adjust автоматически, поэтому кнопка Add Adjust flag constants заменена описанием. Существующие A/B-тесты и их генератор не менялись; явно сохранённое EnableRemoteConfig=false не мигрируется и сохраняется.

Для отключения Adjust на следующем полном запуске приложения по-прежнему требуется опубликованный строковый параметр adjust_enable=false в Firebase Console и настроенные Firebase/Adjust. При отсутствии удалённого значения действует прежний разрешающий fallback.

В текущем проекте отдельно синхронизирован существующий Assets/MaxSdk/Resources/AppLovinSettings.asset и включён Firebase.EnableRemoteConfig в Assets/Resources/amzn_god_sdk.json. Ключи проверены на равенство без вывода значений; QualityServiceEnabled=true и Adjust.Enabled=false сохранены. Эти настройки проекта находятся вне git-репозитория SDK.

## Проверки

- Ревью обеих частей: approved, issues=[].
- Компиляция AMZNGoD.Runtime и AMZNGoDSDK.Editor штатным Roslyn Unity 2022.3.60f1 успешна. Два прежних CS0168; новых ошибок нет.
- Одноразовые проверки реальных новых исходников с заглушками Unity: 16/16 с MAX, 6/6 без MAX. Проверены defaults, явное отключение RC, пустой список ABTests, приоритет ключа, пустой ключ, идемпотентность, отключённые модули, ожидание compilation/export, Android/iOS prebuild, отсутствие ключа в логах и отсутствие MAX.
- git diff --check с cr-at-eol пройден. Проверочные артефакты: Temp~/AppLovinKeyAdjustDefaults (игнорируются git и Unity).

Unity Test Framework и Tester не запускались по AGENTS.md. Полный Android-билд не перезапускался: через MCP подключён другой Unity-проект. Доступность Quality Service по сети и опубликованный параметр Firebase Console в этой проверке не проверялись.

## Git

Подготовлено в tmp/applovin-build-key-sync от safety для локального commit и merge в safety. Пользовательское изменение Runtime/Modules/AppLovin/AMZNGoDSDK.Module.AppLovin.asmdef не входит в commit. Push не выполняется.
