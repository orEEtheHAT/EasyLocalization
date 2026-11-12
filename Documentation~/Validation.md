# Проверка 0.1.0

Unity **6000.3.22f1**, Localization **1.5.13**, Addressables **2.9.1**, Windows. Проверки выполнены 12 ноября 2026 года. Разработка велась в Games Factory, локальные сцены — `Assets/PackageTests/EasyLocalization`. В публикуемый пакет входят только файлы `Packages/com.oreethehat.easy-localization`.

## Выполнено

Исходная проверка 0.1.0 приведена ниже. Дополнительные runtime-изменения интеграционной волны проверены отдельно в [IntegrationWave1.md](IntegrationWave1.md); версия пакета в этом этапе не менялась.

| Проверка | Результат |
|---|---|
| Импорт Runtime / Editor / Samples в Games Factory | Компиляция без ошибок |
| Editor tests | 32/32: ID, переименование и обмен ключами, metadata, пустые/отсутствующие переводы, конфликты, Smart Strings, устаревший preview, отмена, потерянный ответ, частичный результат, повтор |
| Play Mode tests | 9/9: параметры, языки, state, fallback, пустой перевод, последняя смена языка при отложенной загрузке, событийный текст, отмена |
| Google SDK + имитация HTTP | 7/7 в общем и отдельном чистом редакторе: gzip batch, большие текстовые ID, сохранение notes и чужих колонок, дубли, числовая потеря точности, 429 retry, отмена, отсутствие повтора неопределённой записи и защита от изменения подключения во время запроса |
| Самостоятельная установка | В отдельном чистом проекте, без остальных Easy-пакетов и Input System: 32 Editor + 9 Play Mode пройдены |
| Первый запуск в чистом проекте | После установки заявленных зависимостей sample — Input System 1.20.0 и uGUI 2.0.0 — созданы Locale/таблицы/конфигурация/сцены, Development build успешен, 0 ошибок |
| Standalone runtime | Windows player из чистого проекта: холодный запрос строки с параметром, русский перевод, восстановление языка, недоступный язык, пропущенный ключ, быстрые смены языка — пройдены |
| Совместный пример | en/ru: названия ResourceDefinition и BuildingDefinition по стабильным ID, причина PlacementFailure, LabelKey и TextKey реального InteractionPresenter |
| UI первого примера | Перевод из реального CSV показан в Play Mode по ID 1001; кнопки +1, Capture language и Restore language проверены, используется InputSystemUIInputModule |
| Производительность | 100 / 1000 / 5000 LocalizedText после прогрева; CPU-section, полное время, managed memory и GC через ProfilerRecorder. См. Performance.md |

## Реальная сеть и имитация

На отдельной Google-таблице выполнено реальное получение CSV из Unity Editor по HTTPS **без Google Cloud/OAuth**. После изменения русского перевода и переименования ключа `demo.welcome` в `demo.welcome.synced` повторное получение изменило одну строку и сохранило ID **1001**. Новый перевод проверен в Play Mode и в UI первого примера. Повторный обмен даёт 0 изменений. Google-таблица открыта пользователем по ссылке с правом чтения; разрешение проверено через Drive API. Саму правку перевода в Google выполнил подключённый Google Drive; это не выдаётся за отправку из Unity.

Двусторонняя OAuth-отправка реализована через стандартную авторизацию Unity Localization и Google SDK. Живой OAuth Push/Pull из Unity **не выполнялся**: Desktop OAuth JSON и вход пользователя не были предоставлены; для первой сетевой проверки выбран публичный CSV. Перед выпуском с заявленной поддержкой OAuth нужно выполнить полный цикл из README sample First Run на отдельной тестовой вкладке.

Конфликты, прерывание, потерянный ответ, частичный результат и повторные операции проверены с имитацией сервиса. Google HTTP tests дополнительно проверяют настоящий SDK и транспорт через подставленный HTTP handler; это не обращение к Google и не OAuth-проверка.

## Воспроизведение

1. Для обычных тестов добавьте пакет в `testables` файла `Packages/manifest.json`, установите Unity Test Framework 1.6.0, откройте Test Runner.
2. Запустите `EasyLocalization.Editor.Tests`, `EasyLocalization.Google.Tests` в EditMode и `EasyLocalization.PlayMode.Tests` в PlayMode.
3. Для UI импортируйте соответствующий sample, создайте сцену его пунктом Tools и переключите en/ru.
4. Для реальной Google-проверки используйте шаги [First Run](../Samples~/First%20Run/README.md). Публичный CSV проверяет получение; OAuth — получение и отправку.
5. Для отдельной сборки и измерений используйте [verification tools](../Tools~/README.md).

В Games Factory отчёты сохранены в `Audits/EasyLocalization`: `editor-results.json`, `playmode-results.json`, `google-http-tests-final.json`, `csv-network-final.json`, `gameplay-results-final.txt`, `first-run-ui-check.txt`, `clean-performance.json`. Скриншоты: `first-run-final.png`, `gameplay-final.png`, `translations-window.png`. Чистый проект — `Audits/EasyLocalization/CleanStandalone`, его NUnit XML — `EditMode.xml`, `PlayMode.xml`, `GoogleHttp.xml`.

## Межпакетные изменения и границы

Исходники EasyResources, EasyBuilding и EasyInteraction не изменялись. Использованы актуальные локальные API, включая интерфейсы IInteractionTextProvider / IBuildingMessageResolver и события Presenter. Все новые адаптеры находятся в опциональном sample EasyLocalization. Интеграции EasyInput/EasyUI остаются ответственностью этих пакетов.

Конфликты разрешаются целой строкой. CSV не переносит notes/Smart metadata; новые строки должны иметь ID, Smart настраивается в Unity. Пользовательские Google SheetColumn требуют отдельного адаптера. Проверка Smart Strings проверяет структуру и селекторы, а не все значения аргументов. Между последним чтением и записью Google остаётся окно конкурентного редактирования; подробности в Synchronization.md. На мобильных устройствах и WebGL сборки не проверялись.
