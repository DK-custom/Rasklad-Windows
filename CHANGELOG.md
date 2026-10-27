# Rasklad 1.0.33 — Windows x64

- «Считать правильным» предлагает исходное слово, вариант в другой раскладке и до трёх предложений системного словаря для каждого языка. Выбранное написание сохраняется в пользовательском словаре; повторный выбор удаляет запись.
- Клавиша исправления на выбор: Copilot / Break, левый или правый Alt, левый или правый Ctrl. Одиночное нажатие исправляет слово/выделение; сочетания сохраняют обычное действие. Copilot работает независимо от выбранной клавиши.
- Fn переключает язык ввода даже без активного текстового поля. При нескольких установленных способах ввода используется системный порядок Windows.
- Улучшена защита inline-кода и блоков кода: закрывающая обратная кавычка сохраняется, обычный текст после неё исправляется. Семантический тег code распознаётся через Windows UI Automation и после переходов мышью или стрелками.
- Чтение контекста ограничено текущим полем ввода: история чата не влияет на распознавание кода.
- Убран лишний разделитель над первой секцией меню.

English:
- Accept spelling offers the original word, the other keyboard layout and up to three Windows spelling suggestions per language. Each choice independently adds/removes the selected spelling in the user dictionary.
- Select Copilot / Break, Left/Right Alt or Left/Right Ctrl as the correction key. Standalone modifier taps correct text without breaking normal shortcuts; Copilot remains independent.
- Fn switches input languages even without text-field focus, following the Windows input-method order.
- Inline and rich code protection survives closing backticks and caret navigation. Native Windows UIA code roles are recognized; context reads stay inside the current field.
- Removed the leading tray-menu separator.

Signed with the self-signed Rasklad (DK-custom) certificate. This is not a trusted-publisher certificate; Windows trust stores are not changed. Public certificate and SHA256SUMS.txt are included.

# Rasklad 1.0.32 — Windows x64

- English and Russian UI follows the Windows display language at startup; other languages fall back to English. Menu commands, dictionary window, hints and application messages are localized.
- Русский и английский интерфейс по языку Windows: меню, окно словаря, подсказки и сообщения. Для других языков используется английский.
- English README and Russian README.ru.md, with direct download links and descriptions of the Windows keyboard layout switcher / аналога Punto Switcher.
- Both documentation languages are included in installer, portable and source packages.
- UI tests cover ru-RU, en-US and fallback cultures in light and dark themes.

Signed with the self-signed Rasklad (DK-custom) certificate. Public certificate and SHA256SUMS.txt are included; Windows trust stores are not changed.

## Previous release: 1.0.31

- Встроенная обработка Copilot без AutoHotkey; предотвращено открытие «Пуска».
- Многократная смена слова/выделения и исправление предыдущего слова после пробела по Copilot/Pause.
- Переключение RU ↔ EN одиночной Fn на совместимых ThinkPad (обнаруженный код E0 63), отдельная настройка в меню.
- Удаление одного лишнего пробела перед закрывающими знаками препинания с однократной отменой по Copilot.
- Меню с разделами и разделителями, текущей версией, сохранёнными светлой/тёмной темами и шрифтом 10 pt.
- Переключатель автозагрузки, совместимый с пользовательским ярлыком установщика.
- Проверки Copilot/Fn/пунктуации/автозагрузки, защиты паролей, фокуса и сохранности пользовательских данных.

## Требования и подпись

Windows 10/11 x64, .NET Framework 4.8+, средства правописания ru-RU и en-US. Copilot и Fn включаются в разделе «Клавиши». Fn поддерживается только там, где клавиатура передаёт обнаруженный сигнал в Windows. Приложение не добавляет подчёркивание ошибок в произвольные редакторы.

Установщик и EXE подписаны самоподписанным сертификатом Rasklad (DK-custom), SHA-256; это не сертификат доверенного издателя. Windows может показывать предупреждение. Открытый сертификат и SHA256SUMS.txt приложены; доверенные хранилища автоматически не изменяются. Сборка GitHub Actions без закрытого ключа остаётся неподписанной.

## Предыдущий выпуск: 1.0.30

Первый подготовленный дистрибутив Windows-версии.

- Автоматическое переключение RU ↔ EN со словарями Windows.
- Исправление опечаток, регистра и смешанных слов.
- Pause/Break для выделения, ручной коррекции и отмены.
- Личный словарь, исключения приложений и защита редакторов кода.
- Офлайн IT-словарь FOLDOC и русские термины.
- Подсказки над текстовой кареткой и меню с поддержкой темы Windows.
- Исходный фиолетовый значок Rasklad, отдельный ICO для трея.
- Установщик для текущего пользователя, автозагрузка по выбору и portable ZIP.

## Требования и ограничения

Windows 10/11, x64, .NET Framework 4.8+, средства правописания `ru-RU` и `en-US`. Copilot требует отдельного переназначения в Pause/Break. Приложение не создаёт подчёркивание ошибок в редакторах. Совместимость с каждым приложением не гарантируется; разные DPI нескольких мониторов отдельно не проверялись.

Локальный тестовый дистрибутив подписан самоподписанным сертификатом `Rasklad (DK-custom)` (SHA-256). Он не является сертификатом доверенного издателя; без отдельного доверия Windows может показывать предупреждение. Открытый сертификат включён в пакет, доверенные хранилища установщик не изменяет. Автоматическая сборка без параметра подписи создаёт неподписанные пакеты. Контрольные суммы опубликованы в `SHA256SUMS.txt`.
