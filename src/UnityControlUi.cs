using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace uUnityBridge;

internal static class UnityLocalClient
{
    // Calls only the current-user named pipe, not HTTP or a shell.
    internal static async Task<string> RequestAsync(string action)
    {
        if (action is not ("unity_status" or "unity_project_create" or "unity_project_open"))
            throw new InvalidDataException("Команда не разрешена.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var pipe = new NamedPipeClientStream(".", "uUnityBridge.agent.v1",
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000, timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
        { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { action }));
        var text = await reader.ReadLineAsync(timeout.Token) ??
            throw new IOException("Агент не ответил.");
        if (text.Length > 4096) throw new InvalidDataException("Ответ слишком большой.");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        string status = root.GetProperty("status").GetString() ?? "BLOCKED";
        string error = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
        string result = status switch
        {
            "PASS" => "Unity Editor подключён и отвечает.",
            "ACCEPTED" => "Задание принято агентом. Это не подтверждение завершения.",
            "BLOCKED" => "Действие заблокировано: " + error,
            _ => "Старая версия агента не поддерживает Unity API: " + error
        };
        return result;
    }
}

internal static class UnityConsentDialog
{
    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uUnityBridge", "unity-approval.json");

    internal static void Show(IWin32Window owner)
    {
        using var dialog = new Form
        {
            Text = "uUnityBridge · локальное разрешение Unity",
            Size = new Size(670, 280), MinimumSize = new Size(580, 260),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false
        };
        var title = new Label
        {
            Left = 18, Top = 12, Width = 620, Height = 50,
            Text = "Разрешить только тестовый проект uUnityDemo.\n" +
                   "ChatGPT и GitHub не могут менять путь или запускать PowerShell."
        };
        var path = new TextBox
        {
            Left = 18, Top = 75, Width = 510, ReadOnly = true,
            Text = "Папка родительского каталога не выбрана"
        };
        var choose = new Button { Left = 540, Top = 73, Width = 104, Text = "Выбрать" };
        var allow = new Button { Left = 18, Top = 140, Width = 190, Height = 38,
            Text = "Разрешить Unity" };
        var deny = new Button { Left = 220, Top = 140, Width = 190, Height = 38,
            Text = "Отключить" };
        var close = new Button { Left = 423, Top = 140, Width = 220, Height = 38,
            Text = "Закрыть" };

        choose.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog
            {
                Description = "Выберите папку, внутри которой будет создан uUnityDemo",
                ShowNewFolderButton = false
            };
            if (picker.ShowDialog(dialog) == DialogResult.OK)
                path.Text = picker.SelectedPath;
        };

        allow.Click += (_, _) =>
        {
            try
            {
                string parent = path.Text;
                ValidateParent(parent);
                if (MessageBox.Show(dialog,
                    "Разрешить агенту создавать и открывать ТОЛЬКО uUnityDemo\nв папке:\n" +
                    parent + "\n\nНикакого удалённого PowerShell или установки программ.",
                    "Локальное разрешение Unity",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                Save(parent, enabled: true);
                MessageBox.Show(dialog, "Разрешение сохранено только на этом ПК.");
                dialog.Close();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidDataException)
            {
                MessageBox.Show(dialog, "Папка не разрешена: " + e.Message);
            }
        };

        deny.Click += (_, _) =>
        {
            try
            {
                string? original = ReadPreviousParent();
                if (original is null) throw new InvalidDataException("Нет сохранённого разрешения.");
                Save(original, enabled: false);
                MessageBox.Show(dialog, "Разрешение Unity отключено.");
                dialog.Close();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidDataException or JsonException)
            {
                MessageBox.Show(dialog, "Не удалось отозвать разрешение: " + e.Message);
            }
        };
        close.Click += (_, _) => dialog.Close();
        dialog.Controls.AddRange([title, path, choose, allow, deny, close]);
        dialog.ShowDialog(owner);
    }

    private static string? ReadPreviousParent()
    {
        if (!File.Exists(SettingsFile)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
        if (doc.RootElement.GetProperty("schema").GetString() != "uub.unity.approval.v1")
            throw new InvalidDataException("Некорректный файл разрешения.");
        return doc.RootElement.GetProperty("projectParent").GetString();
    }

    private static void Save(string parent, bool enabled)
    {
        ValidateParent(parent);
        string directory = Path.GetDirectoryName(SettingsFile)!;
        Directory.CreateDirectory(directory);
        string staging = SettingsFile + ".next";
        if (File.Exists(staging))
            throw new InvalidDataException("Есть незавершённая запись разрешения.");
        File.WriteAllText(staging, JsonSerializer.Serialize(new
        {
            schema = "uub.unity.approval.v1", enabled,
            projectParent = Path.GetFullPath(parent), projectName = "uUnityDemo",
            editorVersion = "6000.6.7f1"
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(staging, SettingsFile, overwrite: true);
    }

    private static void ValidateParent(string parent)
    {
        if (!Path.IsPathFullyQualified(parent) || !Directory.Exists(parent))
            throw new InvalidDataException("Выберите существующую абсолютную папку.");
        string root = Path.GetPathRoot(parent) ?? "";
        string full = Path.GetFullPath(parent);
        if (full.TrimEnd(Path.DirectorySeparatorChar).Equals(
            root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Корень диска запрещён.");
        for (string? part = full; part is not null; part = Path.GetDirectoryName(part))
        {
            if (File.GetAttributes(part).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Папки-ссылки запрещены.");
            if (part.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
        string project = Path.Combine(full, "uUnityDemo");
        if (Directory.Exists(project) &&
            File.GetAttributes(project).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Проект является папкой-ссылкой.");
    }
}
