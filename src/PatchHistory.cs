using System.Text;
using System.Text.Json;

namespace uUnityBridge;

internal sealed record PatchReceipt(int Number, string Id, string State, string Summary,
    string IssueUrl, bool Posted, string? AnswerUrl);

internal static class PatchHistory
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uUnityBridge");
    private static readonly string FilePath = Path.Combine(Root, "answers.json");

    internal static Dictionary<int, PatchReceipt> Load()
    {
        if (!File.Exists(FilePath)) return new Dictionary<int, PatchReceipt>();
        var items = JsonSerializer.Deserialize<List<PatchReceipt>>(File.ReadAllText(FilePath, Encoding.UTF8))
                    ?? throw new InvalidDataException("Пустая локальная история патчей.");
        // An invalid ledger must stop processing; otherwise a patch could be applied twice.
        if (items.Any(x => x.Number <= 0 || !System.Text.RegularExpressions.Regex.IsMatch(x.Id, "^P[0-9]{4,6}$")) ||
            items.Select(x => x.Number).Distinct().Count() != items.Count)
            throw new InvalidDataException("Локальная история патчей повреждена. Применение остановлено.");
        return items.ToDictionary(x => x.Number);
    }

    internal static void Save(Dictionary<int, PatchReceipt> history)
    {
        Directory.CreateDirectory(Root);
        string temp = Path.Combine(Root, "answers-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var data = history.Values.OrderBy(x => x.Number).ToArray();
            File.WriteAllText(temp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
