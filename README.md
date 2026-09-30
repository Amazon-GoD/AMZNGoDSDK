# AMZN GoD SDK — 1.0.8-beta.1

Бета-релиз от 30 сентября 2026 года, собранный из текущей ветки `safety`.

После публикации ветки пакет можно установить через Unity Package Manager:

```text
https://github.com/Amazon-GoD/AMZNGoDSDK.git#beta
```

Для фиксации этой версии используйте `#v1.0.8-beta.1`. Канал `#Releases`
содержит стабильный релиз. Список изменений: [CHANGELOG.md](CHANGELOG.md).
Требования и установка: [Documentation~/README.md](Documentation~/README.md).

Cross-promo: [настройка мастер JSON и смена JSON без пересборки](Runtime/Modules/Cross-Promo/README.md).

При включении и сохранении модуля AppLovin Google AdMob автоматически устанавливается вместе с обязательными адаптерами MAX.

Android App ID AdMob задаётся в AMZN GoD → SDK Settings → AppLovin MAX → AdMob Android App ID и переносится в MAX при сохранении и перед сборкой. Пустое поле сохраняет ID, заданный в MAX Integration Manager. Android-сборка останавливается, если итоговый App ID отсутствует или имеет неверный формат.
