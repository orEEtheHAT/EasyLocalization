# Названия ресурсов

**Compatibility:** EasyLocalization 0.2.0+, EasyResources 0.3.0+. No prerequisite sample imports.

Нужны EasyLocalization и EasyResources. В Package Manager импортируйте только sample **EasyResources**.

1. Создайте **Create > EasyLocalization > Localized Catalog**. Добавьте строку с ID `resource.` + `ResourceDefinition.Id`; в Text выберите стандартную таблицу и запись Unity Localization.
2. На объект добавьте **Resource Names**. Автоматически добавленный **Localized Catalog Cache** свяжите с каталогом.
3. Читайте `resourceNames.GetName(definition)`. Подпишитесь на `resourceNames.Changed` в OnEnable, отпишитесь в OnDisable и перечитывайте имя по этому событию. Оно приходит после смены языка и изменения каталога, без Update/polling.

До готовности таблицы или при отсутствии перевода используется `definition.DisplayName`. Пустой перевод остаётся пустым. ID определения и ID записи таблицы не зависят от отображаемого имени.

В runtime меняйте список через `catalog.SetEntries(...)`, после правок массива вызывайте `catalog.NotifyChanged()`. Для замены каталога используйте `cache.Catalog`; для замены cache — `resourceNames.Cache`. После изменения самих таблиц вызывайте `Localization.Refresh()`. Для изменения DisplayName определения уведомите своё представление или вызовите `resourceNames.Refresh()`.
