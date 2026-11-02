using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sunpole.uUnityBridge.Editor
{
    // Local Unity Editor API foundation. NOT a network server and NOT yet
    // connected to GitHub or the installed Windows Agent.
    public static class UnityBridgeEditorCommands
    {
        private const string Root = "Assets/uUnityDemo";
        private const string Scenes = Root + "/Scenes";
        private const string Art = Root + "/Generated";
        private const string DemoScene = Scenes + "/uUnityDemo.unity";
        private const string PlatformSprite = Art + "/platform.png";
        private const int MaxPlatforms = 100;

        [Serializable]
        private sealed class Reply
        {
            public string status;
            public string action;
            public string unityVersion;
            public string scene;
            public int platformCount;
            public string error;
        }

        // Future local adapter must pass only typed parameters validated
        // before this method. This function accepts NO commands, URLs or paths.
        public static string ExecuteLocal(string action, float x = 0f, float y = 0f,
            float width = 3f, float height = .4f)
        {
            var answer = new Reply
            {
                action = action,
                unityVersion = Application.unityVersion,
                status = "BLOCKED",
                error = ""
            };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("editor_in_play_mode");

                switch (UnityBridgePolicy.Parse(action))
                {
                    case UnityBridgeAction.UnityStatus:
                        break;
                    case UnityBridgeAction.CreateScene:
                        CreateScene();
                        break;
                    case UnityBridgeAction.CreatePlatform:
                        UnityBridgePolicy.ValidatePlatform(x, y, width, height);
                        CreatePlatform(x, y, width, height);
                        break;
                    case UnityBridgeAction.SaveScene:
                        SaveScene();
                        break;
                    default:
                        throw new InvalidOperationException("unknown_action");
                }
                answer.status = "PASS";
            }
            catch (ArgumentException)
            {
                answer.error = "invalid_parameters_or_action";
            }
            catch (InvalidOperationException e)
            {
                // Internal messages are fixed error codes, not stack traces
                // or local filesystem paths.
                answer.error = e.Message;
            }
            catch (Exception e)
            {
                Debug.LogError("uUnityBridge local Editor action failed: " + e.GetType().Name);
                answer.error = "editor_operation_failed";
            }

            Scene current = SceneManager.GetActiveScene();
            answer.scene = current.IsValid() ? current.path : "";
            answer.platformCount = IsDemoScene(current) ? PlatformCount(current) : 0;
            return JsonUtility.ToJson(answer);
        }

        [MenuItem("Tools/uUnityBridge/1. Проверить Unity")]
        public static void MenuStatus()
        {
            Debug.Log("uUnityBridge: " + ExecuteLocal("unity_status"));
        }

        [MenuItem("Tools/uUnityBridge/2. Создать тестовую сцену")]
        public static void MenuCreateDemo()
        {
            string reply = ExecuteLocal("create_scene");
            Debug.Log("uUnityBridge: " + reply);
            EditorUtility.DisplayDialog("uUnityBridge", reply, "OK");
        }

        [MenuItem("Tools/uUnityBridge/3. Добавить платформу")]
        public static void MenuAddPlatform()
        {
            Debug.Log("uUnityBridge: " +
                ExecuteLocal("create_platform", 0f, 4f, 3f, .4f));
        }

        [MenuItem("Tools/uUnityBridge/4. Сохранить сцену")]
        public static void MenuSave()
        {
            Debug.Log("uUnityBridge: " + ExecuteLocal("save_scene"));
        }

        private static void CreateScene()
        {
            Scene active = SceneManager.GetActiveScene();
            // Never silently discard unsaved work from an unrelated scene.
            if (active.IsValid() && active.isDirty)
                throw new InvalidOperationException("unsaved_scene_open");

            // Never overwrite an existing scene with the same name.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScene) != null)
                throw new InvalidOperationException("demo_scene_already_exists");

            EnsureFolder(Root, "Assets", "uUnityDemo");
            EnsureFolder(Scenes, Root, "Scenes");
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .14f, .25f, 1f);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            if (!EditorSceneManager.SaveScene(scene, DemoScene))
                throw new InvalidOperationException("save_scene_failed");
            CreatePlatform(-3.8f, -3f, 4f, .5f);
            CreatePlatform(.2f, -.9f, 3f, .5f);
            CreatePlatform(3.9f, 1.3f, 2.7f, .5f);
            SaveScene();
            Selection.activeGameObject = cameraObject;
        }

        private static void CreatePlatform(float x, float y, float width, float height)
        {
            Scene scene = RequireDemoScene();
            int count = PlatformCount(scene);
            if (count >= MaxPlatforms)
                throw new InvalidOperationException("platform_limit_reached");

            Sprite sprite = EnsureSprite();
            string name = "uUnityPlatform_" + (count + 1).ToString("D3");
            var platform = new GameObject(name);
            var renderer = platform.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(.36f, .83f, .88f, 1f);
            platform.transform.position = new Vector3(x, y, 0f);
            platform.transform.localScale = new Vector3(width, height, 1f);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = platform;
        }

        private static void SaveScene()
        {
            Scene scene = RequireDemoScene();
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("save_scene_failed");
        }

        private static Scene RequireDemoScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!IsDemoScene(scene))
                throw new InvalidOperationException("demo_scene_not_active");
            return scene;
        }

        private static bool IsDemoScene(Scene scene)
        {
            return scene.IsValid() && scene.isLoaded &&
                string.Equals(scene.path, DemoScene, StringComparison.Ordinal);
        }

        private static int PlatformCount(Scene scene)
        {
            int count = 0;
            foreach (GameObject obj in scene.GetRootGameObjects())
                if (obj.name.StartsWith("uUnityPlatform_", StringComparison.Ordinal))
                    count++;
            return count;
        }

        private static void EnsureFolder(string assetPath, string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(assetPath))
                AssetDatabase.CreateFolder(parent, name);
            if (!AssetDatabase.IsValidFolder(assetPath))
                throw new InvalidOperationException("cannot_create_demo_folder");
        }

        private static Sprite EnsureSprite()
        {
            EnsureFolder(Root, "Assets", "uUnityDemo");
            EnsureFolder(Art, Root, "Generated");
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(PlatformSprite) == null)
            {
                // Fixed project-relative output; no caller-controlled path.
                string assetFile = Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    PlatformSprite.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(assetFile))
                    throw new InvalidOperationException("demo_artwork_conflict");

                var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                try
                {
                    var colors = new Color[64];
                    for (int i = 0; i < colors.Length; i++)
                        colors[i] = Color.white;
                    texture.SetPixels(colors);
                    texture.Apply();
                    File.WriteAllBytes(assetFile, texture.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }

                AssetDatabase.ImportAsset(PlatformSprite,
                    ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(PlatformSprite) as TextureImporter;
                if (importer == null)
                    throw new InvalidOperationException("sprite_import_failed");
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 8f;
                importer.SaveAndReimport();
            }
            Sprite loaded = AssetDatabase.LoadAssetAtPath<Sprite>(PlatformSprite);
            if (loaded == null)
                throw new InvalidOperationException("demo_sprite_unavailable");
            return loaded;
        }
    }
}
