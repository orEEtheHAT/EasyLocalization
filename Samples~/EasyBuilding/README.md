# Названия и сообщения строительства

**Compatibility:** EasyBuilding 0.3.0+, EasyLocalization 0.2.0+. No prerequisite sample imports.

Нужны EasyLocalization и EasyBuilding. В Package Manager импортируйте только sample **EasyBuilding**.

1. Создайте **Localized Catalog**. ID имени: `building.` + `BuildingDefinition.Id`. ID сообщения: `building.result.` + значение `PlacementFailure`, например `building.result.CannotAfford`. Text — стандартный LocalizedString со стабильной ссылкой на запись таблицы.
2. Добавьте **Building Translations**, назначьте каталог в автоматически добавленном **Localized Catalog Cache**.
3. UI получает имя через `buildingTranslations.GetName(definition)`, причину через `buildingTranslations.Resolve(result)`. Компонент реализует `IBuildingMessageResolver`; назначьте его в **Building Palette UI > Message Provider** либо передайте своему UI через интерфейс.
4. В OnEnable подпишитесь на `buildingTranslations.Changed`, в OnDisable отпишитесь. Перечитывайте название и сохранённый PlacementResult по этому событию.

Fallback: `definition.Name` / `result.Reason`; до готовности таблиц действует тот же fallback. ID определения не меняется при переименовании. Пустой перевод остаётся пустым.

Изменения данных: `catalog.SetEntries(...)` или `catalog.NotifyChanged()`; замена каталога: `cache.Catalog`; изменение таблиц: `Localization.Refresh()`. Всё обновляет один Changed. Для обновления исходных Name/Reason перечитайте данные или вызовите `buildingTranslations.Refresh()`.
