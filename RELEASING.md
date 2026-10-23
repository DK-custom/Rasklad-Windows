# Выпуск Windows-версии

Целевой отдельный репозиторий: `DK-custom/Rasklad-Windows`. Локальная подготовка не создаёт репозиторий на GitHub и не публикует релиз.

## Подготовка

1. Обновить `AssemblyFileVersion` в `src/AppBranding.cs` и `CHANGELOG.md`. Последний компонент версии EXE — `0`; имена пакетов используют первые три компонента.
2. Собрать `build-release.ps1 -IsccPath <ISCC.exe>` на Windows x64. Для установщика используется постоянный AppId — не менять между версиями.
3. На тестовом ПК с русскими и английскими средствами правописания закрыть рабочий Rasklad и последовательно выполнить:

   ```powershell
   .\build\Rasklad.exe --self-test
   .\build\Rasklad.exe --dpi-test
   .\build\Rasklad.exe --menu-test
   .\build\Rasklad.exe --hint-test
   .\build\Rasklad.exe --integration-test-apostrophe
   .\build\Rasklad.exe --copilot-test
   .\build\Rasklad.exe --fn-test
   .\build\Rasklad.exe --punctuation-test
   .\build\Rasklad.exe --startup-test
   ```

   Проверять exit code, а не только вывод. UI-тесты требуют интерактивного рабочего стола; фокус не должен перехватываться другими окнами. Не запускать вместе с рабочей копией: обе используют клавиатурные hooks.

4. Проверить установку, повторную установку поверх той же версии, запуск, ярлыки и удаление. Файлы настроек/словаря не должны перезаписываться или удаляться. Автозагрузка и рабочий стол не должны изменяться при выключенных задачах.
5. Проверить `SHA256SUMS.txt` и состав обоих ZIP: никаких личных словарей, настроек, журналов, токенов, старых EXE и исследованных сторонних программ.
6. Для подписанной сборки передать `-SigningCertificateThumbprint` в `build-release.ps1`: EXE подписывается до упаковки, установщик — после компиляции, хеши вычисляются в конце. Самоподписанный сертификат явно отмечать как недоверенный; CI без ключа выпускает неподписанный пакет. Позже можно использовать сертификат доверенного издателя. Закрытые ключи в Git и ZIP не включать.

## Текущий самоподписанный сертификат

`CN=Rasklad (DK-custom)`, RSA 3072 / SHA-256, действует до 2028-10-22. Thumbprint: `20B9A56897C5750DC0484DBD0AAC22F08A16E15B`.

Открытая копия: `certificates/Rasklad-selfsigned.cer`. Закрытый ключ неэкспортируемый, находится только в `Cert:\CurrentUser\My` на машине владельца. Доверенные корни и Trusted Publishers не изменяются. Подпись без сервера временных меток; после истечения сертификата потребуется повторная подпись.

```powershell
.\build-release.ps1 -IsccPath '<ISCC.exe>' `
  -SigningCertificateThumbprint '20B9A56897C5750DC0484DBD0AAC22F08A16E15B'
```

## GitHub

Workflow `build.yml` собирает артефакты, но не публикует релизы. Репозиторий `DK-custom/Rasklad-Windows` создан приватным, `main` отправлен. Публикация — отдельное решение владельца; релиз сначала создаётся черновиком:

```powershell
# Репозиторий остаётся приватным по решению владельца.
gh release create v1.0.31 --repo DK-custom/Rasklad-Windows --draft --target main `
  --title 'Rasklad 1.0.31 для Windows' `
  --notes-file dist/Rasklad-1.0.31-release-notes.md `
  dist/Rasklad-1.0.31-windows-x64-Setup.exe `
  dist/Rasklad-1.0.31-windows-x64-portable.zip `
  dist/Rasklad-1.0.31-source.zip dist/Rasklad-signing.cer dist/SHA256SUMS.txt
```

`dist` должен быть собран из того же коммита, что отправлен в `main` (`git status` чистый).

Установщик не мигрирует автоматически файлы из произвольных старых папок и не перенастраивает чужие задачи Copilot/Punto Switcher. Пользователь закрывает старую копию и переносит свои файлы явно.
