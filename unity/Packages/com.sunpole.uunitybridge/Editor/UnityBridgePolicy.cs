using System;

namespace Sunpole.uUnityBridge.Editor
{
    // Pure, testable allowlist. Never interpret a remote string as C#,
    // a process command, filesystem path, or Unity console instruction.
    public enum UnityBridgeAction
    {
        UnityStatus,
        CreateScene,
        CreatePlatform,
        SaveScene
    }

    public static class UnityBridgePolicy
    {
        public static UnityBridgeAction Parse(string action)
        {
            switch (action)
            {
                case "unity_status": return UnityBridgeAction.UnityStatus;
                case "create_scene": return UnityBridgeAction.CreateScene;
                case "create_platform": return UnityBridgeAction.CreatePlatform;
                case "save_scene": return UnityBridgeAction.SaveScene;
                default: throw new ArgumentException("unknown_action", nameof(action));
            }
        }

        public static void ValidatePlatform(float x, float y, float width, float height)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(width) ||
                float.IsNaN(height) || float.IsInfinity(x) || float.IsInfinity(y) ||
                float.IsInfinity(width) || float.IsInfinity(height) ||
                x < -20f || x > 20f || y < -20f || y > 20f ||
                width < .2f || width > 12f || height < .2f || height > 4f)
                throw new ArgumentOutOfRangeException("platform", "invalid_platform_dimensions");
        }
    }
}
