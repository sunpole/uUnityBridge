# uUnityBridge — передача проекта новому чату ChatGPT

**Дата контрольной точки:** 1 ноября 2026 года. **Язык общения и работы:** русский.
**Единый основной документ:** `CONTINUE_HERE.md` в ветке `main` репозитория `sunpole/uUnityBridge`.
**Назначение:** новый чат должен прочитать этот документ и продолжить работу **без Codex**, используя ChatGPT + подключённый GitHub, не заставляя пользователя постоянно копировать команды на ПК.

> ВНИМАНИЕ: это снимок состояния на указанную дату, а не утверждение, что функция уже работает сегодня. Перед новой работой перепроверить GitHub, PR, тесты, Release и ответы агента. Не считать код из draft PR установленным на пользовательский ПК.








## 2026-11-02 — диагностический QA IPC патч, текущий Windows CI 3/3 PASS

**Проверенный Draft PR #30 HEAD:** `67a7c90f92bdb69d08c312299ca4d2e71c9f6f47`. Все три GitHub Actions завершены **success** на точном SHA: [Desktop+Agent #41503208005](https://github.com/sunpole/uUnityBridge/actions/runs/41503208005), [Agent #41503207919](https://github.com/sunpole/uUnityBridge/actions/runs/41503207919), [Unity contract #41503208078](https://github.com/sunpole/uUnityBridge/actions/runs/41503208078). Windows Desktop CI проверил generated launcher PowerShell 5.1, выпустил тестовые артефакты; Agent CI проверил ожидаемый отказ при elevated/без QA флага. **GitHub Windows hosted runner elevated, поэтому полноценный незаблокированный `--serve-qa` и QA Named Pipe health E2E в этом CI не доказаны**. Любой CI PASS здесь не является проверкой на пользовательском ПК.

**Локальный ПК:** ранее staged QA Agent 0.2.1-preview `--diagnose` вернул `isElevated=false`; запуск `Run-Unity-QA.ps1` ранее ответил `BLOCKED: QA Agent exited unexpectedly` без exit code. Следующий ближайший шаг — **не новая установка**, а временный `--serve-qa` со строго локальным `UUB_UNITY_QA=1` и наблюдением `QA_EXIT_CODE` либо `QA_RUNNING=TRUE`; после 2 секунд остановить **только созданный тестовый PID**. Запуск QA Desktop, `UNITY · ЛОГИ`, reimport 6 `.meta`, `Tools → uUnityBridge`, создание/сохранение/повторное открытие 3-платформенной сцены остаются **BLOCKED / NOT VERIFIED**.

**Ограничения:** не менять рабочую `uUnityBridge_App`; [Draft PR #25](https://github.com/sunpole/uUnityBridge/pull/25) отдельный upgrade/rollback QA и merge conflict pending; [Issue #23](https://github.com/sunpole/uUnityBridge/issues/23) открыт; приватный transport PNG+report остаётся `LOCAL_ONLY`, `uploadPermitted=false`, до явного разрешения и независимого ChatGPT readback.

## 2026-11-02 — проверка QA IPC в GitHub CI, коррекция ожидаемого кода выхода

**Подтверждённый ПК:** старый QA Agent `--diagnose` вернул `isElevated=false`. Причина прежнего `QA Agent exited unexpectedly` НЕ установлена без exit code от `--serve-qa`. **Никакого нового запуска, переимпорта UPM, Editor Console или сцены не подтверждено.**

**Изменения Draft PR #30** для корректного QA runtime теста: `47b7216` направляет QA `--request` к изолированному QA Pipe при `UUB_UNITY_QA=1`. `0dd2f04` добавил runtime тест `--serve-qa`: обязательный блок без QA флага (exit 2), а на elevated GitHub runner — обязательный отказ exit 5 вместо ложного QA PASS. Первое выполнение [Agent #41502830937](https://github.com/sunpole/uUnityBridge/actions/runs/41502830937) формально **FAIL** из-за сохранённого ожидаемого native exit 5 в GitHub pwsh wrapper даже после успешной проверки отказа. Коммит [67a7c90](https://github.com/sunpole/uUnityBridge/commit/67a7c90f92bdb69d08c312299ca4d2e71c9f6f47) очищает `LASTEXITCODE` **только после проверки** кода блокировки, не убирает саму защиту. Проверить результаты новых трёх CI: [Desktop #41503208005](https://github.com/sunpole/uUnityBridge/actions/runs/41503208005), [Agent #41503207919](https://github.com/sunpole/uUnityBridge/actions/runs/41503207919), [Unity #41503208078](https://github.com/sunpole/uUnityBridge/actions/runs/41503208078). На момент документирования они выполнялись. **CI PASS сам по себе не означает проверку QA на пользовательском ПК.**

**Безопасный следующий шаг:** на **старом** QA Agent из уже проверенного immutable слота отдельно запустить `--serve-qa` с локальным `UUB_UNITY_QA=1` и узнать только `ExitCode` через 2 секунды либо `QA_RUNNING=true`; если процесс всё ещё работает, завершить только его PID и восстановить переменную среды. Это не новая установка и не изменение рабочего агента. Затем исправлять подтверждённую причину, а не предполагать elevated. Не трогать `uUnityBridge_App`, PR #25 Draft/rollback, Issue #23; PNG/report остаются `LOCAL_ONLY`, `uploadPermitted=false`.

## 2026-11-02 — QA elevation excluded by real PC diagnostic; cause of early Agent exit still unknown

**Новый локальный вывод пользователя:** `uUnityBridge.Agent.exe --diagnose` из ранее staged QA-слота `4ed8ca798d89-41493135234-fe5f780e77d949889e53d35399916f35` вернул `{"service":"uUnityBridge.Agent","version":"0.2.1-preview","isElevated":false,"expectedExecution":"same standard Windows user/session for server and client"}`. **Это проверка прав диагностического процесса**, не запуск `--serve-qa`; в этом пользовательском контексте elevated-прав нет. Старый launcher **реально** выдавал `BLOCKED: QA Agent exited unexpectedly`, но не сообщал exit code. **Причина остаётся UNKNOWN**. Не записывать `BLOCKED_QA_ELEVATED_CONTEXT` как установленную причину. Desktop ещё не открылся; `UNITY · ЛОГИ`, reimport шести .meta, Tools → uUnityBridge, сцена и повторное открытие **не проверены**.

**Новые изменения только в Draft PR #30:** коммит [47b7216](https://github.com/sunpole/uUnityBridge/commit/47b7216f4bfa942941c2002252c45e827698c08e) маршрутизирует явный QA `--request` к изолированному QA Named Pipe только при `UUB_UNITY_QA=1`, а не в обычный production Pipe. Коммит [0dd2f04](https://github.com/sunpole/uUnityBridge/commit/0dd2f04fbd93f44157a5167a84d6dee782ae904f) добавляет Windows CI runtime проверку `--serve-qa`/QA health и запрета неизвестных команд: на обычном runner — настоящий QA Named Pipe E2E, а если runner elevated — обязательный отказ exit 5 **без заявления runtime PASS**. Продолжает действовать отдельная проверка блокировки без QA flag (exit 2), no GitHub polling, CurrentUserOnly, отсутствие удалённого shell. **Изменения не установлены на пользовательский ПК.** При записи новых docs CI для HEAD `0dd2f04fbd93f44157a5167a84d6dee782ae904f`: [Desktop #41502830945](https://github.com/sunpole/uUnityBridge/actions/runs/41502830945), [Agent #41502830937](https://github.com/sunpole/uUnityBridge/actions/runs/41502830937), [Unity #41502830951](https://github.com/sunpole/uUnityBridge/actions/runs/41502830951); статусы проверить перед выводом PASS.

**Ближайшая локальная диагностика без новой установки:** в отдельном временном QA процессе с `UUB_UNITY_QA=1` повторить `--serve-qa` и записать только его код завершения через 2 секунды. Если продолжает работать, завершить **только этот тестовый PID** и передать `QA_RUNNING=TRUE`. Если завершился, передать `QA_EXIT_CODE=<number>` (2 QA flag/args; 3 QA mutex conflict; 5 elevated; иной код требует дополнительного безопасного анализа). После диагностики восстановить прежнее значение `UUB_UNITY_QA`. Не удалять старый QA-слот и не запускать/убивать production Agent. В новой QA на GitHub также проверить 3/3 CI до запуска staging.

**Остальные долги:** P0014 Unity Editor E2E; P0015/P0016 приватный PNG и очищенный отчёт `LOCAL_ONLY`, `uploadPermitted=false` до отдельного разрешения и подтверждения private storage / ChatGPT readback; PR #25 доставка/откат остаётся Draft и ранее `mergeable=false`; Issue #23 открыт. Ни stable Release, ни замены `uUnityBridge_App`.

## 2026-11-02 — новый фактический блокер: QA Agent завершился при запуске

**Подтверждение пользователя:** QA staging на SHA `4ed8ca798d89fb0d6d7f90a10a78f9dfe940569e` / run `41493135234` прошёл проверку обоих EXE SHA-256 и Agent self-test; однако **при настоящем запуске** из `4ed8ca798d89-41493135234-fe5f780e77d949889e53d35399916f35\Run-Unity-QA.ps1` получено `BLOCKED: QA Agent exited unexpectedly.` в строке 11. **QA Desktop не был открыт, UNITY · ЛОГИ, UPM reimport, Tools → uUnityBridge и 3 платформы не проверены.** Не выдавать прежний STAGING PASS за START PASS. Конкретный exit code в прежнем launcher не записывался; причины (права администратора, повторный экземпляр/Mutex, отказ QA flag, иной сбой) пока гипотезы, не подтверждённый диагноз.

**Исправление в Draft PR #30:** новый код launcher различает `BLOCKED_QA_ELEVATED_CONTEXT` (не запускать в elevated Windows), `BLOCKED_QA_AGENT_ALREADY_RUNNING` (exit code 3), `BLOCKED_QA_FLAG_OR_ARGUMENT` (exit code 2) и `BLOCKED_QA_AGENT_EXIT_UNKNOWN` (остальные). Перед запуском проверяется наличие QA EXE; при блокировке не стартует Desktop, не предпринимается обход запрета агента, не изменяются Named Pipe или production. Генерируемый launcher дополнительно парсится как отдельный PowerShell 5.1 script в CI. Коммиты: [1c3a743](https://github.com/sunpole/uUnityBridge/commit/1c3a74333af03b5d9d17ce0def0bb2122481d5ce), [e40f311](https://github.com/sunpole/uUnityBridge/commit/e40f311e89375e6ddf08cf582c14bed38ae44f93). **Это диагностика причины, ещё не исправление неизвестного локального сбоя.**

Последний проверенный PR #30 HEAD: `e40f311e89375e6ddf08cf582c14bed38ae44f93`; Windows CI для этого HEAD: [Desktop+Agent #41501826487](https://github.com/sunpole/uUnityBridge/actions/runs/41501826487), [Agent #41501826505](https://github.com/sunpole/uUnityBridge/actions/runs/41501826505), [Unity contract #41501826480](https://github.com/sunpole/uUnityBridge/actions/runs/41501826480). **При составлении отчёта выполняются; считать `ci_pending` до явной проверки final conclusion.** До PASS нового SHA не скачивать и не запускать обновлённый launcher. Старую папку не удалять, `uUnityBridge_App` не менять.

**Следующий gate:** read-only выполнить старый Agent `--diagnose` для выяснения `isElevated`, не запуская сервер и не отправляя приватные данные; либо после нового CI PASS выполнить новый stage+launcher в обычном (не elevated) PowerShell. По кодам BLOCKED отладить только соответствующую причину, затем получить фактический QA Agent running + `UNITY · ЛОГИ`. Лишь затем переимпорт шести Unity `.meta`, меню, сцена и приватный sidecar. PNG/upload остаётся `LOCAL_ONLY`, `uploadPermitted=false`, до согласия/проверки приватности и ChatGPT readback. PR #25 Draft, `mergeable=false` ранее наблюдался; перед merge требуется проверка и E2E отката; [Issue #23](https://github.com/sunpole/uUnityBridge/issues/23) открыт.

## 2026-11-02 — LOCAL QA STAGING PASS; запуск Editor ещё ожидается

**Результат Windows PowerShell пользователя:** `Stage-Unity-QA.ps1` для точного Draft PR #30 SHA `4ed8ca798d89fb0d6d7f90a10a78f9dfe940569e` и Windows CI RunId `41493135234` завершился сообщениями `PASS: Unity IPC three typed actions`, `PASS: relay schema...`, `PASS: 10 agent protocol tests...`, `PASS: matching Desktop/Agent binaries staged; both SHA-256 verified.`. **Новая изолированная QA-папка создана** (`%LOCALAPPDATA%\uUnityBridge-QA\4ed8ca798d89-41493135234-fe5f780e77d949889e53d35399916f35`). Это локальное подтверждение скачивания/проверки Desktop и Agent, Agent self-test и отсутствия замены production EXE в рамках сценария, но **не факт запуска QA Desktop и не Unity Editor E2E**.

**Следующая введённая пользователем строка `To launch QA only` не была командой:** Windows PowerShell вывел `To : Имя "To" не распознано`. Это ошибка копирования поясняющего текста, не сбой Agent/Unity. Настоящая команда запуска находится после двоеточия в сообщении сценария:
```powershell
& "$env:LOCALAPPDATA\uUnityBridge-QA\4ed8ca798d89-41493135234-fe5f780e77d949889e53d35399916f35\Run-Unity-QA.ps1"
```
**Не утверждать, что пользователь уже выполнил эту команду**. UX-долг: в будущем выводить отдельную пригодную для копирования строку и предупреждать, что `To launch QA only` — заголовок, а не команда. Не изменять текущий QA-код на одном и том же SHA ради косметики до локальной приёмки.

**Оставшиеся реальные QA-gate:** 1) запуск изолированного Desktop и `UNITY · ЛОГИ` (подтверждённые сообщения Unity CLI/Editor или честный `BLOCKED`); 2) reimport Git UPM пакета с 6 `.meta` из pinned `4ed8ca798d89fb0d6d7f90a10a78f9dfe940569e`, отсутствие новых `has no meta file`; 3) `Tools → uUnityBridge`, создать демо-сцену с тремя платформами, сохранить и повторно открыть в `uUnityDemo`; 4) локальный sidecar / screenshot QA; 5) отдельное явное разрешение на закрытое хранилище, доказательство приватности, реальный upload/readback ChatGPT, до этого **LOCAL_ONLY / uploadPermitted=false**; 6) [Draft PR #25](https://github.com/sunpole/uUnityBridge/pull/25) isolated E2E upgrade/rollback с проверкой ранее отмеченного `mergeable=false`.

**Приватный транспорт — только проектирование:** возможный адаптер для закрытой папки Google Drive, доступной через уже подключённый к ChatGPT Google Drive-коннектор, **не означает**, что Windows Agent уже умеет аутентифицироваться в Google Drive и загружать файлы. Для реализации нужны отдельное локальное разрешение, ограниченный OAuth-поток/права только на назначенную папку, проверка политики доступа (не anyone/domain), целостности PNG, санитизация JSON, идемпотентность, удаление и независимый readback. Пока разрешения на upload нет и хранилище не выбрано; ничего не создавать и не загружать.

Источники: [PR #30](https://github.com/sunpole/uUnityBridge/pull/30), [Windows CI #41493135234](https://github.com/sunpole/uUnityBridge/actions/runs/41493135234), [PR #25](https://github.com/sunpole/uUnityBridge/pull/25), [Issue #23](https://github.com/sunpole/uUnityBridge/issues/23).

## 2026-11-02 — QA staging patch: CI 3/3 PASS, приёмка ПК остаётся

[Draft PR #30](https://github.com/sunpole/uUnityBridge/pull/30) HEAD `4ed8ca798d89fb0d6d7f90a10a78f9dfe940569e`: исправлено создание уникального QA-слота на каждый успешный RunId/SHA без overwrite старого `2ef31bc8fb75`. Первое выполнение добавленного CI guard на `c730ad247e6214ce80272ba4133a6d13e994580e` упало из-за ложного regex-совпадения `Move-Item` внутри `Remove-Item`; guard исправлен коммитом `4ed8ca7`.

**Верификация текущего HEAD GitHub Actions: 3/3 PASS:** [Desktop+Agent #41493135234](https://github.com/sunpole/uUnityBridge/actions/runs/41493135234), [Agent #41493135352](https://github.com/sunpole/uUnityBridge/actions/runs/41493135352), [Unity contract #41493135193](https://github.com/sunpole/uUnityBridge/actions/runs/41493135193). В Desktop workflow находятся **оба** артефакта `uUnityBridge-windows-x64-ci` и `uUnityBridge-agent-p0008-preview`. Проверка PowerShell 5.1 нового Stage прошла на Windows CI. **Не является** фактом исполнения на ПК; старый QA-слот нельзя удалять, `uUnityBridge_App` не изменять.

**Следующий реальный E2E:** stage только из run #41493135234 и SHA выше, проверить обе локальные хеш-суммы и Agent self-test, открыть QA; проверить `UNITY · ЛОГИ` (реальные CLI/Editor ошибки либо честный BLOCKED); обновить pinned Unity UPM Git package с шестью .meta, проверить исчезновение предупреждений; `Tools → uUnityBridge`, три платформы и сохранение/повторное открытие сцены. Снимок и отчёт остаются `LOCAL_ONLY` без upload до разрешения на закрытое хранилище. PR #25 остаётся Draft до отдельного E2E доставки/отката.

## 2026-11-02 — безопасное устранение коллизии QA в Draft PR #30

В [Draft PR #30](https://github.com/sunpole/uUnityBridge/pull/30) исправлен сценарий `Stage-Unity-QA.ps1`: при проверенном `ExpectedCommit` и успешном `RunId` **каждая попытка теперь использует отдельную уникальную тестовую папку** (`SHA12-RunId-GUID`); старый слот `2ef31bc8fb75` не удаляется и не перезаписывается. Защита от junction/symlink корневого QA-каталога и проверка SHA-256 Desktop/Agent сохранены. В `windows-build.yml` добавлена проверка синтаксиса PowerShell 5.1 и статических правил против перезаписи/удаления. Коммиты: [dfb4d6b](https://github.com/sunpole/uUnityBridge/commit/dfb4d6bfc52506392938f4ebaa4b628c3f2f04a2) → [c730ad2](https://github.com/sunpole/uUnityBridge/commit/c730ad247e6214ce80272ba4133a6d13e994580e). Windows CI на этом HEAD запущены: [Desktop+Agent #41492786845](https://github.com/sunpole/uUnityBridge/actions/runs/41492786845), [Agent #41492786977](https://github.com/sunpole/uUnityBridge/actions/runs/41492786977), [Unity contract #41492786941](https://github.com/sunpole/uUnityBridge/actions/runs/41492786941). **На момент записи CI ещё выполняется, локальная QA НЕ пройдена**. Не объявлять реализацию PASS до результатов CI и проверки на ПК. `uUnityBridge_App` не затронута.

## 2026-11-02 — QA BLOCKED: существующий тестовый каталог

PowerShell пользователя вернул `BLOCKED: QA folder already exists` при `Stage-Unity-QA.ps1 -ExpectedCommit 2ef31bc8fb75694c77ed0e977bb4ff157a983ff6 -RunId 41485132406` для `%LOCALAPPDATA%\uUnityBridge-QA\2ef31bc8fb75`. Скрипт намеренно запрещает перезапись. **Это не локальный QA PASS:** неизвестны содержимое каталога, целостность его EXE, происхождение файлов и состояние нового запуска.

[PR #30](https://github.com/sunpole/uUnityBridge/pull/30) открыт как Draft на точном SHA выше. Windows CI 3/3 PASS: [Desktop+Agent 41485132406](https://github.com/sunpole/uUnityBridge/actions/runs/41485132406), [Agent 41485132430](https://github.com/sunpole/uUnityBridge/actions/runs/41485132430), [Unity 41485132420](https://github.com/sunpole/uUnityBridge/actions/runs/41485132420). Оба QA-артефакта доступны в первом run. [PR #25](https://github.com/sunpole/uUnityBridge/pull/25) Draft на `fb840d1bf6bf0de623ec129fb0f70e858b0d78e1`, GitHub сообщает `mergeable=false` (причину перепроверить перед merge). [Issue #23](https://github.com/sunpole/uUnityBridge/issues/23) открыт.

**Следующие блокирующие проверки:** только read-only проверка старого QA-слота; независимая сверка SHA256 обоих EXE со скачанными в отдельную папку артефактами того же RunId и SHA; запуск QA только после сверки и согласия. Затем `UNITY · ЛОГИ` с реальными CLI/Editor ошибками либо честным `BLOCKED`; переимпорт UPM с шестью `.meta` и отсутствие новых предупреждений; `Tools → uUnityBridge`, три платформы, сохранение и повторное открытие сцены. Приватная отправка PNG/санитизированного отчёта не реализована: `LOCAL_ONLY`, `uploadPermitted=false`. Разрешение и проверка закрытого хранилища/чтения ChatGPT обязательны. Отдельный E2E delivery/rollback PR #25 до релиза. `uUnityBridge_App` не изменять.

## Контрольная точка 2026-11-02: починка CI, новое QA и приватные доказательства

**Windows CI для проверенного HEAD PR #30 `2ef31bc8fb75694c77ed0e977bb4ff157a983ff6` — 3/3 PASS:** [Desktop + Agent №41485132406](https://github.com/sunpole/uUnityBridge/actions/runs/41485132406), [протокол агента №41485132430](https://github.com/sunpole/uUnityBridge/actions/runs/41485132430), [Unity UPM/.meta №41485132420](https://github.com/sunpole/uUnityBridge/actions/runs/41485132420). Артефакты обоих EXE относятся к одному CI №41485132406. **Ни исправленный Unity Editor Console, ни новый pinned UPM Git package на пользовательском ПК повторно не проверены.**

**Приоритет из запроса пользователя:** `.meta` Unity UPM → корректные ошибки Unity Console →
изолированная QA-сборка → приватная доставка снимка/журнала в ChatGPT.

**PR #25** (`fb840d1bf6bf0de623ec129fb0f70e858b0d78e1`) остаётся Draft;
его старые 3 Windows CI были PASS, но реальный QA обновления/отката отсутствует.
**PR #30**: после предыдущих исправлений обнаружены повреждённые YAML сразу
в трёх workflow, из-за чего новые CI могли вообще не запускаться.
Workflow восстановлены и в них исправлена проверка CRLF у Unity `.meta`.
Истинную готовность новых сборок определять **только по результату
GitHub Actions именно на текущем HEAD PR**, а не по старым результатам.
Контрольный SHA изменений: `2ef31bc8fb75694c77ed0e977bb4ff157a983ff6`.

**Реальный факт пользователя:** QA `UNITY · КАДР` снимал только окно
Unity Editor `uUnityDemo` 1936×1056, но `UNITY · ЛОГИ` выдавал
«ошибок нет» несмотря на `has no meta file`. Новый код для фильтра
ошибок и большого окна консоли есть в PR #30, **новая локальная
приёмка не проведена**. Шесть версионированных `.meta` присутствуют
в Git; исчезновение предупреждений после установки новой ревизии
UPM на ПК пока **не проверено**.

**Тестовая доставка:** `scripts/Stage-Unity-QA.ps1` переписан целиком,
теперь принимает один `-RunId` успешного Desktop+Agent Windows workflow
и `-ExpectedCommit`, проверяет точный SHA коммита и SHA256 **обоих**
EXE, исполняет Agent `--self-test` и готовит строго отдельную QA-папку.
Применять его можно **только после PASS соответствующего Windows CI**;
установку `uUnityBridge_App` не заменять.

**Приватная доставка:** после локального `UNITY · КАДР` в Draft записывается
`latest-unity-evidence.json` с SHA256/размером/UTC и явными
`transport=LOCAL_ONLY`, `uploadPermitted=false`. Никакой реальной
отправки на GitHub/в ChatGPT пока нет. Передавать только в
закрытое место после отдельного согласия пользователя и проверки
возможности чтения через ChatGPT-коннектор:
[контракт P0015/P0016](https://github.com/sunpole/uUnityBridge/blob/feature/unity-mvp-editor-foundation/docs/PRIVATE_EVIDENCE_RELAY.md).

**Следующий видимый результат:** после PASS нового CI — отдельно открыть
QA GUI, убедиться, что `UNITY · ЛОГИ` показывает реальные
предупреждения либо честно объясняет блокировку источника;
обновить pinned Git URL UPM на SHA с `.meta`, удостовериться
в исчезновении Unity warning и проверить `Tools → uUnityBridge`.
Не заявлять, что агент уже создаёт сцену автоматически. Не просить
пользователя переприсылать скриншоты вручную как штатный сценарий.

## 2026-11-02 — ОБЯЗАТЕЛЬНО при каждом отчёте о разработке uUnityBridge

**Постоянное пожелание пользователя, закреплено в
[AGENTS.md](AGENTS.md#обязательный-формат-каждого-отчёта-о-разработке--действия-и-следующий-промпт).**
В каждом содержательном ответе о разработке, патчах, PR, тестах,
доставке, ошибках или приёмке uUnityBridge в конце **всегда**
выводить:

1. **«Что вам сделать»** — до трёх конкретных действий с ожидаемым
   результатом, учитывая управление ПК с телефона через AnyDesk;
   если действий нет — буквально «Сейчас от вас ничего не требуется».
2. **«Следующий промпт (необязательный)»** — один пригодный к копированию
   блок кода, сформированный из **открытых долгов беседы, последнего
   результата, патчей, PR, Issue, CI и недоказанных QA-гейтов**.
   Указывать ближайший блокирующий долг, реальный план проверки,
   ограничения и обновление документов.

**Промпт — предложение, а не инструкция к автоматическому запуску.
Пользователь не обязан его выполнять и может выбрать другую задачу.**
Новый чат сначала сверяет реальные источники, не копирует устаревшие
версии и не превращает план в утверждение, что что-либо уже установлено.

### Долги для формирования ближайших следующих промптов
На контрольной точке 2026-11-02 (сверять заново перед действиями):

- Draft **PR #30**: недостающие шесть `.meta` уже добавлены в Git,
  отчёт `UNITY · ЛОГИ` и большой интерфейс исправляются в коде;
  повторные CI и реальная установка нового pinned Git SHA Unity UPM
  пакета ещё требуют подтверждения. Последний указанный HEAD:
  `0943dd5b23f27ab324becee20c39c3ab85f57636`.
- **Реальный локальный QA PASS**: захват только Unity Editor,
  1936×1056; автоматическая защищённая доставка в ChatGPT **отсутствует**.
- **P0011/P0015/P0016**: приватная передача изображения и ошибок через
  разрешённый подключённый источник, прочтение из ChatGPT, сквозной
  ChatGPT → GitHub → Agent → Unity → приватный снимок → ChatGPT,
  идемпотентность и достоверные финальные статусы — не завершены.
- Draft **PR #25**: безопасная доставка/откат и реальная Windows
  QA-приёмка по-прежнему требуют проверки перед Release.
- **Unity Editor MVP**: `uUnityDemo` открыт и `unity_status`
  проходил; создание и сохранение сцены с платформами через управляемый
  агент ещё не доказаны на ПК.

Использовать список как **исходную точку**, а не вечный жёсткий порядок:
после каждого патча/ответа обновлять долги по фактам и подбирать
следующий необязательный промпт из оставшихся.

## 2026-11-02 — Реальный QA: снимок Unity работает, исправляем консоль и .meta

**Реально подтверждено пользователем:** QA Desktop/Agent из PR #30 работают на Windows;
`UNITY · КАДР` сохранил **1936×1056 px** снимок **окна Unity 6.6** с видимой
`SampleScene` и Console, `LOCAL_ONLY`. В приватный чат автоматически PNG
ещё НЕ передаётся. Это реальный PASS **локального снимка** — не P0016.

**Реальная обнаруженная ошибка:** окно Unity Console показывает `has no meta file`
для шести файлов/папок `com.sunpole.uunitybridge`, а `UNITY · ЛОГИ` выдал
ложное «Подходящих сообщений об ошибках нет». Причины в разработанном коде:
(1) UPM-пакет Git опубликован без необходимых `.meta`; (2) глобальный
`Editor.log` читается только при буквальном пути проекта в начале файла;
(3) фильтр не распознавал `has no meta file`; (4) консольный отчёт в GUI
был тесен/расположен внизу.

**В Draft PR #30 исправлено в исходниках:**
- Добавлены ровно шесть версионированных `.meta`, включая `Editor.meta`,
  C# скрипты, `asmdef`, `package.json` и `README.md`, с уникальными GUID.
  CI проекта проверяет наличие всех метаданных; настоящая повторная установка
  UPM пакета в Editor ещё НЕ проверена. Ранее установленный Git SHA остаётся
  старым до явной смены URL/коммита в Package Manager.
- `UNITY · ЛОГИ`: расширенный фильтр распознаёт `has no meta file` и
  `immutable folder`; если заголовок `Editor.log` не содержит путь проекта,
  агент допускает только строго **один подтверждённый работающий Unity Editor**
  `uUnityDemo` в ожидаемой версии с актуальным журналом. Неясный источник
  показывается как `BLOCKED`, НЕ «ошибок нет». Текст по-прежнему ограничен
  и очищается от токенов, адресов и путей.
- QA интерфейс больше не тратит место на пустой PATCH список; кнопка
  `UNITY · ЛОГИ` открывает отдельную большую прокручиваемую консоль.

**Точная ветка:** `feature/unity-mvp-editor-foundation`, [PR #30](https://github.com/sunpole/uUnityBridge/pull/30),
HEAD `c24db4340e6569258103eba34e49fd3455776e31`. CI нового HEAD ещё
проверить; установленная QA сборка у пользователя старая (`e202f4d...`).
Рабочие EXE не обновлять тестовыми сборками. **Не объявлять исправление
консоли и пакета подтверждённым на ПК** без нового QA.

**Ближайшие шаги:** проверить 2 Windows CI для нового HEAD; получить из
них отдельную QA-копию (`scripts/Stage-Unity-QA.ps1`); проверить в
отдельном большом окне `UNITY · ЛОГИ` реальные Unity ошибки.
Затем обновить pinned Git URL редакторного пакета на SHA с `.meta`,
проверить исчезновение warnings и меню Tools → uUnityBridge.
После этого — защищённый upload одного скриншота, реально читаемый в ChatGPT.

## 2026-11-02 — новый QA: ошибки консоли и локальный снимок Unity

**Состояние:** только в Draft [PR #30](https://github.com/sunpole/uUnityBridge/pull/30),
точный HEAD `e202f4d2816770cd5d248d0ad6e42ff01fc3217a`. **Windows CI 3/3 PASS**:
[Agent](https://github.com/sunpole/uUnityBridge/actions/runs/41469011247),
[Desktop](https://github.com/sunpole/uUnityBridge/actions/runs/41469010811),
[Unity policy](https://github.com/sunpole/uUnityBridge/actions/runs/41469010738).
**На ПК эта QA-версия ещё не установлена и не проверена.**

Пользователь управляет домашним ПК с телефона через AnyDesk, поэтому
ручная передача скриншотов в чат — не штатное решение. Добавлены:
- **UNITY · ЛОГИ:** локальное типизированное `unity_diagnostics` по CurrentUserOnly
  Named Pipe. Агент записывает ограниченный хвост Unity CLI stdout/stderr и,
  только если смог доказать принадлежность `Editor.log` разрешённому
  `uUnityDemo`, показывает до 10 последних сообщений Error/Exception/Warning.
  Токены, URL, email и локальные пути редактируются, строки ограничены.
  В UI два раздельных блока: «КОНСОЛЬ UNITY CLI» и «КОНСОЛЬ UNITY EDITOR».
- **UNITY · КАДР:** одна локальная кнопка в QA Desktop, выбирает окно
  действительного Unity Editor версии `6000.6.7f1` и проекта `uUnityDemo`,
  только при локальном согласии. Захват отдельного HWND через PrintWindow
  (может быть чёрным из-за GPU!), локальный приватный PNG
  `%LOCALAPPDATA%\\uUnityBridge-QA\\captures\\latest-unity-window.png`,
  показ в QA-окне. Без отправки скриншота в публичный GitHub.
- Тесты фильтров, ограничений строк и отказа для чужого `Editor.log` PASS.
- [Подробный документ](https://github.com/sunpole/uUnityBridge/blob/feature/unity-mvp-editor-foundation/docs/P0015_CAPTURE_DIAGNOSTICS.md).

**Что всё ещё НЕ работает:** снимок из QA ещё не проверен у пользователя;
он НЕ передаётся автоматически в ChatGPT, нет утверждённого приватного канала
и нет сквозного ChatGPT→Unity→screenshot→ChatGPT E2E. Это приоритет
следующей разработки; P0015 и P0016 не отмечать DONE. Рабочие EXE
`uUnityBridge_App` не менялись. Не загружать приватные снимки в public repo.

**Ближайшее ручное испытание:** закрыть ранее запущенный QA Desktop, скачать
проверенную новую QA-сборку через `scripts/Stage-Unity-QA.ps1` с exact SHA
и Run IDs выше, открыть новую QA-копию и нажать `UNITY · ЛОГИ`,
`UNITY · КАДР`. Только после проверки можно выбрать приватный
доступный ChatGPT канал и реализовать передачу.


## QA-развёртывание без затрагивания рабочей установки — 2026-11-02

**Важно:** реальной установки нового Desktop/Agent на ПК пока НЕ было; PR #25 и #30 остаются Draft, стабильный Release v0.4.2 без изменений.

**Исправление найденного дефекта:** официальный Unity CLI `projects create <имя> --path <папка>` принимает **родительский каталог**, а не полный путь к новому проекту. Это исправлено в `agent/UnityCliBridge.cs` PR #30 и покрыто тестом. Источник: https://github.com/Unity-Technologies/skills/blob/main/skills/unity-cli/SKILL.md.

**Создан изолированный QA-режим PR #30:** агент `--serve-qa` под пользователем Windows использует отдельные Mutex `Local\\uUnityBridge.Agent.qa.v1`, Named Pipe `uUnityBridge.agent.qa.v1` и файл `unity-approval-qa.json`; исходящий GitHub relay отключён. Desktop с переменной окружения `UUB_UNITY_QA=1` отключает GitHub PATCH/ANS, обновлятор и работу с обычной локальной историей. Запущенные Production Desktop/Agent не заменяются. QA-скрипт `scripts/Stage-Unity-QA.ps1` проверяет успешный SHA HEAD двух CI workflow, загружает artifacts в новую папку `%LOCALAPPDATA%\\uUnityBridge-QA\\<12-hex>`, сверяет SHA-256 Desktop и выполняет `Agent --self-test`; отдельный `Run-Unity-QA.ps1` запускается **только по инициативе пользователя** и останавливает QA Agent при закрытии окна. Тестовые EXE не подписаны как стабильный Release; процедура ещё не запускалась на пользовательском ПК.

**Windows CI для точного PR #30 HEAD `481fcfef455e868ae498838bd406a71df3c50749` — 3/3 PASS:**
- Desktop [#41443979385](https://github.com/sunpole/uUnityBridge/actions/runs/41443979385)
- Agent [#41443979594](https://github.com/sunpole/uUnityBridge/actions/runs/41443979594)
- Unity contract [#41443979317](https://github.com/sunpole/uUnityBridge/actions/runs/41443979317)

**Ближайшее действие:** по явному выбору пользователя скачать проверенные CI artifacts в отдельную QA-папку, **не** запускать рабочий Updater для тестового ZIP. Затем открыть QA Desktop + QA Agent, разрешить отдельную родительскую папку, нажать `UNITY · СОЗДАТЬ` и удостовериться, что действительно создан `uUnityDemo` (Unity 6.6 `6000.6.7f1`), открыть его и проверить `unity_status`. Если появится ошибка — сохранить прежнюю программу нетронутой, снять лог/скриншот **только QA окна**. Это ещё НЕ завершено; скриншотер и ChatGPT→Unity GitHub relay пока не готовы.

## Актуальная контрольная точка — 2026-11-02, Unity Agent → Unity CLI

**Последний факт с ПК:** Unity Hub работает, установлен **Unity Editor 6.6 (6000.6.7f1)**. Unity CLI beta.15 установлен. Пользователь **не подтвердил**, что тестовый `uUnityDemo` создан/открыт. `uUnityBridge_App` и Agent не обновлялись.

### Что добавлено и проверено (ТОЛЬКО исходники в Draft PR #30)

[Draft PR #30](https://github.com/sunpole/uUnityBridge/pull/30), ветка
`feature/unity-mvp-editor-foundation`, HEAD `3c5519711ba37b92f5dee7682261055ec8192db4`:

- `agent/UnityCliBridge.cs` — три типизированных действия
  `unity_status`, `unity_project_create`, `unity_project_open`.
  Запускаются исключительно фиксированные Unity CLI процессы через
  `ProcessStartInfo.ArgumentList`; PowerShell/CMD не используются.
- `src/UnityControlUi.cs` и `src/MainForm.cs` — кнопки:
  **UNITY · ПАПКА**, **UNITY · СТАТУС**, **UNITY · СОЗДАТЬ**,
  **UNITY · ОТКРЫТЬ**. Desktop передаёт JSON по Windows Named Pipe
  `CurrentUserOnly`, а не через общедоступный HTTP.
- Только пользователь на ПК выбирает **существующий родительский каталог**
  и явно разрешает проект `uUnityDemo` в
  `%LOCALAPPDATA%\uUnityBridge\unity-approval.json`.
  Отправленные через GitHub Issue пути, флаги и команды не принимаются.
- Unity CLI создание проекта `uUnityDemo`: строго определённый `--path`
  внутри локально разрешённой папки, `--template 2d`,
  `--editor-version 6000.6.7f1`, `--no-cloud`,
  `--with-pipeline`. Повторное создание не перезаписывает папку.
  `unity_status` проверяет только `--project-path` этого проекта.
  `unity_project_open` допускает только существующий Unity-проект.
- Создание/открытие сначала дают `ACCEPTED`, а НЕ `PASS`:
  асинхронную работу CLI нужно проверить отдельно.
- **CI для точного HEAD — PASS (3/3), включая проверку, что JSON-состояние `not_ready`/`ready:false` не считается подключением:**
  [Agent + Unity policy #41434916126](https://github.com/sunpole/uUnityBridge/actions/runs/41434916126);
  [Desktop GUI #41434916270](https://github.com/sunpole/uUnityBridge/actions/runs/41434916270);
  [Unity Editor policy #41434916382](https://github.com/sunpole/uUnityBridge/actions/runs/41434916382).
  Тесты используют изолированную Windows VM, **не** установленный Unity пользователя.
- Документ реализации в PR:
  [docs/UNITY_AGENT_CLI.md](https://github.com/sunpole/uUnityBridge/blob/feature/unity-mvp-editor-foundation/docs/UNITY_AGENT_CLI.md).

### Чего пока нет (НЕ объявлять готовым)

- Установленный у пользователя Desktop `0.4.2.0` и Agent `0.2.1.0`
  **не содержат** новых действий; код из PR #30 не был доставлен на ПК.
- **Нет реального E2E**: CLI по кнопке → открытый Unity Editor →
  сохранённая сцена → подтверждённая проверка.
- **Нет удалённых** `unity_project_create/open` из GitHub relay:
  публичные Issues пока поддерживают только старый безопасный `status`.
  До расширения relay нужны локальное разрешение, устойчивый журнал,
  контроль повторов и итоговый ответ (не только `ACCEPTED`).
- Пакет Editor из PR #30 ещё не устанавливался в настоящий Unity и
  не создавал платформы. Нет приватного скриншотера, нет прямого MCP
  подключения ChatGPT к локальному ПК.
- **PR #25 P0010** доставки остаётся Draft; без локального VM E2E не
  выпускать стабильный Release и не заменять рабочие EXE.

### Что сделать следующим

1. Интеграционно проверить CLI beta.15 на настоящей Unity 6.6:
   команду `projects create` с `--path`, `--with-pipeline`,
   `projects open` и `status --project-path`. До этого считать
   синтаксис и runtime поведение **непроверенными на ПК пользователя**.
2. Провести безопасную приёмку P0010/PR #25 в изолированной Windows VM;
   согласовать версии и перенос Agent/Desktop с новым Unity IPC и GUI
   из PR #30. Не устанавливать CI EXE прямо на рабочую установку.
3. После безопасного обновления получить явное локальное разрешение
   папки через GUI и провести реальный `uUnityDemo` E2E.
4. Затем подключать Editor scene/create/save, подтверждённое screenshot
   только выбранного окна и устойчивый GitHub relay для типизированных
   команд. Только реальный видимый результат переводит цель в `done`.
5. Обновлять `AGENTS.md`, `version.md`, `version.json`, `version.html`,
   критерии готовности и этот документ по проверенным фактам.

## Текущая контрольная точка UNITY-MVP — 2026-11-01

**Пользователь уточнил:** Unity Hub, Unity Editor и тестового проекта пока **НЕТ** на Windows ПК. Нельзя требовать от него открыть отсутствующий Editor. Инструкция: с официального `https://unity.com/download` установить Unity Hub, войти в аккаунт (если разрешено правилами платформы), через Installs → Install Editor выбрать **Unity 6.3 LTS**, затем Projects → New project → **2D**, название `uUnityDemo`. Записать версию редактора и подтвердить открытие проекта. Не запускать старые скрипты и не обновлять рабочий uUnityBridge.

**Реально сделано в GitHub:** создан отдельный **Draft [PR #30](https://github.com/sunpole/uUnityBridge/pull/30)**, ветка `feature/unity-mvp-editor-foundation`, commit `b06c08b4deb6c3686f1286c836d3be9c18cf52a8`.
В `unity/Packages/com.sunpole.uunitybridge/` добавлен редакторный пакет `0.1.0` (Unity 6.3 LTS), меню `Tools → uUnityBridge`, четыре локальные типизированные операции `unity_status`, `create_scene`, `create_platform`, `save_scene`. Они работают с фиксированной демонстрационной сценой `Assets/uUnityDemo/Scenes/uUnityDemo.unity`, отказываются менять чужую сцену, перезаписывать существующую и использовать произвольный путь/команду.
- [Unity policy CI #41339356505](https://github.com/sunpole/uUnityBridge/actions/runs/41339356505) — **PASS** (без Unity Editor).
- [Windows desktop CI #41339356403](https://github.com/sunpole/uUnityBridge/actions/runs/41339356403) — **PASS**.
- **Не проверено:** установка пакета через Package Manager, компиляция внутри Unity Editor, реальное создание/открытие сцены на Windows, ChatGPT→Unity связь, window_capture и API-адаптер. До реального теста Unity статус `UNITY-MVP` — `ci_pass_qa_pending`, не `done`.
- **PR #25 P0010** остаётся отдельно открытым Draft; `v0.4.2` — последний стабильный Desktop. Никакие файлы рабочей установки `uUnityBridge_App` или Agent не изменялись.
- Основная история обновлена: `version.json`, `version.md`, `version.html`, `docs/GOALS_AND_ACCEPTANCE.md`; полные правила — `AGENTS.md`.
- **Следующий реальный видимый критерий:** установлен Unity Editor, открыт пустой проект `uUnityDemo`, пакет из конкретного SHA PR #30 проверен, `Tools → uUnityBridge → Создать тестовую сцену` реально создаёт и сохраняет сцену с тремя платформами.
- **Новые фичи агента не обещать до реализации.** Agent 0.2.1 пока работает только с `status`; агент не читает `AGENTS.md` и не принимает команды Unity.
- **Уведомление о полном завершении** настроено отдельной условной автоматизацией ChatGPT и не означает, что задачи выполняются в фоне.

## Главное уточнение цели и новые правила — 2026-11-01 (UNITY-FIRST)

**Проблема обнаружена пользователем:** много Windows CI и обновлений,
но пока нет видимого управления Unity из ChatGPT. **Следующая разработка
должна дать настоящий Unity Editor MVP**; не подменять цель обновлятором.

**Новые документы (main):**
- `AGENTS.md` — обязательные правила, реальные доказательства и уведомление.
- `docs/GOALS_AND_ACCEPTANCE.md` — приоритеты, видимые критерии и действия пользователя.
- `agent/GOAL_REPORTING.md` — политика отчётности будущего локального Agent
  (это **не** функция установленного Agent 0.2.1).
- `version.md` — человеческая история; `version.json` — машинный снимок;
  `version.html` — наглядная **статическая** хронология. Раньше этих трёх файлов не было.
  `docs/HISTORICAL_RELEASES.md` содержит исторические коммиты.
- `README.md` — текущее назначение проекта и ссылки на документы.

**Честный статус:**
- Desktop `v0.4.2` — последний стабильный GitHub Release.
- По последней пользовательской диагностике: Desktop `0.4.2.0`,
  Updater `0.4.0.0`, Agent `0.2.1.0`; установка после этого
  **не менялась этим чатом**.
- GitHub relay `status` подтверждён [R000005](https://github.com/sunpole/uUnityBridge/issues/29).
- [PR #25](https://github.com/sunpole/uUnityBridge/pull/25) — Draft, новая
  доставка прошла CI, но **не выпущена и не установлена**.
- В `main` ещё НЕТ проверенного в редакторе Unity Editor package; в отдельном Draft PR #30 есть его исходники и CI-контракт. Нет window_capture или реального API ChatGPT ↔ Unity.
  `docs/LOCAL_API_MCP.md` — проектирование, НЕ существующий MCP.
- `version.json` — документальный снимок, не выдаёт агенту разрешений
  и не обновляет цели автоматически.

**Действия пользователя сейчас (всё):**
1. Открыть Unity Hub и удостовериться, что установлен Unity Editor.
2. Создать/открыть **отдельный тестовый 2D-проект** `uUnityDemo`,
   оставить настоящий Unity Editor запущенным.
3. Передать в чат **версию Editor и название тестового проекта**.
   Личный полный путь не требуется. `uUnityBridge.exe`, PowerShell
   и замену EXE запускать не нужно.

**Сразу после этой проверки:** в GitHub реализовать отдельный
проверенный Unity Editor C# package с типизированными `unity_status`,
`create_scene`, `create_platform`, `save_scene`, ограниченными
командами. Не превращать GitHub Issue в shell и не запускать произвольный
C#-код. Показать настоящий результат в тестовом проекте,
затем интеграцию с API/GUI агента и приватный скриншот одного окна.
Параллельно держать P0010 как отдельную безопасную готовность доставки,
не сливая Draft PR до реальной VM E2E.

**Правило завершения:** лишь после реальных доказательств и приёмки
всех согласованных блокирующих целей/долгов/патчей сообщить:
**«ТЕКУЩИЙ НАБОР ЦЕЛЕЙ ЗАВЕРШЁН. НУЖНЫ НОВЫЕ ЦЕЛИ»**.
Если цель не закрыта — явный статус и следующий видимый результат.
На 2026-11-01 создана ежедневная условная автоматизация ChatGPT,
которая только проверяет GitHub и сообщает о полном завершении,
если есть независимые доказательства ВСЕХ блокирующих целей.
Она не изменяет GitHub/ПК и не заменяет работу с пользователем.
Обычный чат без такой автоматизации сам позже не отправляет сообщения.

### Готовый промпт следующего чата

```text
Продолжи sunpole/uUnityBridge без Codex.
Сначала прочитай main: AGENTS.md, CONTINUE_HERE.md, version.json,
version.md, docs/GOALS_AND_ACCEPTANCE.md, agent/GOAL_REPORTING.md.
Перепроверь текущие GitHub PR, Issue, Release и CI.
ГЛАВНАЯ ЦЕЛЬ: из ChatGPT управлять настоящим Unity Editor через
безопасный типизированный API, видеть сцену и получать приватный
скриншот только разрешённого окна. Не подменяй это обновлятором.

Начни с первого ВИДИМОГО результата: Unity Editor package
с командами unity_status/create_scene/create_platform/save_scene;
запусти возможные тесты в CI и подготовь проверку в отдельном Unity
проекте обычного пользователя. Не объявляй Unity PASS до проверки в
настоящем Unity Editor. Затем добавляй связь с локальным Agent,
видимый GUI статуса и защищённый снимок окна.
Параллельно продолжай существующий Draft PR #25 для безопасной
доставки только до подтверждённого E2E. Ничего не меняй в рабочем
uUnityBridge_App и не публикуй новый стабильный Release без QA.
После каждого этапа обновляй version.json/version.md/version.html,
CONTINUE_HERE.md и статус целей с доказательствами.
Когда ВСЕ согласованные цели, долги и патчи реально DONE,
сообщи «ТЕКУЩИЙ НАБОР ЦЕЛЕЙ ЗАВЕРШЁН. НУЖНЫ НОВЫЕ ЦЕЛИ».
В ответе показывай три вещи: что видит пользователь, что
подтверждено тестами, и что нужно сделать дальше — без обещаний
фоновой разработки.
```

## Новейшая контрольная точка — 2026-11-01, P0010-D: изолированная Windows QA

**Статус:** ветка PR #25 содержит новый testable agent upgrade,
Windows CI для ТОЧНОГО HEAD **3/3 PASS**. Это **НЕ** означает установку
новой версии на рабочий ПК. **PR #25 Draft, стабильный Release v0.4.2.**

- **PR:** [#25](https://github.com/sunpole/uUnityBridge/pull/25),
  ветка `feature/p0010-safe-release-deploy`,
  проверенный HEAD `fb840d1bf6bf0de623ec129fb0f70e858b0d78e1`.
- **Техническая реализация:** добавлен `updater/AgentUpgradeTransaction.cs`.
  Реальный helper `updater/AgentUpdater.cs` теперь использует этот
  модуль для проверенного копирования, переключения EXE,
  ожидания `health`, сохранения SHA-256 старой версии и rollback.
  Границы процессов передаются тестовыми callbacks, поэтому
  CI не останавливает пользовательские процессы.
- **Windows fault-injection:** новый проект
  `tests/uUnityBridge.UpgradeTests` (запускается только при
  `GITHUB_ACTIONS=true`, использует только `RUNNER_TEMP`).
  Проверки: недоступный файл после сбоя сети, обрезанная загрузка,
  плохой SHA-256, чужой staging, эксклюзивная блокировка Windows EXE
  до и после backup, отказ остановки процесса, неудачный `health`,
  исключение IPC, успешный update и проверенный hash backup.
  **Сбои сети имитируются отсутствием/обрезкой файла**, а не
  реальным разрывом GitHub/CDN. EXE-файлы в этих тестах —
  безвредные временные текстовые фикстуры.
- **Локальный API:** `agent/LocalIpcSelfTests.cs` реально открывает
  временную Windows Named Pipe `CurrentUserOnly` и проверяет
  `health`, `capabilities`, отказ от неизвестного `powershell`.
  Это не полный запуск постоянно работающего агента из
  непривилегированной пользовательской сессии.
- **Windows CI PASS на HEAD:**
  [Desktop/upgrade #41333118950](https://github.com/sunpole/uUnityBridge/actions/runs/41333118950),
  [Agent/IPC #41333118927](https://github.com/sunpole/uUnityBridge/actions/runs/41333118927),
  [Manifest #41333119066](https://github.com/sunpole/uUnityBridge/actions/runs/41333119066).
- `.github/workflows/release-windows.yml` обновлён:
  до публикации стабильных assets он **должен** запускать UpgradeTests
  и IPC-self-test. Сам release workflow по этому PR **не выполнялся**,
  поскольку работает по `workflow_dispatch` только на `main`.
- Подробный план и точная граница доказанных проверок:
  [docs/P0010_WINDOWS_QA_RELEASE_GATE.md](https://github.com/sunpole/uUnityBridge/blob/feature/p0010-safe-release-deploy/docs/P0010_WINDOWS_QA_RELEASE_GATE.md).
- **Безопасный порядок:**
  1. Завершить код-ревью и **отдельный QA в изолированной VM** под
     обычным Windows-пользователем. Проверить настоящие EXE 0.4.2/0.2.1,
     GUI, старый updater 0.4.0, backup, rollback, Named Pipe,
     поведение при блокировках и перезагрузке.
  2. Проверить и защитить права `main`, release-workflow и публикации
     assets; для production нужна подпись/аттестация происхождения.
  3. Только после PASS этих ворот объединить **тот же** PR #25 и
     повторить main CI; затем опубликовать стабильный `v0.4.3`
     через существующий `release-windows.yml` с точным version.
  4. Проверить tag SHA, успешный release run, опубликованные asset
     ID и SHA-256 digest для Desktop, Updater, Agent и ZIP.
  5. **С явного действия пользователя** открыть Desktop 0.4.2;
     старый updater 0.4.0 заменяет **Desktop**, но не себя.
     В новом Desktop 0.4.3 через GUI «Обновить агент»
     отдельно подтвердить upgrade Agent 0.2.1 с резервной копией
     и проверкой `health`.
  6. Локально разрешить **отдельную пустую тестовую папку**,
     проверить новый read-only `status` и только затем реальный
     типизированный `install_release`, повторы и rollback.
  7. Скриншотер/GUI launch — следующие отдельные этапы, не P0010.
- **ПК пользователя не менялся:** по последней диагностике
  установлены Desktop `0.4.2.0`, Agent `0.2.1.0`,
  Updater `0.4.0.0`. Этот чат не заменял файлы и не отправлял
  задания установки. Локальная копия `CONTINUE_HERE.md`
  **этой записью не синхронизирована**; актуальная копия в GitHub main.
- **Стоп-условие:** не сливать Draft PR и не публиковать новый
  стабильный Release лишь на основании зелёного CI:
  реальные VM/user-session E2E и происхождение assets ещё не проверены.

## Актуальная контрольная точка — 2026-11-01, P0010-C (PR #25)

**Результат:** исправления безопасности и логики разрешений реализованы;
Windows CI **3/3 PASS**. Тест удалённого `status` **PASS**.
**Локальное обновление агента и установка Release на пользовательском ПК НЕ выполнялись.**

- **GitHub:** существующий draft PR [#25](https://github.com/sunpole/uUnityBridge/pull/25),
  ветка `feature/p0010-safe-release-deploy`, актуальный HEAD
  `39276b607bcaadec4a0f571bd3f707c9804d4323`.
  PR не слит, новых стабильных Release нет; действующий выпуск `v0.4.2`.
- **Исправление 1 — безопасный отзыв разрешений:** окно
  `src/DeploymentApprovalDialog.cs` больше не создаёт профиль при
  нажатии «Запретить профиль» и не подменяет сохранённый путь или
  GitHub-репозиторий пустыми/текущими полями GUI.
  Новый чистый модуль `src/DeploymentPermissionData.cs` меняет
  только поле `enabled=false` у **единственного существующего** ID;
  чужой schema, дубли и отсутствующий ID блокируются. Тесты
  в `tests/uUnityBridge.Tests` проверяют сохранение всех полей,
  других профилей и отказные сценарии. Промежуточный тест упал
  из-за сравнения пробелов в сериализованном JSON; тест исправлен
  на сравнение семантических значений; итоговый CI PASS.
- **Исправление 2 — происхождение бинарного релиза:**
  `agent/ReleaseDeployment.cs` теперь требует опубликованный GitHub API
  `asset.digest` формата SHA-256, идентичный хешу задания.
  Если digest отсутствует/не совпадает, установка блокируется.
  `agent/DeploymentSelfTests.cs` проверяет совпадение, отсутствие и
  неправильный digest. ZIP-проверки и recovery-тесты предыдущего этапа
  сохранены. Документ `docs/P0010_RELEASE_DEPLOY.md` обновлён в PR.
- **Проверенный CI для HEAD:**
  [Windows desktop #41328190929](https://github.com/sunpole/uUnityBridge/actions/runs/41328190929) — PASS;
  [Windows Agent #41328190969](https://github.com/sunpole/uUnityBridge/actions/runs/41328190969) — PASS;
  [modular manifest #41328190871](https://github.com/sunpole/uUnityBridge/actions/runs/41328190871) — PASS.
- **Онлайн-связь с компьютером:**
  новый read-only [R000005 / Issue #29](https://github.com/sunpole/uUnityBridge/issues/29)
  закрыт агентом с `Status: PASS`: подтверждена авторизация GitHub,
  доступ к `sunpole/uUnityBridge` и канал ANS-ответов.
  Задание `status` не выполняло установку или изменение файлов ПК.
- **Изменения на ПК:** отсутствуют в этом этапе; никаких новых EXE,
  локальных скриптов, удалённых команд или обновлений не отправляли.
  По последней диагностике пользователя рабочие версии:
  Desktop `0.4.2.0`, Agent `0.2.1.0`, Updater `0.4.0.0`.
  Локальный `CONTINUE_HERE.md` не синхронизирован этой работой.
- **Следующие ворота:** прежде чем merge и stable Release, провести
  изолированный Windows E2E: auto-update Desktop, одноразовый GUI-bootstrap
  агента, сохранение/откат занятого EXE, действительная установка
  `install_release` в отдельно разрешённую тестовую папку.
  CI **не является** доказательством этих действий.
  Скриншотер и запуск GUI остаются последующими отдельными этапами.

## Самая свежая контрольная точка — 2026-11-01, P0010-B

**Статус: GUI-bootstrap агента + обновление релизного процесса закодированы, Windows CI PASS.
Полноценный E2E установки на ПК НЕ ПРОВЕРЕН. PR #25 остаётся Draft.**

- Продолжен **тот же PR #25**, ветка `feature/p0010-safe-release-deploy`,
  без Codex и без второго деплой-модуля.
- Добавлен `src/AgentBootstrapper.cs`: в будущем Desktop 0.4.3 через GUI
  `Обновить агент` сможет после явного согласия получить **Agent + новый updater helper**
  из того же проверенного стабильного Release. Проверяются GitHub Release,
  commit, успешный release workflow, размеры и SHA-256 обоих assets.
- Добавлен `updater/AgentUpdater.cs`: отдельный helper использует одноразовое
  локальное разрешение на конкретный Agent.exe, делает резервную копию,
  останавливает только точно совпадающий процесс в текущей сессии Windows,
  заменяет файл, проверяет IPC `health`; при неудаче пытается откатить.
  Неиспользуемый старый Desktop updater не перезаписывается.
- Релизный workflow дополнен публикацией `uUnityBridge.Agent.exe` и
  `uUnityBridge.Agent.exe.sha256`; CI дополнен проверкой отказа при отсутствии
  локального разрешения. **Новый stable Release пока НЕ создавался.**
- Документация реализации: `docs/P0010_RELEASE_DEPLOY.md` в feature-ветке.
- Проверенный HEAD PR: `774dc56954deab3ab7374c99c773fa064155fb4d`.
- **CI PASS:** [Desktop #41324939846](https://github.com/sunpole/uUnityBridge/actions/runs/41324939846),
  [Agent #41324939853](https://github.com/sunpole/uUnityBridge/actions/runs/41324939853).
- **Реальный status relay PASS:** [R000003 / Issue #27](https://github.com/sunpole/uUnityBridge/issues/27).
  Агент на ПК ответил `Status: PASS`, подтвердил учётную запись GitHub
  и ветку main; Issue закрыт. Это доказывает **связь**, но НЕ установку.
- Последний стабильный Release всё ещё `v0.4.2`, установленная версия по
  последнему сообщению пользователя Desktop 0.4.2 / Agent 0.2.1.
  Вся новая функциональность пока находится только в draft PR, не на ПК.
- **Следующие ворота:** в изолированном тестовом каталоге проверить Desktop
  автообновление 0.4.2 → 0.4.3, однократный GUI bootstrap Agent 0.2.1 → 0.3.0,
  откат при сбое и живое типизированное `install_release` Issue в
  заранее локально разрешённую тестовую папку. Не начинать скриншотер
  до подтверждённого реального E2E.
- Источник актуального документа — GitHub `main`. Локальная копия
  `C:\\!CODE_CLUB\\new 2026\\017_uunity\\CONTINUE_HERE.md`
  **этим изменением автоматически не синхронизировалась**: агент 0.2.1
  не поддерживает доставку Markdown или новые действия.

## Текущий журнал продолжения — 2026-11-01, P0010 / PR #25

**Этап P0010-A: проверка и восстановление прерванного развёртывания — CI PASS, локальный E2E НЕ подтверждён.**

- Продолжена **существующая** ветка `feature/p0010-safe-release-deploy`, без второго модуля доставки.
- В `agent/DeploymentTransaction.cs` добавлен журнал незавершённой операции `.deploy-pending.json`:
  после сбоя между перемещениями каталогов следующий запуск восстанавливает предыдущий
  управляемый `current` из резервной копии, а после записи нового состояния завершает
  коммит без повторной установки. Повреждённый журнал блокируется.
- `agent/DeploymentTransactionTests.cs` имитирует сбой после перемещения старой версии,
  после перемещения новой, после фиксации состояния; проверяет откат и отказ от
  небезопасного пути в журнале. Тесты работают только во временной папке Windows CI.
- Head PR после этапа: `5bc0a5cf4d50bab56a543e5b7ba7484bd51dbef2`.
- Windows Actions: [agent tests #41324140897](https://github.com/sunpole/uUnityBridge/actions/runs/41324140897)
  и [desktop build #41324140900](https://github.com/sunpole/uUnityBridge/actions/runs/41324140900) — success.
- **Ничего не загружалось и не устанавливалось на ПК пользователя.** Agent 0.2.1
  по-прежнему не поддерживает `install_release`; GUI 0.4.3 и Agent 0.3.0 остаются в draft PR.
- Следующий блокирующий этап: **однократный безопасный bootstrap агента через GUI и
  существующий автоапдейтер desktop**, без ручного скачивания ZIP/EXE,
  с сохранением рабочего uUnityBridge 0.4.2 и автозапуска агента.
  После реализации и CI провести тест на отдельном Windows QA-каталоге.

Все старые подтверждения ниже — историческая контрольная точка, а не текущее
состояние релиза или установленного ПО.

## 1. Главная цель — НЕ подменять её разработкой автономной модели

Пользователь хочет из обычного чата ChatGPT, включая телефон, выполнять повторяемый цикл:

1. Пользователь пишет задачу на русском («Исправь интерфейс uCode и покажи результат»).
2. ChatGPT проектирует изменение, правит исходники **в GitHub**, создаёт PR, запускает/проверяет Windows CI.
3. После проверенного Release ChatGPT создаёт **строго типизированное** задание для локального Windows-агента в GitHub.
4. Агент, работающий в фоне на ПК пользователя, сам читает задание (исходящий доступ к GitHub), устанавливает только одобренную сборку в заранее разрешённую папку, при необходимости запускает тестируемое приложение.
5. Агент выполняет разрешённые тесты/GUI-действия, делает скриншот **окна именно этого приложения**, возвращает отчёт и, с разрешения пользователя, изображение через приватное хранилище.
6. ChatGPT читает отчёт и изображение, предлагает следующую правку и повторяет цикл.
7. Пользователь видит и оценивает результат, когда нужна содержательная/визуальная человеческая проверка.

**Ключевое требование:** минимум AnyDesk, CMD, PowerShell, ручных загрузок EXE и переноса файлов. После первоначальной настройки обычный цикл должен выполняться по сообщению в ChatGPT. Подключать отдельную модель к локальному агенту для этого базового процесса **не требуется**. Локальный агент — исполнитель ограниченных операций, ChatGPT — разработчик.

## 2. Архитектура

```text
Пользователь (мобильный ChatGPT)
            |
     ChatGPT + GitHub
     код / PR / Windows CI / Releases
            |
   GitHub Issue: типизированное задание
            |
   исходящее соединение, polling ~60 секунд
            |
   uUnityBridge.Agent (Windows, обычный пользователь)
            |                   |
       локальный API        зарегистрированные
       Named Pipe          проекты / пути / права
            |                   |
   GitHub CLI (gh)      разрешённые сборки, тесты, окна
            |                   |
   статус/отчёт в GitHub  скриншоты только окна
            |                   |
            +-----> приватное хранилище снимков
            |
   ChatGPT проверяет результат и продолжает
```

Сейчас **работает только путь передачи задания типа `status` и возврата статуса**. Фоновый агент НЕ является универсальным удалённым PowerShell-сервером. Мост через GitHub уже рабочий и **не требует входящего публичного порта или ngrok**. Прямое MCP-соединение с ChatGPT — возможное улучшение позже, не обязательное для первого полноценного цикла.

Важно: обычный чат ChatGPT не выполняет работу бесконечно в фоне между сообщениями. Агент способен работать на ПК в фоне; чтобы ChatGPT проанализировал результат, нужен новый запрос пользователя или отдельно настроенный поддерживаемый механизм продолжения/автоматизации. Не обещать невидимую бесконечную автономную работу чата.

## 3. Подтверждённые факты и версии (на 01.11.2026)

### На компьютере — подтверждено пользователем
- Рабочая основная программа: **uUnityBridge.exe 0.4.2**.
- Установлен и запущен агент: **uUnityBridge.Agent.exe 0.2.1**; локальный API проверен.
- Ярлык автозапуска Windows создан, но **поведение после реальной перезагрузки ещё не проверено**.
- Успешная самопроверка агента (`--self-test`) и локальная проверка IPC (`--request health`).
- Резервная копия прежнего агента создана в локальном каталоге резервных копий.
- Пользователь ранее столкнулся с блокировкой запущенного EXE и `NamedPipeClientStream UnauthorizedAccessException`. Исправления в PR #22 включены в `main`. Нельзя снова перезаписывать работающий EXE или ослаблять права Named Pipe.
- Локальная история главной программы находится в `%LOCALAPPDATA%\uUnityBridge`: `answers.json`, `report-cursor.json`, файлы `.bak`, `updater-last.txt`. Это **не полноценный журнал агента**.
- Рабочая установка: `C:\!CODE_CLUB\new 2026\017_uunity\uUnityBridge_App`. Не удалять/не перезаписывать её без отдельной проверки и разрешения.

### Через GitHub — подтверждено
- Репозиторий: https://github.com/sunpole/uUnityBridge
- Текущий `main` на момент проверки: `599ef40f4a938523a1cd579b186323e952e6b1b7` (перед добавлением этого документа).
- Последний **опубликованный стабильный Release**: `v0.4.2` (для основной программы).
- Удалённая задача R000001: https://github.com/sunpole/uUnityBridge/issues/21 — закрыта агентом, статус **PASS**.
- Удалённая задача R000002: https://github.com/sunpole/uUnityBridge/issues/24 — закрыта **постоянным Agent 0.2.1**, статус **PASS**. Этот тест подтвердил полноценную цепочку ChatGPT → GitHub → ПК → GitHub.
- Рабочий исходящий канал выполняет только `status`, проверяет автора `sunpole`, ограниченную JSON-схему и публикует идемпотентный ANS-комментарий.
- Задание дальнейшей разработки: https://github.com/sunpole/uUnityBridge/issues/23 — **P0011**, открыто.
- Основные документы: `docs/P0008_AGENT_FOUNDATION.md`, `docs/P0009_RELAY.md`, `docs/LOCAL_API_MCP.md`, `docs/UPDATE_ARCHITECTURE_V2.md`.

### Очень важно: активная работа, которую НЕ нужно начинать заново
- **Draft PR #25:** https://github.com/sunpole/uUnityBridge/pull/25
- Тема: P0010 — GitHub Release → установка сборки в локально разрешённую папку.
- Ветка: `feature/p0010-safe-release-deploy`; просмотренный head: `9bc9bde14775c6f506a88ec9d5ef03a8a6ed5bfd`.
- На 01.11.2026 в ветке уже реализуются: GUI «Папка сборок», локальный allowlist-профиль, операция `install_release`, проверки метаданных Release, SHA-256, ZIP, staged-доставка, локальный откат и QA-тесты распаковки.
- Windows CI этой ветки: https://github.com/sunpole/uUnityBridge/actions/runs/41322242981 и https://github.com/sunpole/uUnityBridge/actions/runs/41322242904 — **success**.
- **PR остаётся draft и не слит**. Его код **не установлен** на ПК. Там указаны preview-версии **Agent 0.3.0** и **Desktop 0.4.3**; они **не являются выпущенными стабильными релизами**.
- Подробности: https://github.com/sunpole/uUnityBridge/blob/feature/p0010-safe-release-deploy/docs/P0010_RELEASE_DEPLOY.md

## 4. Следующий шаг — закончить P0010 / PR #25

**Это задача №1 для нового чата; не создавать второй параллельный модуль деплоя.**

Последовательность:

1. Снова проверить содержимое PR #25, точные head SHA, CI и замечания. Провести код-ревью границ доверия `install_release`, локального профиля, проверки Release/CI, SHA-256 и ZIP.
2. Убедиться, что GitHub Issue **не может** передать произвольный путь, URL, PowerShell, CMD, shell, процесс, GitHub-репозиторий или новый профиль разрешений.
3. Запретить небезопасные ZIP-пути, относительный выход за каталог, симлинки, альтернативные потоки и чрезмерные размеры. Проверить защёлку от двойной установки, повреждённого ZIP, плохого SHA и обрыва загрузки.
4. CI должен покрывать happy path, отказные сценарии, повторное задание, откат и необрабатываемые форматы. Сборка и собственные тесты **не подтверждают** установку на реальном ПК.
5. Подготовить **отдельную тестовую папку**, а не менять действующую `uUnityBridge_App`. Локальное разрешение установки задаётся пользователем через GUI.
6. Сделать совместимый переход с установленного Agent 0.2.1 на новый проверенный агент, **не заменяя запущенный EXE**; при ошибке — возврат к старому агенту и сохранение данных/автозапуска.
7. Визуально проверить GUI-профиль, провести реальный E2E «создать типизированный GitHub Issue → агент скачал конкретный Release → проверил и установил в тестовый каталог → ответил в GitHub», затем повторить без дополнительных команд.
8. Только после PASS по всем воротам готовности переводить PR из draft, выполнять merge и публиковать новый проверенный Release по существующему процессу.

**Критерий PASS P0010:** после однократного локального разрешения пути пользователь просит в ChatGPT установить проверенный Release, агент делает это в нужной тестовой папке и сообщает версию, SHA, статус, ошибки/откат в GitHub. Никакого ручного PowerShell на обычном цикле.

## 5. Следующие этапы после успешного P0010

### P0012 — надёжный агент, автозапуск и самообновление
- Лаунчер/проверенный установщик, version slots, staging и атомарное переключение вместо перезаписи занятого EXE.
- Проверка версии при старте, сохранение `%LOCALAPPDATA%\uUnityBridge`, резервной версии и возможности отката.
- Контроль одной копии агента, одинакового стандартного пользовательского контекста, тест после перезапуска Windows.
- GUI-индикаторы: агент онлайн/офлайн, последняя синхронизация, версия, текущая задача, состояние GitHub.

### P0013 — постоянные логи и диагностика
- Локальный структурированный журнал агента (JSONL) в `%LOCALAPPDATA%\uUnityBridge\agent\logs` с ротацией и ограничением размера.
- Поля: UTC, идентификатор задачи, этап, состояние, версия приложения, commit SHA, код ошибки, длительность; без токенов, паролей, содержимого личных файлов и неотредактированных снимков.
- Экран «Журнал» в GUI, фильтры, экспорт безопасного отчёта в GitHub и кнопка «Скопировать отчёт».
- Тесты повреждённых логов и ограничений размера; исходные логи остаются локальными.

### P0014 — запуск приложения и проверка GUI
- Реестр доверенных проектов/путей на ПК, запуск только заранее разрешённого EXE **как обычный пользователь**.
- Тайм-ауты, обнаружение окна, контроль PID, проверка процесса, остановка только своего тестового процесса.
- Типизированные GUI-действия по разрешённым элементам. **Не** открывать общий удалённый Desktop/PowerShell.

### P0015 — скриншотер и обратный визуальный цикл
- Снимок только выбранного окна тестируемого ПО; не всего экрана, не чужих окон и не фоновой съёмкой.
- Проверка приватности и ограничение отправки изображений: отдельный **приватный** репозиторий/хранилище с явным разрешением пользователя. **Не загружать скриншоты в публичный `sunpole/uUnityBridge`**.
- Агент публикует в GitHub Issue безопасный результат со ссылкой на снимок; ChatGPT может открыть изображение, оценить интерфейс, исправить GitHub-код и повторить цикл.

### P0016 — управление циклом и качество
- Задачи с idempotency-key: NEW → RUNNING → PASS/BLOCKED/NEEDS_REVIEW; защита от повторов и гонок.
- Критерии качества: CI PASS, локальная установка PASS, GUI проверен, визуальная проверка пройдена; остановка при ошибках или необходимости человеческого решения.
- Сверить реальный пользовательский сценарий: **одно сообщение в ChatGPT → код/PR/CI → Release → ПК → окно/скриншот → ответ**, без обычного использования AnyDesk/PowerShell.

### Опционально позже — прямой MCP
Через поддерживаемое защищённое подключение можно заменить задержку опроса GitHub на прямые инструменты. Но **не открывать** универсальный удалённый терминал, административный PowerShell, доступ к секретам или ngrok на общий shell.

## 6. Единая политика безопасности

- Вход из GitHub — только данные по заранее известной схеме, а не инструкции для выполнения кода.
- Права агента — обычный Windows-пользователь. Админ-действия, если когда-нибудь потребуются, только отдельный локально подтверждаемый ограниченный broker.
- Разрешения на проект/папку выдаются **на ПК**, а не текстом в GitHub Issue.
- В публичном GitHub не размещать токены, секреты, частные логи, системные пути с личными данными, скриншоты рабочего стола.
- Раздельно оценивать: исходники доступны, CI успешно, PR слит, релиз опубликован, программа установлена и реально протестирована. Это **разные** утверждения.
- При сбое Windows не удалять рабочую установку и не перезаписывать занятый EXE. Сначала диагностика и backup.

## 7. Как быстро проверить проект в новом чате

1. Прочитать этот файл **целиком**.
2. Проверить `main`, latest Release, открытые PR и Issue #23; особое внимание PR #25 и `docs/P0010_RELEASE_DEPLOY.md`.
3. Проверить актуальные GitHub Actions run-и и различать CI в feature-ветке и CI в main.
4. Для проверки онлайн-связи создать **новое** задание `status` в формате `[UUB JOB R######]` с JSON `{"schema":"uub.agent.job.v1","id":"R######","action":"status"}`; никогда не переиспользовать R000001/R000002 и не считать пустое Issue подтверждением.
5. Прочитать ANS-комментарий и статус Issue. При отсутствии ответа проверить: компьютер включён, пользователь Windows вошёл, агент запущен, `gh` авторизован, нет ошибок сети.
6. Не предлагать пользователю запускать Codex. Если понадобится одноразовая установка обновления, подготовить минимальный проверяемый GUI/установщик или исключительно безопасную одноразовую команду.
7. По окончании каждого реального этапа **обновить этот документ**: установленная версия, Release, PR, CI, локальный QA, подтверждение от пользователя, ограничения и следующая задача.

## 8. Промпт для нового чата — копировать

```text
Продолжи проект sunpole/uUnityBridge БЕЗ Codex.

Открой и прочитай полностью:
https://github.com/sunpole/uUnityBridge/blob/main/CONTINUE_HERE.md

Это главный документ передачи проекта.
Проверь реальные main, открытые PR, CI, Releases и Issue #23.
Не полагайся на устаревшие версии или обещания.

Главная цель: я пишу задачу в ChatGPT (часто с телефона).
Ты разрабатываешь код в GitHub; мой uUnityBridge Agent на Windows
сам получает проверенную сборку, устанавливает её в разрешённый
каталог, запускает приложение, снимает окно и возвращает отчёт.
Без ручных PowerShell/AnyDesk на каждом цикле.

Приоритет: закончить существующий draft PR #25 (P0010);
не начинать деплой заново, не трогать рабочую 0.4.2 без QA.
Затем реализовать журнал агента, запуск GUI и скриншотер.

Действуй через GitHub, используй отдельные PR и проверяй Windows CI.
Пиши, что действительно реализовано и проверено, а что ещё нет.
Когда меняется подтверждённое состояние — обновляй CONTINUE_HERE.md.
```

## 9. Где хранить документ на ПК

Рекомендуемая локальная копия: `C:\!CODE_CLUB\new 2026\017_uunity\CONTINUE_HERE.md`
(рядом с папкой `uUnityBridge_App`, **не внутри установленного приложения**).

Единственный источник актуального текста — GitHub `main`.
После изменения документа в GitHub локальную копию можно обновить одной
операцией скачивания **только Markdown**, без установки/запуска новых EXE.
Впоследствии добавить в uUnityBridge отдельную кнопку «Открыть план проекта»
и автоматическое чтение этого Markdown из доверенного репозитория.

---
**Последняя проверка источников:** GitHub Repo/main, PR #25 и CI ветки, Issues #21/#23/#24, Releases. Изменение документа само по себе не означает, что новая функция агента установлена.
