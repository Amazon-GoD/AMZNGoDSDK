# AMZN GoD SDK for Unity — 1.0.9-beta.1

Бета-релиз от 20 октября 2026 года, собранный из ветки `safety`.

Установка через Unity Package Manager:

```text
https://github.com/Amazon-GoD/AMZNGoDSDK.git#beta
```

Для фиксации этой версии используйте `#v1.0.9-beta.1`. Канал `#Releases`
содержит стабильный релиз 1.0.8. Для приватного репозитория необходим
настроенный доступ к GitHub.

[Изменения 1.0.9-beta.1](CHANGELOG.md) · [Установка и интеграция](Documentation~/README.md)
· [Android-зависимости и миграция](Documentation~/ANDROID-DEPENDENCIES.md)

**Перед выпуском игры на 1.0.9 обновите бэкенд.** Текущий production-бэкенд
отклоняет пакетные события баннеров (`placement: "banner"`, `n`,
`mediation_*`) с HTTP 400, и SDK их отбрасывает. Подробности — раздел
Upgrade notes в CHANGELOG.

В beta.1: показы баннеров Cross-Promo и MAX отправляются пакетами, во всех
событиях есть `sdk_version` и `app_version`, недоставленные события баннеров
повторяются раз в минуту, debug APK собирается одной кнопкой в SDK Settings.
Исправлены повторный `cp_click` при двойном нажатии, `NaN` в revenue и пропуск
загрузки Remote Config на Android.

Unity 2022.3 LTS; при включённом MAX минимальная Android API — 24.
Подробные результаты ревью и границы проверки:
[отчёт об исправлениях](Documentation~/Reviews/2026-10-05/FIXES.md).

Cross-promo: [настройка мастер JSON и смена JSON без пересборки](Runtime/Modules/Cross-Promo/README.md).

При включении и сохранении модуля AppLovin Google AdMob автоматически устанавливается вместе с обязательными адаптерами MAX.

Android App ID AdMob задаётся в AMZN GoD → SDK Settings → AppLovin MAX → AdMob Android App ID и переносится в MAX при сохранении и перед сборкой. Пустое поле сохраняет ID, заданный в MAX Integration Manager. Android-сборка останавливается, если итоговый App ID отсутствует или имеет неверный формат.
