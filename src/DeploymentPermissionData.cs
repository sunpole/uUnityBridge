using System.Text.Json;
using System.Text.Json.Nodes;

namespace uUnityBridge;

// Pure JSON policy operation: revoking permission must not touch the old
// approved path, repository, workflow or any unrelated profile.
internal static class DeploymentPermissionData
{
    internal const string Schema = "uub.deploy.settings.v1";

    internal static string Revoke(string content, string profileId)
    {
        if (content.Length > 64_000 || profileId.Length is < 2 or > 32)
            throw new InvalidDataException("Invalid approval data.");
        var root = JsonNode.Parse(content) as JsonObject ??
            throw new InvalidDataException("Invalid profile settings.");
        if ((string?)root["schema"] != Schema ||
            root["profiles"] is not JsonArray profiles || profiles.Count > 20)
            throw new InvalidDataException("Invalid profile schema.");

        JsonObject? selected = null;
        foreach (JsonNode? item in profiles)
        {
            if (item is not JsonObject profile || profile["id"] is not JsonValue)
                throw new InvalidDataException("Invalid profile.");
            if ((string?)profile["id"] != profileId) continue;
            if (selected is not null)
                throw new InvalidDataException("Duplicate profile.");
            selected = profile;
        }
        if (selected is null)
            throw new InvalidDataException("Unknown profile: no permission to revoke.");
        if (selected["enabled"] is not JsonValue value ||
            !value.TryGetValue<bool>(out _))
            throw new InvalidDataException("Invalid permission flag.");

        selected["enabled"] = false;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
