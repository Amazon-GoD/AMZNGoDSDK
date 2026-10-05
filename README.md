# AMZN GoD SDK for Unity — 1.0.8

Стабильная версия от 5 октября 2026 года. В Unity Package Manager можно
закрепить пакет ссылкой:

`https://github.com/Amazon-GoD/AMZNGoDSDK.git#v1.0.8`

Ветка `#Releases` содержит последний стабильный релиз. Для приватного
репозитория необходим настроенный доступ к GitHub.

[Изменения 1.0.8](CHANGELOG.md) · [Установка и интеграция](Documentation~/README.md)
· [Android-зависимости и миграция](Documentation~/ANDROID-DEPENDENCIES.md)

Unity 2022.3 LTS; при включённом MAX минимальная Android API — 24.
Подробные результаты ревью и границы проверки:
[отчёт об исправлениях](Documentation~/Reviews/2026-10-05/FIXES.md).

Cross-promo: [настройка мастер JSON и смена JSON без пересборки](Runtime/Modules/Cross-Promo/README.md).

При включении и сохранении модуля AppLovin Google AdMob автоматически устанавливается вместе с обязательными адаптерами MAX.

Android App ID AdMob задаётся в AMZN GoD → SDK Settings → AppLovin MAX → AdMob Android App ID и переносится в MAX при сохранении и перед сборкой. Пустое поле сохраняет ID, заданный в MAX Integration Manager. Android-сборка останавливается, если итоговый App ID отсутствует или имеет неверный формат.
