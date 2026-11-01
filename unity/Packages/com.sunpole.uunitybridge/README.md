# uUnityBridge Unity Editor MVP — версия пакета 0.1.0 (тестовая)

**Состояние:** исходный код в GitHub feature branch. **Ещё не запускался в настоящем Unity Editor**. Пакет не подключён к установленному Windows Agent и не принимает команды из ChatGPT через сеть.

## Что будет видно после установки пакета

Unity Editor: меню **Tools → uUnityBridge**:
1. **Проверить Unity** — версия Unity и статус текущей сцены, только чтение.
2. **Создать тестовую сцену** — только в `Assets/uUnityDemo/Scenes/uUnityDemo.unity`, камера и три 2D-платформы.
3. **Добавить платформу** — только в открытой сцене `uUnityDemo.unity`; размеры и координаты ограничены.
4. **Сохранить сцену** — сохраняет только открытую демонстрационную сцену.

Пакет запрещает изменение другой сцены, перезапись уже существующей демо-сцены, потерю несохранённой сцены, не принимает пути/URL или произвольные команды.
Возвращает JSON-ответы `PASS` или `BLOCKED` с кодом причины.

## Как установить позже в отдельный тестовый проект

1. Установить **официальный Unity Hub**, **Unity 6.3 LTS** и создать пустой проект `uUnityDemo` (шаблон 2D).
2. В Unity выбрать **Window → Package Manager → Install package from git URL**.
3. Вставить URL Git из **конкретного проверенного коммита**, содержащего пакет:
   `https://github.com/sunpole/uUnityBridge.git?path=/unity/Packages/com.sunpole.uunitybridge#<commit-sha>`
   (заменить `<commit-sha>` после проверки текущего PR, а не копировать текст-заглушку).
4. Найти меню **Tools → uUnityBridge** и запустить создание сцены.

**Важно:** установка по Git URL и Unity API ещё не прошли фактический E2E. До этого не предлагать пакеты как стабильные или автоматические. Не устанавливать в существующие рабочие Unity-проекты.

## Ограничения

- Ни HTTP, ни MCP, ни GitHub relay, ни прямого соединения ChatGPT в этом MVP нет.
- Нет автоматического запуска Unity, захвата экрана, загрузки приватных данных, исполняемого шелла, C#-скриптов из Issue.
- Единый `UnityBridgePolicy.Parse` принимает только `unity_status`, `create_scene`, `create_platform`, `save_scene`.
- Статическая проверка контракта выполняется в обычном Windows CI **без установленного Unity**, что не заменяет запуск в реальном Editor.

Официально: https://docs.unity.com/en-us/hub/add-editor, https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.EditorSceneManager.html
