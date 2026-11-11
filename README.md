# AMZN GoD SDK for Unity — 1.0.13

Стабильная версия от 11 ноября 2026 года. В Unity Package Manager можно
закрепить пакет ссылкой:

`https://github.com/Amazon-GoD/AMZNGoDSDK.git#v1.0.13`

Ветка `#Releases` содержит последний стабильный релиз. Репозиторий
публичный: ключи и конфиги игр (`google-services.json`, `amzn_god_sdk.json`,
`AppLovinSettings.asset`) в него не кладутся.

[Изменения 1.0.13](CHANGELOG.md) · [Установка и интеграция](Documentation~/README.md)
· [Android-зависимости и миграция](Documentation~/ANDROID-DEPENDENCIES.md)

Версия 1.0.13 добавляет отключение отдельного провайдера баннера
(`SetBannerProviderEnabled`: только кросс-промо или только AppLovin MAX).
Версия 1.0.12 починила скрытие баннера кросс-промо при замене на баннер MAX и
добавила управление баннером из `AmznGoDSDKCore` (включение, запрет замены на
MAX, позиция MAX-баннера), а также настройку времени появления крестика на
баннере подписки. Изменений бэкенда нет. При обновлении с 1.0.10 и
ниже прочитайте Upgrade notes 1.0.11 в [CHANGELOG](CHANGELOG.md): бэкенд должен
принимать профиль игры в first_open.

Unity 2022.3 LTS; при включённом MAX минимальная Android API — 24.

Cross-promo: [настройка мастер JSON и смена JSON без пересборки](Runtime/Modules/Cross-Promo/README.md).

При включении и сохранении модуля AppLovin Google AdMob автоматически устанавливается вместе с обязательными адаптерами MAX.

Android App ID AdMob задаётся в AMZN GoD → SDK Settings → AppLovin MAX → AdMob Android App ID и переносится в MAX при сохранении и перед сборкой. Пустое поле сохраняет ID, заданный в MAX Integration Manager. Android-сборка останавливается, если итоговый App ID отсутствует или имеет неверный формат.
