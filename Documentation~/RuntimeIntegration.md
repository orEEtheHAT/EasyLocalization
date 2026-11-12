# Runtime-интеграция

Основной сценарий остаётся `await Localization.InitializeAsync()`, `await Localization.SetLanguageAsync("ru")`, `await Localization.TextAsync(reference)`. Все вызовы — с главного потока Unity.

## Настройки и жизненный цикл

Инициализация, конфигурация, подписки и незавершённые запросы привязаны к конкретному `LocalizationSettings.Instance`. При обнаружении другого экземпляра прежний lifecycle прекращается. Новая конфигурация берётся из явного аргумента либо `Resources/EasyLocalizationConfig`; конфигурация прежнего экземпляра не переносится автоматически.

```csharp
LocalizationConfig previousConfig = Localization.Configuration;
Localization.Reset();
// Владелец назначает LocalizationSettings.Instance.
await Localization.ReinitializeAsync(config);
```

- `Reset()` отсоединяется от фактического прежнего экземпляра и отменяет его незавершённые запросы. Сохраняет подписчиков публичных событий. Не уничтожает Unity Settings, не меняет выбранный Locale и не сбрасывает внутренние базы Unity.
- `ReinitializeAsync(LocalizationConfig config = null)` делает Reset и запускает новую инициализацию. Используйте после внешней замены Instance или изменения начальной конфигурации. Сама замена Unity Instance не генерирует событие EasyLocalization, пока не вызван его API.
- `Configuration` возвращает текущий config; полезно для временного окружения тестов.
- Смена settings/reset завершает старые задачи через `OperationCanceledException`. Обработка отмены UI не должна писать ошибку в Console.
- Перед завершением временного окружения отключите/уничтожьте его представления, вызовите Reset, восстановите прежний Instance и дождитесь `ReinitializeAsync(previousConfig)`. Это также важно для штатных компонентов Unity Localization, владеющих собственными подписками.
- `LanguageChanged` совместим с прежним API. При готовности нового settings уведомляет даже при прежнем коде языка.
- Последний запрос языка имеет приоритет; отменённый заранее запрос не вытесняет предыдущий. Отмена ожидающего клиента не отменяет общую инициализацию.

## Один путь обновления представления

`Localization.Changed` сообщает о готовности нового settings, смене языка и явном `Localization.Refresh()` после изменения таблиц. Подписывайтесь в OnEnable и отписывайтесь в OnDisable. `LocalizedText` делает это сам; `RefreshAsync()` позволяет дождаться конкретного обновления. `SetArguments(...)`, Disable и новая загрузка отменяют устаревшие ожидания.

`LocalizedCatalog` связывает стабильный ID определения/сообщения с обычным `LocalizedString`. Данные меняются через `SetEntries(CatalogTranslation[])`; после правки публичного массива вызывайте `NotifyChanged()`. В Play Mode правки каталога в Inspector уведомляют автоматически.

`LocalizedCatalogCache` слушает каталог и Localization, затем публикует целый снимок через `Changed`. `Get(id, fallback)` возвращает перевод или актуальный fallback вызывающего кода. Отсутствующая запись/неназначенная ссылка не подменяется техническим ID; существующий пустой перевод остаётся пустым. Во время смены языка/settings до нового снимка действует fallback. `IsReady` показывает готовность текущего снимка.

Для замены каталога используйте `cache.Catalog = next`. Старое поле `catalog` сохранено; после прямой записи вызовите `Refresh()`. Для ожидания используйте `await cache.RefreshAsync()`. Disable отменяет загрузку и отписывается от фактического старого каталога.

## Независимые адаптеры

В Package Manager импортируйте только нужный sample; обязательные зависимости основного пакета не изменены.

| Sample | Дополнительный пакет | Тип, namespace EasyLocalization.Integrations | API / ID |
| --- | --- | --- | --- |
| EasyResources | EasyResources | ResourceNames | `GetName(ResourceDefinition)`, `resource.{Id}` |
| EasyBuilding | EasyBuilding | BuildingTranslations | `GetName(BuildingDefinition)`, `building.{Id}`; `Resolve(in PlacementResult)`, `building.result.{Failure}` |
| EasyInteraction | EasyInteraction | InteractionTranslations | `Translate(key, fallback)`, исходный ключ сообщения |

Все три наследуют `LocalizedCatalogAdapter`: общий `Changed`, `Cache`, сохранённое поле `cache`, `Translate`, `Refresh`. Компонент автоматически добавляет LocalizedCatalogCache; назначьте каталог и подпишите UI на Changed. BuildingTranslations реализует `IBuildingMessageResolver`; InteractionTranslations — `IInteractionTextProvider`. У последнего назначьте обе ссылки: `presenter.TextProvider = adapter` и `adapter.presenter = presenter`.

Старый sample Gameplay Integration и `GameplayTranslations.ResourceName/BuildingName/Translate/Resolve` сохранены. Сериализованная ссылка cache сохранена в базовом классе. Общий пример всё ещё требует три игровых пакета; отдельные samples позволяют установить одну интеграцию. EasyInput и EasyUI владеют своими адаптерами и используют этот публичный API.

Переводимые названия не являются ID. Переименование definition или ключа таблицы при сохранённом ID записи не меняет связь. Fallback — DisplayName ресурса, Name постройки либо Reason сообщения. Изменения количества/прогресса/других игровых данных обновляют собственные модели; локализация не становится источником игрового состояния.
