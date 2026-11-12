# Проверки runtime-интеграции, 12 ноября 2026

Unity 6000.3.22f1, Localization 1.5.13, Addressables 2.9.1. Версия пакета не изменена. Правки относятся к жизненному циклу локализации и необязательным адаптерам; Google-сеть в этом этапе не использовалась.

## Исправленная причина

Прежние статические initialization/configuration/listening сохранялись после замены `LocalizationSettings.Instance`. Подписка оставалась на прежнем экземпляре, а сброс отписывался через уже новый static accessor. Это переносило config и незавершённые запросы между тестовыми окружениями. Теперь отдельная session хранит конкретный Settings, config, lifetime cancellation и последовательность запросов языка. Reset отсоединяет фактический старый экземпляр. Отменённый старый запрос не публикует результат в новом окружении.

Проверки `LocalizedTextUpdatesByEventsAndLatestArguments` и `NewestRequestWinsWhenOlderLanguageLoadsLast` сохранили исходные проверки значений; добавлен корректный Reset при выходе fixture. Исправление находится в runtime, увеличение ожиданий этих тестов не применялось.

## Результаты

| Проверка | Результат | Отчёт в Audits/EasyLocalization/IntegrationWave1 |
| --- | --- | --- |
| Финальная общая компиляция ядра, независимых samples и совместного примера | Без ошибок | final-compile.json |
| Финальный совместный PlayMode-фильтр Localization | 29/29: 24 runtime-теста, 3 typed adapter fixture, 1 EasyInput и 1 EasyUI; повтор после финальных обновлений соседних пакетов | joint-playmode-final.json |
| Runtime без других Easy-пакетов | 24/24, включая 15 новых lifecycle/cache тестов | clean-playmode.xml |
| Editor в отдельном проекте | 39/39: 32 редакторных + 7 имитаций HTTP | clean-editmode.xml |
| Только EasyLocalization + EasyResources | Установка, компиляция, поведенческая проверка Passed | independent-EasyResources.xml |
| Только EasyLocalization + EasyBuilding | Установка, компиляция, поведенческая проверка Passed | independent-EasyBuilding.xml |
| Только EasyLocalization + EasyInteraction | Установка, компиляция, поведенческая проверка Passed | independent-EasyInteraction.xml |
| Существующая сцена Gameplay в отдельном проекте | 1/1: сериализованная ссылка cache, catalog/presenter и прежние ResourceName/BuildingName/Translate/Resolve сохранены | legacy-scene.xml |

В каждом отдельном adapter-проекте импортирован один sample, создана рабочая конфигурация через публичный `LocalizationSetup.Create` (тот же First setup), проверены имя по стабильному ID, переименование определения, смена языка, изменение данных, отключение/включение, fallback и отписка старого каталога. В CleanStandalone нет других Easy-пакетов. Первый пробный adapter-стенд выявил неполную настройку Addressables и ошибку генерации asmdef тестов; они исправлены в стенде, итоговые XML выше — успешные повторные прогоны.

Все 28 проверяемых исходников совпадают с самостоятельными стендами (`verified-source-parity.json`). Импортированные копии независимых и общего samples сверены с пакетом (`imported-source-parity.json`). После финальных тестов восстановлена исходная `Assets/PackageTests/EasySuite/EasySuiteLab.unity`: одна сцена, dirty=false, Play Mode остановлен, компиляция завершена (`scene-final.json`). Блокировка освобождена, дальнейших операций редактора от EasyLocalization нет. Первый успешный совместный прогон также сохранён в `joint-playmode.json`.

Lifecycle-тесты используют управляемые отложенные загрузки: A→B→C settings, отложенный язык и строка, Reset до завершения, отмена одного клиента во время общей инициализации, старый callback, несколько reinitialize, смена аргументов, disable/re-enable, замена каталога, отсутствие прежнего снимка после смены settings. Явно пустой перевод проверяется отдельно от отсутствующей записи.

## Повторение

Для основных тестов включите пакет в `testables`, установите Unity Test Framework 1.6.0. Запустите EasyLocalization.PlayMode.Tests и EasyLocalization.Editor.Tests; HTTP fixture находится в Tools~/GoogleTransport.Tests и использует имитацию транспорта.

В Games Factory локальные typed fixture находятся в `Assets/PackageTests/EasyLocalization/IntegrationTests`, импортированные samples — в соседнем Samples. Совместный CLI-прогон: `test run --mode playmode --filter Localization --filter-type testName --async-tests true`, с явным project-path и блокировкой по AGENTS.md. Проверочные проекты и логи сохранены в Audits вне Temp.

Общий полный прогон всех Easy-пакетов, сцена EasySuite и её профилирование относятся к координатору. Этот отчёт подтверждает перечисленные подмножества и самостоятельные установки. Старые измерения производительности относятся к исходной 0.1.0, не являются повторным замером обновлённого runtime. API и шаги подключения: [RuntimeIntegration.md](RuntimeIntegration.md).
