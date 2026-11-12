# Сообщения взаимодействия

**Compatibility:** EasyInteraction 0.3.0+, EasyLocalization 0.2.0+. No prerequisite sample imports.

Нужны EasyLocalization и EasyInteraction. В Package Manager импортируйте только sample **EasyInteraction**.

1. Создайте **Localized Catalog**. ID должен совпадать с ключом, который передаёт EasyInteraction (например `interaction.Busy`), Text — стандартная ссылка на запись Unity Localization. Регистр важен.
2. Добавьте **Interaction Translations**, назначьте каталог в автоматически добавленном **Localized Catalog Cache**.
3. В **Interaction Presenter > Text Provider** назначьте этот компонент. Его поле **Presenter** свяжите с тем же presenter — видимая подсказка обновится сразу после готовности перевода.

`Translate(key, fallback)` реализует `IInteractionTextProvider`. До готовности или при отсутствующем переводе возвращается переданный fallback. Для другого UI подпишитесь на `interactionTranslations.Changed`, отпишитесь в OnDisable.

`catalog.SetEntries(...)`, `catalog.NotifyChanged()`, `cache.Catalog` и `Localization.Refresh()` идут через единый Changed. ID не зависит от локализованной подписи. Стандартный пустой перевод остаётся пустым. Адаптер не меняет источник игрового ввода.
