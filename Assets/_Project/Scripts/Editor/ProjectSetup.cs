using kcp2k;
using LandFlipper.Network;
using LandFlipper.Player;
using LandFlipper.Player.Animation;
using LandFlipper.UI;
using LandFlipper.World;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LandFlipper.EditorTools
{
    /// <summary>
    /// Çekirdek sahneleri, karakter/oyuncu prefab'larını ve UI ayarlarını tekrar üretilebilir
    /// biçimde kurar. Tekrar çalıştırmak güvenlidir: üretilen varlıkların üzerine yazar.
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/_Project";
        const string MainMenuScenePath = Root + "/Scenes/MainMenu.unity";
        const string GameScenePath = Root + "/Scenes/Game.unity";
        const string PlayerPrefabPath = Root + "/Prefabs/Player/Player.prefab";
        const string CharacterVisualPrefabPath = Root + "/Prefabs/Player/CharacterVisual.prefab";
        const string CharacterModelPath = Root + "/Art/CharacterTest.fbx";
        const string CharacterTexturesFolder = Root + "/Art/CharacterTest_Textures";
        const string CharacterMaterialsFolder = Root + "/Art/Materials/Character";
        const string PanelSettingsPath = Root + "/UI/LandFlipperPanelSettings.asset";
        const string ThemePath = Root + "/UI/Themes/LandFlipperTheme.tss";
        const string MainMenuUxmlPath = Root + "/UI/MainMenu/MainMenu.uxml";
        const string GameMenuUxmlPath = Root + "/UI/GameMenu/GameMenu.uxml";
        const string HudUxmlPath = Root + "/UI/HUD/Hud.uxml";
        const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        const float GamePlotHalfSize = 18f;

        [MenuItem("Land Flipper/Setup/Build Core Scenes")]
        public static void BuildCoreScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            PlaceholderWorldBuilder.EnsureFolder(Root + "/Scenes");
            PlaceholderWorldBuilder.EnsureFolder(Root + "/Prefabs/Player");
            PlaceholderWorldBuilder.EnsureFolder(CharacterMaterialsFolder);

            var panelSettings = CreatePanelSettings();
            var palette = PlaceholderWorldBuilder.LoadPalette();
            var characterVisual = CreateCharacterVisualPrefab();
            var playerPrefab = CreatePlayerPrefab(characterVisual);
            CreateGameScene(panelSettings, palette);
            CreateMainMenuScene(panelSettings, playerPrefab, characterVisual, palette);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true)
            };
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath);

            AssetDatabase.SaveAssets();
            RelinkPanelSettings(GameScenePath, panelSettings);
            RelinkPanelSettings(MainMenuScenePath, panelSettings);
            Debug.Log("[LandFlipper] Çekirdek kurulum tamamlandı: MainMenu + Game sahneleri, karakter ve oyuncu prefab'ları.");
        }

        // ---------------------------------------------------------------- UI

        static PanelSettings CreatePanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }

            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.clearColor = false;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        static UIDocument AddDocument(GameObject go, PanelSettings settings, string uxmlPath, int sortingOrder)
        {
            var doc = go.AddComponent<UIDocument>();
            // Editor'da panelSettings özelliği her zaman serileştirilmiyor; alanı doğrudan yaz.
            var so = new SerializedObject(doc);
            so.FindProperty("m_PanelSettings").objectReferenceValue = settings;
            so.FindProperty("sourceAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            so.FindProperty("m_SortingOrder").floatValue = sortingOrder;
            so.ApplyModifiedPropertiesWithoutUndo();
            return doc;
        }

        // ---------------------------------------------------------------- Character

        static GameObject CreateCharacterVisualPrefab()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(CharacterModelPath);
            if (importer.importAnimation)
            {
                importer.importAnimation = false; // klip yok; animasyon prosedürel IK ile
                importer.SaveAndReimport();
            }

            var bodyMaterial = CreateCharacterMaterial("M_Character_Body",
                CharacterTexturesFolder + "/Ch01_1001_Diffuse.png",
                CharacterTexturesFolder + "/Ch01_1001_Normal.png");
            var lashMaterial = CreateCharacterMaterial("M_Character_Lashes", null, null);
            lashMaterial.SetColor("_BaseColor", new Color(0.12f, 0.09f, 0.07f));

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "CharacterVisual";

            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.sharedMaterial = smr.name.Contains("Eyelash") ? lashMaterial : bodyMaterial;
                smr.updateWhenOffscreen = true; // IK pozu bind sınırlarının dışına çıkabiliyor
            }

            instance.AddComponent<ProceduralCharacterAnimator>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, CharacterVisualPrefabPath);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        static Material CreateCharacterMaterial(string name, string baseMapPath, string normalMapPath)
        {
            string path = $"{CharacterMaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.2f);

            if (!string.IsNullOrEmpty(baseMapPath))
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseMapPath));

            if (!string.IsNullOrEmpty(normalMapPath))
            {
                var normalImporter = (TextureImporter)AssetImporter.GetAtPath(normalMapPath);
                if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
                {
                    normalImporter.textureType = TextureImporterType.NormalMap;
                    normalImporter.SaveAndReimport();
                }
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalMapPath));
                material.EnableKeyword("_NORMALMAP");
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- Player

        static GameObject CreatePlayerPrefab(GameObject characterVisualPrefab)
        {
            var player = new GameObject("Player");

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(characterVisualPrefab);
            visual.name = "Visual";
            visual.transform.SetParent(player.transform, false);
            var animator = visual.GetComponent<ProceduralCharacterAnimator>();
            var bodyRenderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.62f, 0.08f);

            var cameraGo = new GameObject("PlayerCamera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(pivot.transform, false);
            var cam = cameraGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 75f;
            cameraGo.AddComponent<AudioListener>();

            player.AddComponent<NetworkIdentity>();

            var netTransform = player.AddComponent<NetworkTransformReliable>();
            netTransform.target = player.transform;
            netTransform.syncDirection = SyncDirection.ClientToServer;
            netTransform.syncScale = false;

            var controller = player.AddComponent<FirstPersonController>();
            controller.syncDirection = SyncDirection.ClientToServer; // bakış eğimi SyncVar'ı
            var so = new SerializedObject(controller);
            so.FindProperty("inputActions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            so.FindProperty("cameraPivot").objectReferenceValue = pivot.transform;
            so.FindProperty("playerCamera").objectReferenceValue = cam;
            so.FindProperty("characterAnimator").objectReferenceValue = animator;
            var renderers = so.FindProperty("bodyRenderers");
            renderers.arraySize = bodyRenderers.Length;
            for (int i = 0; i < bodyRenderers.Length; i++)
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = bodyRenderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);
            return prefab;
        }

        // ---------------------------------------------------------------- Scenes

        static void CreateMainMenuScene(PanelSettings panelSettings, GameObject playerPrefab,
            GameObject characterVisualPrefab, PlaceholderWorldBuilder.Palette palette)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent<Camera>(out var cam))
                {
                    root.name = "Menu Camera";
                    cam.fieldOfView = 45f;
                    root.AddComponent<MenuCameraOrbit>();
                }
                else if (root.TryGetComponent<Light>(out var light))
                {
                    PlaceholderWorldBuilder.ApplyCozyLighting(light);
                }
            }

            PlaceholderWorldBuilder.BuildPlot("MenuDiorama", new PlaceholderWorldBuilder.PlotSettings
            {
                Seed = 7,
                HalfSize = 9f,
                Density = 0.35f,
                ClearCenter = new Vector3(0f, 0f, 2f),
                ClearRadius = 4f,
                IncludeShack = false,
                IncludeDelivery = false,
                OuterTreeCount = 30
            }, palette);

            var character = (GameObject)PrefabUtility.InstantiatePrefab(characterVisualPrefab);
            character.name = "MenuCharacter";
            character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 15f, 0f));

            var networkGo = new GameObject("NetworkManager");
            var kcp = networkGo.AddComponent<KcpTransport>();
            kcp.port = Core.SessionContext.DefaultPort;
            var manager = networkGo.AddComponent<LFNetworkManager>();
            manager.transport = kcp;
            manager.playerPrefab = playerPrefab;
            manager.autoCreatePlayer = true;
            manager.offlineScene = MainMenuScenePath;
            manager.onlineScene = GameScenePath;
            manager.maxConnections = Core.SessionContext.MaxCoopPlayers;
            manager.playerSpawnMethod = PlayerSpawnMethod.RoundRobin;
            manager.dontDestroyOnLoad = true;
            manager.runInBackground = true;

            var uiGo = new GameObject("MainMenuUI");
            AddDocument(uiGo, panelSettings, MainMenuUxmlPath, 0);
            uiGo.AddComponent<MainMenuController>();

            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
        }

        static void CreateGameScene(PanelSettings panelSettings, PlaceholderWorldBuilder.Palette palette)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Kamera oyuncu prefab'ında; sahne kamerası gerekmiyor.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<Camera>() != null)
                    Object.DestroyImmediate(root);
                else if (root.TryGetComponent<Light>(out var light))
                    PlaceholderWorldBuilder.ApplyCozyLighting(light);
            }

            float h = GamePlotHalfSize;
            Vector3 entrance = new Vector3(0f, 0f, -h + 3f);

            // Phase 2'de seed tabanlı arazi üretimiyle değiştirilecek dummy arazi.
            PlaceholderWorldBuilder.BuildPlot("Plot (Placeholder)", new PlaceholderWorldBuilder.PlotSettings
            {
                Seed = 1234,
                HalfSize = h,
                Density = 1f,
                ClearCenter = entrance + Vector3.forward,
                ClearRadius = 5f,
                IncludeShack = true,
                IncludeDelivery = true,
                OuterTreeCount = 50
            }, palette);

            var spawnRoot = new GameObject("SpawnPoints");
            Vector3[] offsets =
            {
                new Vector3(-1.5f, 0.1f, 0f), new Vector3(1.5f, 0.1f, 0f),
                new Vector3(-3f, 0.1f, 0f), new Vector3(3f, 0.1f, 0f)
            };
            for (int i = 0; i < offsets.Length; i++)
            {
                var spawn = new GameObject($"Spawn_{i + 1}");
                spawn.transform.SetParent(spawnRoot.transform, false);
                spawn.transform.localPosition = entrance + offsets[i];
                spawn.AddComponent<NetworkStartPosition>();
            }

            var uiRoot = new GameObject("UI");

            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(uiRoot.transform, false);
            AddDocument(hudGo, panelSettings, HudUxmlPath, 0);

            var menuGo = new GameObject("GameMenu");
            menuGo.transform.SetParent(uiRoot.transform, false);
            AddDocument(menuGo, panelSettings, GameMenuUxmlPath, 10);
            menuGo.AddComponent<GameMenuController>();

            EditorSceneManager.SaveScene(scene, GameScenePath);
        }

        /// <summary>
        /// Sahneyi yeniden açıp PanelSettings'i boş kalan UIDocument'lara bağlar.
        /// (Yeni oluşturulan sahnede atama bazen diske yazılmıyor.)
        /// </summary>
        static void RelinkPanelSettings(string scenePath, PanelSettings settings)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            int fixedCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var doc in root.GetComponentsInChildren<UIDocument>(true))
            {
                var so = new SerializedObject(doc);
                var prop = so.FindProperty("m_PanelSettings");
                if (prop.objectReferenceValue != null) continue;
                prop.objectReferenceValue = settings;
                so.ApplyModifiedProperties();
                fixedCount++;
            }

            if (fixedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
    }
}
