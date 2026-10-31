using System.Text;
using System.Text.Json;

namespace uUnityBridge;

/// <summary>Durable acknowledgement of which posted ANS have already been COPIED to clipboard.</summary>
internal static class ReportInbox
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uUnityBridge");
    private static readonly string FilePath = Path.Combine(Root, "report-cursor.json");

    // Do not key on UpdatedAt or Closed: closing an already-reported issue is not a new answer.
    internal static string Key(PatchReceipt r) =>
        $"{r.Number}:{r.Id}:{r.State}:{r.Attempts}:{r.Fingerprint ?? "legacy"}";

    internal static IReadOnlyList<PatchReceipt> Pending(
        IEnumerable<PatchReceipt> receipts, IReadOnlySet<string> acknowledged) =>
        receipts.Where(r => r.Posted && !acknowledged.Contains(Key(r)))
            .OrderBy(r => r.Number).ToArray();

    internal static HashSet<string> Parse(string json)
    {
        var ids = JsonSerializer.Deserialize<List<string>>(json)
            ?? throw new InvalidDataException("Отметки отчётов повреждены.");
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Any(x => x.Length > 160) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidDataException("Отметки отчётов некорректны.");
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    internal static HashSet<string> LoadOrInitialize(Dictionary<int, PatchReceipt> history)
    {
        if (File.Exists(FilePath))
            return Parse(File.ReadAllText(FilePath, Encoding.UTF8));

        // Migration from v0.2.1: earlier ANS were already copied manually.
        // Only newly published results after this first launch should light up ОТЧЁТ.
        var existing = history.Values.Where(r => r.Posted)
            .Select(Key).ToHashSet(StringComparer.Ordinal);
        Save(existing);
        return existing;
    }

    internal static void Save(HashSet<string> acknowledged)
    {
        Directory.CreateDirectory(Root);
        string temp = Path.Combine(Root, "report-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var json = JsonSerializer.Serialize(acknowledged.Order(StringComparer.Ordinal).ToArray());
            Parse(json);
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, FilePath + ".bak");
            else
                File.Move(temp, FilePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
