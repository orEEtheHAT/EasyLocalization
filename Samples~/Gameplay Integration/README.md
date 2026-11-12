# Gameplay Integration

**Compatibility:** EasyBuilding 0.3.0+, EasyInteraction 0.3.0+, EasyLocalization 0.2.0+, EasyResources 0.3.0+, Input System 1.20.0+, uGUI 2.0.0+. No prerequisite sample imports.

Опциональный пример для **EasyResources, EasyBuilding и EasyInteraction** версии 0.3.0+. Установите все три, Input System и uGUI перед импортом. Самостоятельный sample First Run для этого примера не требуется.

Для одной интеграции импортируйте отдельный sample **EasyResources**, **EasyBuilding** или **EasyInteraction**. Их типы ResourceNames, BuildingTranslations и InteractionTranslations требуют только соответствующий игровой пакет. Общий GameplayTranslations и ссылки существующих сцен сохраняются.

1. Импортируйте **Gameplay Integration**.
2. Выполните **Tools > EasyLocalization > Create Gameplay Integration Scene**, затем Play.
3. Переключайте English / Русский. Должны меняться Wood / Дерево, Wall / Стена, причина отказа строительства, Use / Использовать и Busy / Занято в реальном InteractionPresenter.

Мастер создаёт отдельную коллекцию, определения и каталог со стандартными LocalizedString. Значения SO исходных игровых определений не меняются.

`GameplayTranslations.ResourceName(definition)` использует `resource.<ResourceDefinition.Id>`, `BuildingName(definition)` — `building.<BuildingDefinition.Id>`. Причины строительства используют `building.result.<PlacementFailure>`. Взаимодействия используют существующие LabelKey / TextKey, например `interaction.Use` и `interaction.Busy`.

Назначьте GameplayTranslations в **Interaction Presenter > Text Provider** и Presenter в самом адаптере. Каталог загружается при включении и смене языка; после загрузки вызывает публичный `presenter.Refresh()`. Для BuildingPaletteUI этот же компонент реализует `IBuildingMessageResolver` и может быть назначен как Message Provider. Собственный UI подписывается на `LocalizedCatalogCache.Changed` и перечитывает подписи.

Пример EasyInteraction `Samples~/Localization` продолжает работать самостоятельно. Новый адаптер использует тот же публичный `IInteractionTextProvider` и стандартные LocalizedString, добавляя общий каталог и немедленное обновление Presenter. Исходные пакеты не модифицируются; адаптеры находятся в EasyLocalization. Подсказки ввода и окна относятся к адаптерам EasyInput / EasyUI.

`GameplayTranslations.Changed` объединяет обновление языка и данных каталога. Изменяйте данные через `catalog.SetEntries(...)` либо вызывайте `catalog.NotifyChanged()` после прямой правки массива; после изменения таблиц — `Localization.Refresh()`. Для замены используйте `cache.Catalog` и `adapter.Cache`. До готовности/при отсутствии перевода возвращается исходное имя/причина, пустой перевод остаётся пустым.
