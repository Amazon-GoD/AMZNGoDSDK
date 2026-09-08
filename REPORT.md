# Dev Crew report

**Date:** 2026-09-08
**Task:** Порядок видео cross-promo через position в JSON с обратной совместимостью.

## What was done

Добавлено необязательное целое `position` с нумерацией от 1. Указанные места закрепляются; свободные заполняются по исходным весам остальных креативов. Без положительных позиций работает прежняя схема.

Обработаны частичные позиции, дубликаты (первый объект JSON владеет слотом), лимиты, фильтры, кулдауны и разреженные позиции вплоть до int.MaxValue. Предзагрузка не расходует слот. Для загрузки баннеров используется снимок списка.

## Architecture and files

- `Runtime/Modules/Cross-Promo/CrossPromoPositionRotation.cs` и `.meta`: исходные слоты, стабильный Peek, однократный Commit.
- `Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Scripts/CrossPromoConfigurationManager.cs`: JSON position, копирование идентификаторов/расположения, сохранение весов.
- `Runtime/Modules/Cross-Promo/CrossPromoModule.cs`: единый выбор для preload и показа.
- `Runtime/Modules/Cross-Promo/VideoPlayer/CrossPromoExoNativeOverlay.cs` и `CrossPromoVideoOverlay.cs`: callback в существующей точке учёта показа.
- `Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Banner/CrossPromoBanner.cs`: снимок списка при загрузке спрайтов.
- `Runtime/Modules/Cross-Promo/README.md`: правила, ограничения и пример смешанного JSON.

Пути относительно Assets/AMZNGoDSDK.

## Review and verification

Две формальные итерации ревью; final approved, issues=[]. Исправлены замечания по перераспределению весов и изменению списка во время загрузки баннеров.

Tester и Unity Test Framework отключены по AGENTS.md и не запускались. Выполнены одноразовые проверки:

- Полный Runtime с Cross-promo: штатный Roslyn Unity 2022.3.60f1, exit 0. Только прежнее CS0414; базовая компиляция давала то же предупреждение.
- Изолированный Unity executeMethod: итоговые 35/35 проверок, 0 ошибок, exit 0. Реальные manager/resolver/rotation/cooldown; SDK core/AppChecker заменены заглушками. Хеши проверенных исходников совпадают с итоговыми файлами SDK.
- Дополнительная проверка тех же исходников в .NET 6 с заглушками Unity API: 35/35, exit 0.
- git diff --check с cr-at-eol: успешно.

Проверены чтение JSON, отсутствие/нулевые/отрицательные позиции, Copy, полный и смешанный порядок, повторы круга, дубликаты, исчерпание заполнителей, предзагрузка/повторные и устаревшие токены, лимиты и отключённая медиация, фильтр установленного приложения, пропуски/int.MaxValue и сохранение пропорций весов. Один повтор Unity задержался на IL Post Processor; запуск в чистом временном проекте завершился успешно.

Артефакты проверок находятся в Temp~/CrossPromoPositionValidation и в коммит не включаются.

## Limitations and usage

Курсор хранится только в памяти и начинается заново с новым конфигом/сессией. Пустые слоты без доступных заполнителей пропускаются. Закреплённые видео следуют очереди независимо от кулдауна; кулдаун заполнителей применяется внутри их пула.

Явный показ переданного PromoConfiguration обходит очередь. Баннеры сохраняют порядок массива JSON. UnityVideoPlayer расходует слот при запуске показа согласно существующему учёту; ExoPlayer — на первом кадре. Воспроизведение на устройстве и загрузка по HTTP в этой проверке не проверялись.

Для полного порядка назначьте объектам Videos позиции 1, 2, ..., N. Для частичного порядка задайте position только нужным креативам; остальные заполнят свободные места по Weight. Отсутствие, 0 или отрицательный position сохраняют выбор без закрепления. Пример находится в README Cross-promo.

## Git

Изменения подготовлены в tmp/cross-promo-position, созданной от safety. Перенос в safety выполняется обычным merge; push не выполняется. Посторонние изменения модулей и asmdef не входят в эту задачу.
