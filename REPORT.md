# Dev Crew report

**Дата:** 2026-09-23
**Задача:** AD Placements AppLovin через SDK Settings.

## Изменения

В SDK Settings > AppLovin добавлен блок AD Placements с отдельными Interstitial Placement и Rewarded Placement. Поля InterstitialAdPlacement и RewardedAdPlacement сохраняются в JSON, восстанавливаются в окне и передаются через Core в AppLovinModule.

MAX, события аналитики, отчёты о выручке, backend, no-fill и выдача награды используют настроенные названия. Выбор interstitial/rewarded-событий и AppMetrica AdType теперь зависит от явного isRewarded, поэтому произвольные, совпадающие или переставленные имена плейсментов не меняют формат рекламы.

Для отсутствующих, пустых и пробельных значений сохранены прежние defaults interstitial/rewarded. У непустых названий удаляются пробелы по краям. Прежний Construct с пятью аргументами сохранён. Обновлены CHANGELOG и Documentation~/README.md.

## Проверки

- Ревью: approved=true, issues=[].
- Компиляция штатным Roslyn Unity 2022.3.60f1 по реальным Unity response files успешна: Runtime, AppLovin, Core, Editor с включённым AppLovin; Runtime, Core, Editor с выключенным AppLovin.
- Только прежние предупреждения: CS0618 для SetSdkKey и два CS0168 в неизменённых строках.
- git diff --check с core.whitespace=cr-at-eol пройден; сохранены исходные окончания строк.
- Проверочные артефакты: Temp~/AppLovinPlacements (игнорируются git и Unity).

Unity Test Framework и Tester отключены в AGENTS.md и не запускались. Реальные показы на устройстве не проверялись.

## Git

Подготовлено в tmp/applovin-ad-placements от safety для локального commit и merge в safety. Существующие пользовательские изменения AppLovin asmdef и crosspromo-config.json в commit не входят. Push не выполняется.
