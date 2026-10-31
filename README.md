# uUnityBridge 0.2.1 — Windows desktop PATCH / ANS

**P0002 preview (in feature branch until CI and local QA).** Queue receipts, bounded retries, ANS deduplication and auto-close on confirmed completion. See [P0002 acceptance](docs/P0002_ACCEPTANCE.md), [future MCP/API](docs/LOCAL_API_MCP.md), and [one-time DeepSeek $5 trial](docs/DEEPSEEK_TRIAL.md).

The app still has **no inbound MCP or REST API**, **no executable self-updater**, and **no DeepSeek connection**. Do not confuse SOURCE_UPDATED with an EXE release.

**Предварительная версия.** Исходники подготовлены; компиляция и GUI версии 0.2.0 на Windows должны быть подтверждены локальным тестом. Репозиторий публичный: `sunpole/uUnityBridge`.

## Три кнопки

1. **ИЗ ЧАТА** — получить все открытые PATCH-задания из GitHub Issues, оставить только созданные `sunpole` и по порядку автоматически обработать новые допустимые задания. Обработанные задания повторно не выполняются.
2. **ПЕРЕДАТЬ ЧАТУ** — опубликовать короткий ответ `ANS P0001 — PASS/FAIL` на каждый локально обработанный, но ещё не отправленный патч. Если соединение прервалось, ранее отправленные и сохранённые ответы не публикуются повторно.
3. **СКОПИРОВАТЬ** — скопировать одну строку о последнем опубликованном ANS для вставки в ChatGPT.

Очередь результатов хранится локально в `%LOCALAPPDATA%\uUnityBridge\answers.json` (без токенов и личных путей). Кнопка получения не публикует отчёты, пока пользователь не нажмёт «ПЕРЕДАТЬ ЧАТУ». После ошибки выполнение следующего патча останавливается. Невалидная история останавливает обработку, чтобы не выполнять задания повторно.

## Безопасность

- Только GitHub Issues от `sunpole`, авторизованный `gh` тоже `sunpole`.
- Только `report-only` и `sync-main` до точного SHA в `origin/main` при чистом рабочем дереве и совпадении удалённого репозитория.
- Никакие команды из публичных Issues не запускаются. В публичный ANS отправляются только номер, PASS/FAIL и короткая предопределённая фраза.
- `sync-main` обновляет **исходники**, не заменяет запущенный EXE и не выполняет произвольные сборочные скрипты.
- Нет фонового отслеживания GitHub и нет самообновления бинарного EXE в 0.2.0.

## Сборка

Для локальной сборки нужен Windows 10/11 x64, .NET 8 SDK, Git, GitHub CLI с авторизацией. Пример из корня проекта:

```powershell
dotnet publish ./src/uUnityBridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./dist/win-x64
```

Для обновления установленной 0.1.0 на 0.2.0 используйте поставляемый отдельно `Apply-Update.ps1`: он проверяет чистоту нужных исходников и Git, создаёт резервную копию, собирает EXE и отправляет коммит в публичный `sunpole/uUnityBridge`. Перед публикацией проверьте содержимое ZIP.

## Проверка

1. Запустить GUI и убедиться, что кнопок ровно три.
2. «ИЗ ЧАТА»: получить P0001, проверить результат PASS для `report-only`.
3. Повторно нажать «ИЗ ЧАТА»: повторной обработки не должно быть.
4. «ПЕРЕДАТЬ ЧАТУ»: увидеть ANS P0001 в GitHub Issue.
5. Повторно нажать «ПЕРЕДАТЬ ЧАТУ»: повторного комментария быть не должно.
6. «СКОПИРОВАТЬ»: вставить строку в ChatGPT.
7. Перезапустить GUI: история должна сохраниться и патч не должен примениться вновь.

Workflow GitHub Actions на `main` отдельно проверяет сборку Windows, но CI не заменяет графическое и интеграционное QA.
