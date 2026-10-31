using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace uUnityBridge;

// New optional fields keep v0.2 answers.json readable during migration.
internal sealed record PatchReceipt(int Number, string Id, string State, string Summary,
    string IssueUrl, bool Posted, string? AnswerUrl,
    string? Fingerprint = null, int Attempts = 1, DateTimeOffset? UpdatedAt = null);

internal static class PatchHistory
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uUnityBridge");
    private static readonly string FilePath = Path.Combine(Root, "answers.json");

    internal static Dictionary<int, PatchReceipt> Parse(string json)
    {
        var items = JsonSerializer.Deserialize<List<PatchReceipt>>(json)
            ?? throw new InvalidDataException("История заданий пуста или повреждена.");
        if (items.Any(x => x.Number <= 0 || !Regex.IsMatch(x.Id, "^P[0-9]{4,6}$")
             || x.Attempts is < 1 or > PatchWorkflow.MaxAttempts
             || x.State is not ("PASS" or "FAIL" or "APPLYING" or "SOURCE_UPDATED" or "BLOCKED" or "NEEDS_REVIEW"))
             || items.Select(x => x.Number).Distinct().Count() != items.Count
             || items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new InvalidDataException("История содержит некорректные или повторяющиеся задания.");
        return items.ToDictionary(x => x.Number);
    }

    internal static Dictionary<int, PatchReceipt> Load()
    {
        if (!File.Exists(FilePath)) return new Dictionary<int, PatchReceipt>();
        // Do not silently clear a corrupted ledger: replaying a patch can change files twice.
        return Parse(File.ReadAllText(FilePath, Encoding.UTF8));
    }

    internal static void Save(Dictionary<int, PatchReceipt> history)
    {
        Directory.CreateDirectory(Root);
        string temp = Path.Combine(Root, "answers-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var data = history.Values.OrderBy(x => x.Number).ToArray();
            string serialized = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            Parse(serialized); // Check before replacing any user data.
            File.WriteAllText(temp, serialized, new UTF8Encoding(false));
            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, FilePath + ".bak"); // Atomic replacement; preserve previous state.
            else
                File.Move(temp, FilePath);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
