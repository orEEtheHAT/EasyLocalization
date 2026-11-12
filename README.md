# EasyLocalization 0.2.0

Настройка языков, короткое runtime API и обмен переводами с Google Sheets поверх **Unity Localization**. Используются стандартные Locale, String Table Collection, LocalizedString, LocalizedAsset и Smart Strings. Игра работает с подготовленными таблицами; Google нужен только редактору.

## Установка и первый запуск

1. Unity **6.3**: **Window > Package Manager > + > Install package from disk**, выберите `package.json`. Зависимости: Localization **1.5.13**, Addressables **2.9.1**, JSON Serialize module.
2. Откройте **Tools > EasyLocalization > Translations > First setup**. Primary language: `en`; Additional codes: `ru`. Назначьте существующую коллекцию или введите имя новой. Нажмите **Create / connect languages and collection**.
3. Через **Open Unity string tables** добавьте ключ и переводы. Строки, созданные ранее, и ссылки на них сохраняются.
4. Для готового UI установите **Input System 1.20.0** и **uGUI 2.0.0**, включите **Project Settings > Player > Active Input Handling = Input System Package (New)** и импортируйте sample **First Run**. Выполните **Tools > EasyLocalization > Create First Run Scene**, нажмите Play. Кнопки English / Русский меняют язык, `+1` обновляет параметр строки.

Для базового API дополнительные Easy-пакеты, Input System и компоненты сцены не нужны. SO `EasyLocalizationConfig` содержит исходную конфигурацию; мастер создаёт единственный автоматически загружаемый asset в папке Resources. Существующая конфигурация повторным запуском мастера не заменяется. Основной язык существующего проекта изменяется через его конфигурацию, а не повторной настройкой мастера.

## Google Sheets: простой старт по ссылке

**Без Google Cloud и OAuth, только получение переводов.**

1. В тестовой таблице задайте заголовки `ID`, `Key`, `en`, `ru`. ID храните как **обычный текст**, не как число или формулу. Создавайте исходные строки в Unity; копируйте их ID без изменения.
2. В Google Sheets выберите **Настройки доступа > Все, у кого есть ссылка > Читатель**. Эти переводы становятся доступны по ссылке.
3. В окне EasyLocalization выберите **Connection = Public link: download only**. Вставьте обычную ссылку на вкладку, включая `gid`, и нажмите **Connect this URL**.
4. Нажмите **Preview Google → Unity**, проверьте строки и **Apply exactly this preview**. Для других колонок откройте Advanced. Опубликованный CSV также можно назначить в поле Published CSV URL.

CSV не передаёт примечания Google или Smart-флаги: существующие Unity Smart-флаги сохраняются. Для новой параметризованной строки сначала включите Smart в Unity. Пустая ячейка — пустой перевод; отсутствующая строка сама по себе ничего не удаляет.

## Полный обмен: получение и отправка

1. В Google Cloud создайте проект, включите **Google Sheets API**. Настройте Google Auth Platform; в режиме Testing добавьте свой аккаунт как test user. Создайте OAuth client типа **Desktop app** и скачайте JSON.
2. В **Google account > Import OAuth JSON** импортируйте JSON, затем **Connect Google account** и завершите вход в браузере. Можно скопировать настройки уже существующего Unity Sheets Service Provider кнопкой **Copy existing credentials to local storage**.
3. Выберите **Google account: download + send**, вставьте ссылку на нужную вкладку и нажмите **Connect this URL**. Аккаунт должен иметь право редактировать таблицу.
4. **Preview Unity → Google** готовит отправку. **Preview Google → Unity** готовит получение. Ни одна из этих кнопок не применяет данные. Выберите решения конфликтов и нажмите **Apply exactly this preview**.

Учётные данные и OAuth-токены хранятся только в `Library/EasyLocalization`, вне Assets и сборки. Для репозитория проекта исключите `Library/`, как в стандартном Unity `.gitignore`. Mapping SO не содержит секретов. Импорт существующего провайдера не очищает его исходный asset: не публикуйте asset с Client Secret.

Для каждой коллекции создаётся отдельный **Google Sheet Mapping**. Переключение — **Saved collections**. Через Advanced можно перенести стандартные Key/Locale columns из официального Google Sheets Extension; его исходные настройки не меняются. Не запускайте встроенный Push/Pull и EasyLocalization одновременно для одной вкладки.

## Runtime API

```csharp
using EasyLocalization;
using UnityEngine.Localization;

await Localization.InitializeAsync();
await Localization.SetLanguageAsync("ru");
string caption = await Localization.TextAsync(myLocalizedString, new object[] { 12 });

Localization.Changed += RefreshMyView; // отписаться в OnDisable: -= RefreshMyView
LanguageState saved = Localization.CaptureState(); // сериализует ваша система сохранений
await Localization.RestoreStateAsync(saved);
```

`Changed` объединяет готовность настроек, смену языка и `Localization.Refresh()` после обновления данных. `LanguageChanged` также сохранён. Отписывайтесь от событий в OnDisable/Dispose. Вызовы выполняются на главном потоке. `TextAsync` самостоятельно ждёт инициализацию и возвращает актуальный язык при смене языка во время загрузки. Последний запрос `SetLanguageAsync` имеет приоритет. Пользовательский выбор не записывается в SO или PlayerPrefs.

При внешней замене `LocalizationSettings.Instance` вызывайте `await Localization.ReinitializeAsync(config)`. `Localization.Reset()` отменяет старые запросы и отсоединяет прежний settings, сохраняя подписчиков. UI должен бесшумно обрабатывать `OperationCanceledException` при завершении своего lifecycle. Подробнее — [RuntimeIntegration.md](Documentation~/RuntimeIntegration.md).

```csharp
// Компонент Localized Text: Text Changed подключается к setter вашего UI.
localizedText.SetArguments(playerName, score);
string title = await Localization.TextAsync(titleReference, fallback: "Unknown item");
```

Неизвестный язык выбирает `fallbackLanguage`, затем первый доступный Locale. Отсутствующий перевод ищется в настроенном fallback-языке; затем возвращается переданный `fallback` или `[entry]`. Существующий пустой перевод остаётся пустым. Для стандартных реактивных переменных можно использовать штатный LocalizeStringEvent; для ресурсов — штатный `LocalizedAsset<T>` и компоненты Unity Localization.

## Дополнительно

- [Правила синхронизации и восстановления](Documentation~/Synchronization.md).
- [Самостоятельный пример и полный цикл Google](Samples~/First%20Run/README.md).
- [Опциональная интеграция с игровыми пакетами](Samples~/Gameplay%20Integration/README.md).
- Независимые samples: [EasyResources](Samples~/EasyResources/README.md), [EasyBuilding](Samples~/EasyBuilding/README.md), [EasyInteraction](Samples~/EasyInteraction/README.md).
- [Жизненный цикл, обновление данных и независимые адаптеры](Documentation~/RuntimeIntegration.md).
- [Проверки и ограничения](Documentation~/Validation.md), [производительность](Documentation~/Performance.md).

EasyInput владеет локализованными подсказками действий, EasyUI — интеграцией текста окон. Они могут использовать этот API и стандартные LocalizedString; обязательных зависимостей от них нет.

## Совместимость Samples

Samples этого выпуска требуют EasyLocalization **0.2.0+**. Core устанавливается без других Easy-пакетов. Установите зависимости только выбранного sample, затем импортируйте его один раз через Package Manager. В таблице указаны поддерживаемые минимумы этой версии адаптеров: у старых выпусков может не быть нужного API. Зависимости Unity из package.json устанавливаются автоматически.

| Sample | Дополнительные требования | Импортировать заранее |
|---|---|---|
| First Run | Input System 1.20.0+, uGUI 2.0.0+ | — |
| EasyResources | EasyResources 0.3.0+ | — |
| EasyBuilding | EasyBuilding 0.3.0+ | — |
| EasyInteraction | EasyInteraction 0.3.0+ | — |
| Gameplay Integration | EasyBuilding 0.3.0+, EasyInteraction 0.3.0+, EasyResources 0.3.0+, Input System 1.20.0+, uGUI 2.0.0+ | — |

Оставляйте одну копию каждой sample-сборки. Существующие импорты могут оставаться в прежней папке Assets: перед повторным импортом сверяйте изменения, сохраняя свои правки и GUID assets/actions.
