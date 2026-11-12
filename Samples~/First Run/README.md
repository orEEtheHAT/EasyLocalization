# First Run

**Compatibility:** EasyLocalization 0.2.0+, Input System 1.20.0+, uGUI 2.0.0+. No prerequisite sample imports.

Нужны Unity 6.3, EasyLocalization, Input System 1.20.0 и uGUI 2.0.0. Установите последние два пакета через Package Manager, включите **Active Input Handling = Input System Package (New)**. Другие Easy-пакеты не требуются.

1. Импортируйте **First Run** из Samples пакета.
2. **Tools > EasyLocalization > Create First Run Scene**. Если у вас есть несохранённая работа, сохраните сцену в диалоге. Будут созданы обычные Locale en/ru, коллекция `EasyLocalization First Run`, mapping, конфигурация и сцена в `Assets/EasyLocalization Example`.
3. Нажмите Play. English / Русский меняют Welcome / Добро пожаловать. Кнопка `+1` меняет Score / Счёт. Capture language и Restore language показывают сохранение стабильного кода без изменения SO.

Повторный запуск мастера сохраняет уже отредактированные переводы и существующую сцену.

## Полный цикл Unity → Google → Unity → игра

1. Подготовьте отдельную пустую Google-таблицу. В Unity откройте **Translations**, выберите mapping примера, подключите OAuth по инструкции основного README. Аккаунт должен иметь доступ на редактирование.
2. Вставьте URL нужной вкладки с gid, нажмите **Connect this URL**. По умолчанию колонки: ID, Key, en, ru.
3. В стандартной коллекции измените строку `demo.welcome`, ID `1001`. Нажмите **Preview Unity → Google**, проверьте Added/Changed и **Apply exactly this preview**. В Google должны появиться три строки.
4. В Google измените русский перевод строки 1001. Можно переименовать её Key, оставив ID прежним, и переставить строки.
5. Нажмите **Preview Google → Unity**, проверьте новое имя/перевод и примените. Если обе стороны менялись, выберите источник явно.
6. Запустите FirstRun, выберите Русский. Новый перевод виден; пример ссылается на **ID 1001**, поэтому переименование ключа его не ломает.

Для старта без OAuth можно заполнить ту же таблицу вручную, открыть её на чтение по ссылке и выбрать **Public link: download only**. Исходные данные:

| ID (текст) | Key | en | ru |
|---|---|---|---|
| 1001 | demo.welcome | Welcome! | Добро пожаловать! |
| 1002 | demo.score | Score: {0} | Счёт: {0} |
| 1003 | demo.language | Language | Язык |

В коллекции строка 1002 уже имеет Smart-флаг. При CSV он сохраняется из Unity. Этот путь проверяет получение; отправка доступна только OAuth-подключению.
