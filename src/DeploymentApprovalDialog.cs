using System.Text.Json;
using System.Text.RegularExpressions;

namespace uUnityBridge;

// This dialog is the LOCAL grant of file-system write permission.
// No GitHub Issue may create or edit a deployment profile.
internal static class DeploymentApprovalDialog
{
    private const string Schema = "uub.deploy.settings.v1";
    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uUnityBridge", "deployment-profiles.json");

    internal static void Show(IWin32Window owner)
    {
        using var dialog = new Form
        {
            Text = "Разрешённая установка GitHub-сборок",
            Width = 760, Height = 525,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            Font = new Font("Segoe UI", 10)
        };
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(15),
            ColumnCount = 2, RowCount = 9
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++)
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        TextBox Input(string label, int row, string value)
        {
            fields.Controls.Add(new Label
            {
                Text = label, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);
            var input = new TextBox { Text = value, Dock = DockStyle.Fill };
            fields.Controls.Add(input, 1, row);
            return input;
        }

        var id = Input("Имя профиля", 0, "uunitybridge");
        var repository = Input("Репозиторий GitHub", 1, "sunpole/uUnityBridge");
        var archive = Input("ZIP-файл Release", 2, "uUnityBridge-windows-x64.zip");
        var entrypoint = Input("Основной EXE", 3, "uUnityBridge.exe");
        var workflow = Input("CI workflow (.yml)", 4, "release-windows.yml");
        fields.Controls.Add(new Label
        {
            Text = "Разрешённая папка", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 5);
        var folderLine = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        folderLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        var folder = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        var pick = new Button { Text = "Выбрать...", Dock = DockStyle.Fill };
        folderLine.Controls.Add(folder, 0, 0);
        folderLine.Controls.Add(pick, 1, 0);
        fields.Controls.Add(folderLine, 1, 5);
        pick.Click += (_, _) =>
        {
            using var browser = new FolderBrowserDialog
            {
                Description = "Выберите отдельную пустую папку только для сборок этого приложения",
                ShowNewFolderButton = true
            };
            if (browser.ShowDialog(dialog) == DialogResult.OK)
                folder.Text = browser.SelectedPath;
        };

        var explain = new Label
        {
            Text = "Задание из GitHub сможет устанавливать только ZIP из указанного " +
                   "репозитория, прошедший CI и SHA-256 проверку. Путь хранится " +
                   "только на этом ПК. Нельзя разрешать системные папки или " +
                   "папки с личными файлами. Автоматический запуск EXE отключён.",
            AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(3)
        };
        fields.Controls.Add(explain, 0, 7);
        fields.SetColumnSpan(explain, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft
        };
        var cancel = new Button { Text = "Закрыть", Width = 115, Height = 32 };
        var grant = new Button { Text = "Разрешить", Width = 135, Height = 32 };
        var revoke = new Button { Text = "Запретить профиль", Width = 165, Height = 32 };
        actions.Controls.Add(cancel);
        actions.Controls.Add(grant);
        actions.Controls.Add(revoke);
        fields.Controls.Add(actions, 0, 8);
        fields.SetColumnSpan(actions, 2);
        cancel.Click += (_, _) => dialog.Close();

        List<JsonElement> LoadProfiles()
        {
            if (!File.Exists(SettingsFile)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            var root = doc.RootElement;
            if (root.GetProperty("schema").GetString() != Schema)
                throw new InvalidDataException("Неизвестный формат разрешений.");
            return root.GetProperty("profiles").EnumerateArray()
                .Select(x => x.Clone()).ToList();
        }

        void PersistProfiles(List<object> profiles)
        {
            if (profiles.Count > 20)
                throw new InvalidDataException("Достигнут лимит 20 профилей.");
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            string next = SettingsFile + ".next";
            File.WriteAllText(next, JsonSerializer.Serialize(new
            {
                schema = Schema, profiles
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(next, SettingsFile, overwrite: true);
        }

        void SaveProfile(bool enabled)
        {
            try
            {
                string profileId = id.Text.Trim();
                if (!Regex.IsMatch(profileId, @"^[a-z][a-z0-9-]{1,31}$"))
                    throw new InvalidDataException("Некорректное имя профиля.");

                if (!enabled)
                {
                    // Revoke the selected EXISTING profile without changing
                    // repository, asset, workflow or previously approved path.
                    var oldProfiles = LoadProfiles();
                    var matches = oldProfiles.Where(x =>
                        x.GetProperty("id").GetString() == profileId).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidDataException("Укажите существующий профиль для запрета.");
                    var original = matches[0];
                    var remaining = oldProfiles.Where(x =>
                        x.GetProperty("id").GetString() != profileId).Cast<object>().ToList();
                    remaining.Add(new
                    {
                        id = original.GetProperty("id").GetString(),
                        repository = original.GetProperty("repository").GetString(),
                        assetName = original.GetProperty("assetName").GetString(),
                        entrypoint = original.GetProperty("entrypoint").GetString(),
                        installRoot = original.GetProperty("installRoot").GetString(),
                        workflowFile = original.GetProperty("workflowFile").GetString(),
                        enabled = false
                    });
                    PersistProfiles(remaining);
                    MessageBox.Show(dialog, "Разрешение отозвано. Путь и параметры сохранены.",
                        "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    dialog.Close();
                    return;
                }

                if (!Regex.IsMatch(repository.Text.Trim(), @"^sunpole/[A-Za-z0-9_.-]+$") ||
                    !Regex.IsMatch(archive.Text.Trim(), @"^[A-Za-z0-9_.-]+\.zip$") ||
                    !Regex.IsMatch(entrypoint.Text.Trim(), @"^[A-Za-z0-9_. -]+\.exe$") ||
                    !Regex.IsMatch(workflow.Text.Trim(), @"^[A-Za-z0-9_.-]+\.yml$"))
                    throw new InvalidDataException("Проверьте репозиторий и имена файлов.");
                string target = folder.Text.Trim();
                if (!Path.IsPathFullyQualified(target) || !Directory.Exists(target))
                    throw new InvalidDataException("Сначала выберите существующую папку.");
                string root = Path.GetFullPath(target);
                if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetPathRoot(root)!.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Корень диска запрещён.");
                for (string? part = root; part is not null; part = Path.GetDirectoryName(part))
                {
                    if (Directory.Exists(part) &&
                        File.GetAttributes(part).HasFlag(FileAttributes.ReparsePoint))
                        throw new InvalidDataException("Папки-ссылки запрещены.");
                    if (Path.GetPathRoot(part) == part) break;
                }
                bool empty = !Directory.EnumerateFileSystemEntries(root).Any();
                bool alreadyManaged = File.Exists(Path.Combine(root, "state.json")) &&
                    File.Exists(Path.Combine(root, "current", ".uub-managed.json"));
                if (!empty && !alreadyManaged)
                    throw new InvalidDataException("Выберите пустую отдельную папку.");
                if (MessageBox.Show(dialog,
                    "Разрешить агенту устанавливать проверенные по SHA-256 " +
                    "ZIP-сборки из " + repository.Text.Trim() + " в выбранную папку?\n" +
                    "GitHub Issues не могут менять папку. Самостоятельный запуск EXE запрещён.",
                    "Подтверждение локального разрешения",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                var profiles = LoadProfiles().Where(x =>
                    x.GetProperty("id").GetString() != profileId).Cast<object>().ToList();
                profiles.Add(new
                {
                    id = profileId, repository = repository.Text.Trim(),
                    assetName = archive.Text.Trim(), entrypoint = entrypoint.Text.Trim(),
                    installRoot = target, workflowFile = workflow.Text.Trim(), enabled = true
                });
                PersistProfiles(profiles);
                MessageBox.Show(dialog,
                    "Разрешение сохранено на этом ПК. GitHub может отправить проверенное задание.",
                    "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                dialog.Close();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException or ArgumentException)
            {
                MessageBox.Show(dialog, ex.Message, "Изменение не сохранено",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        grant.Click += (_, _) => SaveProfile(true);
        revoke.Click += (_, _) => SaveProfile(false);
        dialog.Controls.Add(fields);
        dialog.ShowDialog(owner);
    }
}
