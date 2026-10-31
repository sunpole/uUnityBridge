using System.Security.Cryptography;
using System.Text;

namespace uUnityBridge;

/// <summary>Pure patch state rules, shared by desktop app and dependency-free tests.</summary>
internal static class PatchWorkflow
{
    internal const int MaxAttempts = 3;

    // The fingerprint binds the local receipt to the immutable meaning of a patch.
    // A public Issue edited after receipt cannot silently redefine a completed operation.
    internal static string Fingerprint(Patch patch)
    {
        string payload = string.Join("\n", "uub.patch.v2", patch.Number, patch.Id, patch.Action,
            patch.Commit?.ToLowerInvariant() ?? "", patch.Description);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    internal static bool Matches(Patch patch, PatchReceipt receipt)
    {
        if (patch.Number != receipt.Number || patch.Id != receipt.Id) return false;
        // Legacy v0.2 receipts did not have fingerprints. Only no-op checks can be
        // accepted without binding the receipt to an exact code commit.
        if (string.IsNullOrEmpty(receipt.Fingerprint))
            return patch.Action == "report-only" && receipt.State == "PASS";
        return string.Equals(receipt.Fingerprint, Fingerprint(patch), StringComparison.Ordinal);
    }

    internal static bool ShouldAttempt(PatchReceipt? receipt) =>
        receipt == null || (receipt.State == "FAIL" && receipt.Attempts < MaxAttempts);

    internal static bool IsSendable(PatchReceipt receipt) =>
        receipt.State is "PASS" or "SOURCE_UPDATED" or "BLOCKED" or "NEEDS_REVIEW";

    internal static bool IsSuccess(PatchReceipt receipt) =>
        receipt.State is "PASS" or "SOURCE_UPDATED";

    internal static string AnswerMarker(PatchReceipt receipt)
    {
        if (string.IsNullOrEmpty(receipt.Fingerprint))
            throw new InvalidOperationException("Для нового ответа нужен проверенный отпечаток патча.");
        return $"<!-- uub-ans-v2:{receipt.Id}:{receipt.Fingerprint}:{receipt.Attempts} -->";
    }
}
