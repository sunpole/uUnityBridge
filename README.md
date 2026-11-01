# uUnityBridge — GitHub ↔ Windows Agent ↔ Unity (разработка)

> **Главная цель:** из ChatGPT безопасно изменять настоящий Unity-проект через
> контролируемый Unity Editor C# API, видеть результат в Unity и получать
> согласованный снимок только окна Unity обратно в ChatGPT. **В настоящее
> время эта цепочка ещё не реализована.**

## Текущее состояние · 1 ноября 2026

| Компонент | Проверенное состояние |
| --- | --- |
| Desktop | Последний стабильный [v0.4.2](https://github.com/sunpole/uUnityBridge/releases/tag/v0.4.2), на ПК подтверждён `0.4.2.0` |
| Updater | На ПК был `0.4.0.0`; обновляет Desktop, а не установленный Agent |
| Agent | На ПК подтверждён `0.2.1.0`, GitHub Issue `status` [PASS](https://github.com/sunpole/uUnityBridge/issues/29) |
| P0010 | [Draft PR #25](https://github.com/sunpole/uUnityBridge/pull/25): новый безопасный deploy и Agent bootstrap, CI PASS, реальный Windows E2E ещё не пройден |
| Unity Editor / сцены / скриншоты | **Draft [PR #30](https://github.com/sunpole/uUnityBridge/pull/30)**: Editor package 0.1.0 и четыре команды, [контрактный CI PASS](https://github.com/sunpole/uUnityBridge/actions/runs/41339356505). **Unity Hub/Editor у пользователя ещё не установлены, реальная сцена не проверена; скриншотов и удалённого Unity API нет** |

Проверка CI — не доказательство, что программа обновлена на вашем ПК.
Не заменяйте вручную рабочие EXE и не используйте PR-артефакты как
стабильный выпуск. Установленный Agent 0.2.1 **не** умеет создавать Unity-сцены.

## История и правила (читать в этом порядке)

1. [version.html](version.html) — наглядная статическая история версий и целей.
2. [version.md](version.md) — что разрабатывалось, проверено и остаётся долгом.
3. [version.json](version.json) — машинный документальный снимок и критерии завершения.
4. [CONTINUE_HERE.md](CONTINUE_HERE.md) — полная передача контекста следующему чату.
5. [AGENTS.md](AGENTS.md) — обязательные правила разработки и уведомления о завершении.
6. [docs/GOALS_AND_ACCEPTANCE.md](docs/GOALS_AND_ACCEPTANCE.md) — результат,
   который должен реально увидеть пользователь на каждом этапе.
7. [agent/GOAL_REPORTING.md](agent/GOAL_REPORTING.md) — политика доказательств
   для будущих версий локального агента (**ещё не исполняется Agent 0.2.1**).

## Ближайшая практическая работа

1. Установить **официальный Unity Hub и Unity 6.3 LTS**; создать **отдельный тестовый** Unity-проект `uUnityDemo` и открыть его в настоящем Editor.
2. Проверить **уже созданный в Draft PR #30** ограниченный Unity Editor package с командами создания и сохранения тестовой сцены **в настоящем Editor**. Windows CI не является этой проверкой.
3. Довести безопасный release/agent bootstrap PR #25 до отдельного Windows E2E.
4. Связать API, GUI статуса и согласованный снимок **одного разрешённого окна**
   с GitHub-мостом. Скриншоты не отправлять в публичные Issues.

Не создавать универсальный PowerShell-сервер или выполнять код из GitHub Issue.
GitHub — обмен типизированными заданиями, не источник новых локальных полномочий.

## Архив прежних технических документов

- [Автоматический PATCH/ANS](docs/P0003_AUTOPILOT.md)
- [Проверки старого обновлятора](docs/P0004_UPDATER_QA.md)
- [Исторические выпуски](docs/HISTORICAL_RELEASES.md)
- [Текущий агент GitHub relay](docs/P0009_RELAY.md)
- [План API / MCP](docs/LOCAL_API_MCP.md)
- [P0010 Windows QA](https://github.com/sunpole/uUnityBridge/blob/feature/p0010-safe-release-deploy/docs/P0010_WINDOWS_QA_RELEASE_GATE.md)

**Когда все согласованные цели, долги и патчи доказанно завершены:**
сообщить пользователю **«ТЕКУЩИЙ НАБОР ЦЕЛЕЙ ЗАВЕРШЁН. НУЖНЫ НОВЫЕ ЦЕЛИ»**.
Проактивные уведомления требуют отдельно включённой автоматизации.
