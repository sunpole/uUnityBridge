# uUnityBridge — история версий и прогресса

**Снимок проверен:** 2 ноября 2026 года. Это **история разработки**, а не онлайн-датчик компьютера. Актуальные публикации, PR и результаты тестов проверяются по ссылкам.
[Машинная история](version.json) · [Наглядная HTML-страница](version.html) · [План и критерии](docs/GOALS_AND_ACCEPTANCE.md) · [Правила проекта](AGENTS.md).

## Событие: ошибки консоли + локальный снимок Unity — 2 ноября 2026

В Draft [PR #30](https://github.com/sunpole/uUnityBridge/pull/30) созданы
кнопки `UNITY · ЛОГИ` и `UNITY · КАДР`. Агент сохраняет ограниченные
фрагменты stderr/stdout Unity CLI и безопасно извлекает ошибки
`Editor.log` только при подтверждении разрешённого проекта.
Текстовые отчёты делятся на **блок Unity CLI** и **блок Unity Editor**,
с редакцией токенов, URL, email и локальных путей. Второе действие
делает локальный снимок строго выбранного окна Unity Editor через
`PrintWindow` и показывает локальный предпросмотр — НЕ отправляет его
в ChatGPT. GPU-рендеринг может выдавать пустое окно, ещё нужна проверка.

На HEAD `e202f4d2816770cd5d248d0ad6e42ff01fc3217a`:
[Desktop CI](https://github.com/sunpole/uUnityBridge/actions/runs/41469010811) PASS,
[Agent CI](https://github.com/sunpole/uUnityBridge/actions/runs/41469011247) PASS,
[Unity contract](https://github.com/sunpole/uUnityBridge/actions/runs/41469010738) PASS.
**На домашнем ПК не тестировалось**; автоматическая защищённая
передача изображения/лога в ChatGPT ещё не реализована.

## Событие: отдельный тестовый режим — 2 ноября 2026

**[Draft PR #30](https://github.com/sunpole/uUnityBridge/pull/30)** получил отдельный
`--serve-qa` Agent и интерфейсный режим `UUB_UNITY_QA=1` с другой
Named Pipe, Mutex и локальным файлом разрешения. В QA отключены
GitHub PATCH/ANS и автообновление Desktop; работающие EXE не заменяются.
Команда создания Unity исправлена: `--path` указывает **родительскую**
папку проекта, что следует из [руководства Unity CLI](https://github.com/Unity-Technologies/skills/blob/main/skills/unity-cli/SKILL.md).
В репозитории есть `scripts/Stage-Unity-QA.ps1`, который скачивает
артефакты только из проверенных успешных CI двух workflow с точным HEAD,
сверяет SHA-256 Desktop и выполняет локальный self-test Agent.
Установку/запуск на пользовательском Windows пока никто не проверил.

На HEAD `481fcfef455e868ae498838bd406a71df3c50749` **3/3 CI PASS**:
[Desktop](https://github.com/sunpole/uUnityBridge/actions/runs/41443979385),
[Agent](https://github.com/sunpole/uUnityBridge/actions/runs/41443979594),
[Unity-контракт](https://github.com/sunpole/uUnityBridge/actions/runs/41443979317).
Это **не** Unity Editor E2E: кнопка пока не нажималась у пользователя.

## Событие: безопасные команды Unity CLI в интерфейсе — 2 ноября 2026

**[Draft PR #30](https://github.com/sunpole/uUnityBridge/pull/30) дополнен реальным исходным кодом**
кнопок `UNITY · ПАПКА / СТАТУС / СОЗДАТЬ / ОТКРЫТЬ`, локального
разрешения на один проект `uUnityDemo`, трёх типизированных действий
`unity_status`, `unity_project_create`, `unity_project_open`.
Desktop общается с агентом по `CurrentUserOnly` Named Pipe; агент
запускает **только официальный Unity CLI**, без PowerShell, с
`--path`/`--project-path` только к локально согласованной папке.
Создание выполняется как локальный 2D-проект `--no-cloud` с Unity Pipeline;
ответ `ACCEPTED` не означает завершённую операцию.

**На ПК установлен Unity Editor 6.6 (6000.6.7f1)**, что подтверждено
скриншотом Unity Hub. Проект `uUnityDemo` ещё не подтверждён.
Новый интерфейс/Agent из PR #30 **НЕ установлен** на ПК; проверка
настоящего Unity CLI/Editor через агент пока отсутствует. GitHub Issues
по-прежнему принимают только старую операцию `status`.

**Windows CI на HEAD PR #30: 3/3 PASS** —
[Agent #41434916126](https://github.com/sunpole/uUnityBridge/actions/runs/41434916126),
[Desktop #41434916270](https://github.com/sunpole/uUnityBridge/actions/runs/41434916270),
[Unity policy #41434916382](https://github.com/sunpole/uUnityBridge/actions/runs/41434916382).
Проверено, что `not_ready` или `ready:false` не дают ложный PASS.
Это **не** реальное тестирование редактора пользователя.

Документ реализации:
[docs/UNITY_AGENT_CLI.md](https://github.com/sunpole/uUnityBridge/blob/feature/unity-mvp-editor-foundation/docs/UNITY_AGENT_CLI.md).
Без подтверждения локального E2E цели не завершены.

## Событие: первый код Unity Editor — 1 ноября 2026

Создан отдельный **[Draft PR #30](https://github.com/sunpole/uUnityBridge/pull/30)** с
UPM-пакетом `com.sunpole.uunitybridge` версии `0.1.0` для **Unity 6.3 LTS**.
Он содержит четыре локальные команды `unity_status`, `create_scene`,
`create_platform`, `save_scene` и меню `Tools → uUnityBridge`.
Контрактный [Windows CI #41339356505](https://github.com/sunpole/uUnityBridge/actions/runs/41339356505)
**PASS**, обычный [Desktop CI #41339356403](https://github.com/sunpole/uUnityBridge/actions/runs/41339356403)
**PASS**. Это проверки исходников и ограничений, **не запуск Unity Editor**.

Пользователь позднее показал установленный **Unity Editor 6.6**; наличие и запуск тестового проекта пока не подтверждены. Поэтому Unity-MVP остаётся `ci_pass_qa_pending`:
настоящая 2D сцена ещё не создана и не показана. Существующие версии Desktop
и Agent не обновлялись, PR #25 для безопасной доставки остаётся отдельным Draft.

Следующий видимый шаг: официально установить Unity Hub → Unity 6.3 LTS,
создать `uUnityDemo` (2D) и проверить пакет из PR #30 в настоящем редакторе.
Не использовать пакет в рабочих проектах до такой проверки.

## Где мы находимся

- **Главная цель:** управлять настоящим Unity Editor и сценами из ChatGPT через ограниченный API uUnityBridge, автоматически видеть статус, тест и по разрешению скриншот окна. Это **ещё не работает**.
- **Реально подтверждено:** ChatGPT → GitHub Issue `status` → Windows Agent → ответ `PASS`. [Пример R000005](https://github.com/sunpole/uUnityBridge/issues/29).
- **Последний стабильный Desktop:** [v0.4.2](https://github.com/sunpole/uUnityBridge/releases/tag/v0.4.2).
- **По диагностике владельца на ПК:** Desktop `0.4.2.0`, Updater `0.4.0.0`, Agent `0.2.1.0`; агент был запущен. После указанной проверки состояние ПК удалённо не подтверждалось.
- **В разработке:** [Draft PR #25](https://github.com/sunpole/uUnityBridge/pull/25), Desktop `0.4.3` и Agent `0.3.0-preview`, безопасная доставка с rollback. Windows CI прошёл, но реальное обновление на ПК и Unity E2E **не проверены**.
- **Unity Editor API и скриншотер:** реализация в исходниках на дату снимка отсутствует. Не путать дизайн в `docs/LOCAL_API_MCP.md` с кодом.

## История — от первого прототипа до сейчас

| Этап / версия | Что реально получено | Степень подтверждения |
| --- | --- | --- |
| `v0.1.0–v0.2.1` | Ранние архивные этапы разработки | Историческая запись; см. [журнал](docs/HISTORICAL_RELEASES.md). Архивные пересборки не обязательно совпадают с исходными EXE |
| `v0.3.0` | Ранний Desktop и механизмы PATCH/ANS | Исторический исходный коммит в [журнале](docs/HISTORICAL_RELEASES.md), не текущая установка |
| `v0.4.0` | Этап отдельного Desktop updater | Исторический выпуск; не приравнивать к установленному updater `0.4.0.0` без проверки файла |
| `v0.4.1` | Предыдущий опубликованный выпуск Desktop | [GitHub Release](https://github.com/sunpole/uUnityBridge/releases/tag/v0.4.1) |
| `v0.4.2` | Стабильная версия Desktop, существующий автоапдейтер | [GitHub Release](https://github.com/sunpole/uUnityBridge/releases/tag/v0.4.2) и пользовательская диагностика |
| Agent `0.2.1` | Фоновый Windows Named Pipe, исходящий GitHub `status` relay | [R000005 / PASS](https://github.com/sunpole/uUnityBridge/issues/29). **Не** Unity API, deploy или screenshot |
| Desktop `0.4.3` + Agent `0.3.0-preview` | В Draft PR реализованы типизированный Release deploy, backup, rollback, GUI bootstrap, isolated tests | [PR #25](https://github.com/sunpole/uUnityBridge/pull/25) и [Desktop CI PASS](https://github.com/sunpole/uUnityBridge/actions/runs/41333118950), [Agent CI PASS](https://github.com/sunpole/uUnityBridge/actions/runs/41333118927). **Не выпущено** |
| `UNITY-MVP` package 0.1.0 (Draft PR #30) | Исходники редакторного пакета и четырёх типизированных команд; Windows contract CI PASS | **Реальный Unity Editor не проверен**, GitHub и локальный Agent ещё не подключены |

Все известные исторические коммиты `v0.1.0–v0.4.2` документированы в [docs/HISTORICAL_RELEASES.md](docs/HISTORICAL_RELEASES.md). Даты отдельных ранних событий не восстанавливались предположительно.

## Реестр наших долгов

| ID | Результат, который должен увидеть пользователь | Статус |
| --- | --- | --- |
| P0009 | Статус с ПК приходит обратно в GitHub | **DONE только для status** |
| P0010 | Проверенный Release доставляется и восстанавливается в разрешённой папке | **CI PASS, E2E не выполнен** |
| UNITY-MVP | Из сообщения в ChatGPT создаётся объект/сцена в реальном Unity | **Код в Draft PR #30, Unity E2E не проверен** |
| P0011 | Стабильный мост ChatGPT ↔ Windows ↔ приложение | **В работе, не завершён** |
| P0012 | Агент обновляется и восстанавливается без терминала | **Частично в PR** |
| P0013 | Видны версии, задачи, ошибки, online/offline в GUI | **Не реализовано** |
| P0014 | Запуск и тесты только разрешённого Unity-проекта | **Не реализовано** |
| P0015 | Безопасный скриншот одного окна Unity и обратная передача | **Не реализовано** |
| P0016 | Один полный цикл: команда → Unity → результат → ChatGPT | **Не реализовано** |

### Правило закрытия

**CI PASS ≠ установлено ≠ пользователь увидел результат ≠ цель DONE.** Для закрытия каждого долга указываются ссылки на PR, CI, версию, фактический E2E, приёмку и ограничения. Если все согласованные цели и патчи действительно закрыты, в чате выдаётся:

> **ТЕКУЩИЙ НАБОР ЦЕЛЕЙ ЗАВЕРШЁН. НУЖНЫ НОВЫЕ ЦЕЛИ.**

После этого предложить пользователю сформулировать следующую цель.
На 1 ноября 2026 настроена **ежедневная условная автоматизация ChatGPT**:
она проверяет GitHub и отправляет уведомление только если все блокирующие
цели независимо подтверждены; отсутствие доказательств = нет уведомления.
Она не пишет код и не изменяет файлы ПК. Агент 0.2.1 не читает эти
Markdown/JSON/HTML файлы автоматически.

## Ближайшая наблюдаемая цель

1. Открыть настоящий Unity Editor с отдельным тестовым проектом (у пользователя, без изменения установленного uUnityBridge).
2. Разработать ограниченный Unity Editor C# модуль с командами `status`, `create_scene`, `create_platform`, `save_scene`; проверить реальные объекты и сохранить сцену.
3. Только затем связать Unity с агентом, GUI uUnityBridge, разрешённым скриншотом и проверенной доставкой.
4. Параллельно довести безопасную приёмку [P0010](https://github.com/sunpole/uUnityBridge/pull/25) в изолированной Windows VM, не ломая рабочую установку.

Источник задач: [P0011 Issue #23](https://github.com/sunpole/uUnityBridge/issues/23), [P0010 QA gate](https://github.com/sunpole/uUnityBridge/blob/feature/p0010-safe-release-deploy/docs/P0010_WINDOWS_QA_RELEASE_GATE.md) и [CONTINUE_HERE.md](CONTINUE_HERE.md).
