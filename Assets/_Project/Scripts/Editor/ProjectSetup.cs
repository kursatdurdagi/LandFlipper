using System.Collections.Generic;
using kcp2k;
using LandFlipper.Gameplay;
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
    /// Çekirdek sahneleri, prefab'ları, kaynak verilerini ve UI ayarlarını tekrar üretilebilir
    /// biçimde kurar. Tekrar çalıştırmak güvenlidir: üretilen varlıkların üzerine yazar.
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/_Project";
        const string MainMenuScenePath = Root + "/Scenes/MainMenu.unity";
        const string GameScenePath = Root + "/Scenes/Game.unity";
        const string PlayerPrefabPath = Root + "/Prefabs/Player/Player.prefab";
        const string CharacterVisualPrefabPath = Root + "/Prefabs/Player/CharacterVisual.prefab";
        const string PickupFolder = Root + "/Prefabs/Pickups";
        const string DataFolder = Root + "/Data/ResourceDefinitions";
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

        class ResourceSet
        {
            public ResourceDefinition Log, RockChunk, GrassBale, Plank;
            public List<GameObject> Prefabs = new List<GameObject>();

            public string PriceBoardText =>
                $"FİYATLAR\n{GrassBale.displayName}  {Currency.Format(GrassBale.sellPrice)}\n" +
                $"{RockChunk.displayName}  {Currency.Format(RockChunk.sellPrice)}\n" +
                $"{Plank.displayName}  {Currency.Format(Plank.sellPrice)}\n" +
                $"{Log.displayName}  {Currency.Format(Log.sellPrice)}";
        }

        [MenuItem("Land Flipper/Setup/Build Core Scenes")]
        public static void BuildCoreScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer < 0)
            {
                Debug.LogError("[LandFlipper] 'Player' layer'ı yok. Project Settings > Tags and Layers'dan ekle.");
                return;
            }

            LowPolyWorldBuilder.EnsureFolder(Root + "/Scenes");
            LowPolyWorldBuilder.EnsureFolder(Root + "/Prefabs/Player");
            LowPolyWorldBuilder.EnsureFolder(PickupFolder);
            LowPolyWorldBuilder.EnsureFolder(DataFolder);
            LowPolyWorldBuilder.EnsureFolder(CharacterMaterialsFolder);

            var panelSettings = CreatePanelSettings();
            var kit = LowPolyWorldBuilder.LoadKit();
            var resources = CreateResources(kit);
            var characterVisual = CreateCharacterVisualPrefab();
            var playerPrefab = CreatePlayerPrefab(characterVisual, playerLayer);
            CreateGameScene(panelSettings, kit, resources);
            CreateMainMenuScene(panelSettings, playerPrefab, characterVisual, kit, resources);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true)
            };
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath);

            AssetDatabase.SaveAssets();
            RelinkPanelSettings(GameScenePath, panelSettings);
            RelinkPanelSettings(MainMenuScenePath, panelSettings);
            Debug.Log("[LandFlipper] Çekirdek kurulum tamamlandı: low poly arazi, kaynaklar, satış tezgâhı, oyuncu ve menüler.");
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

        // ---------------------------------------------------------------- Resources

        static ResourceSet CreateResources(LowPolyWorldBuilder.Kit kit)
        {
            var set = new ResourceSet
            {
                GrassBale = Definition("RD_GrassBale", "grass_bale", "Çim Balyası", 4, 1, 0.31f, Vector3.zero),
                RockChunk = Definition("RD_RockChunk", "rock_chunk", "Taş", 6, 1, 0.3f, Vector3.zero),
                Log = Definition("RD_Log", "log", "Kütük", 15, 2, 0.36f, new Vector3(0f, 90f, 0f)),
                Plank = Definition("RD_Plank", "plank", "Tahta", 8, 1, 0.1f, new Vector3(0f, 90f, 0f))
            };

            set.Prefabs.Add(CreatePickupPrefab("Pickup_Plank", set.Plank, 4f, visual =>
            {
                AddMesh(visual, kit.Box, kit.Wood);
                visual.transform.localScale = new Vector3(0.24f, 0.08f, 1.15f);
                var c = visual.transform.parent.gameObject.AddComponent<BoxCollider>();
                c.size = new Vector3(0.24f, 0.08f, 1.15f);
            }));

            set.Prefabs.Add(CreatePickupPrefab("Pickup_GrassBale", set.GrassBale, 3f, visual =>
            {
                AddMesh(visual, kit.Bale, kit.Straw, kit.Twine);
                var c = visual.transform.parent.gameObject.AddComponent<BoxCollider>();
                c.size = new Vector3(0.52f, 0.3f, 0.34f);
            }));

            set.Prefabs.Add(CreatePickupPrefab("Pickup_RockChunk", set.RockChunk, 5f, visual =>
            {
                AddMesh(visual, kit.Chunks[0], kit.Rock);
                visual.transform.localScale = new Vector3(0.2f, 0.17f, 0.18f);
                var c = visual.transform.parent.gameObject.AddComponent<BoxCollider>();
                c.size = new Vector3(0.34f, 0.28f, 0.32f);
            }));

            set.Prefabs.Add(CreatePickupPrefab("Pickup_Log", set.Log, 14f, visual =>
            {
                AddMesh(visual, kit.Log, kit.Bark, kit.WoodCut);
                var c = visual.transform.parent.gameObject.AddComponent<CapsuleCollider>();
                c.direction = 2;
                c.radius = 0.17f;
                c.height = 1.1f;
            }));

            return set;
        }

        static ResourceDefinition Definition(string asset, string id, string displayName, int price, int slots, float stackHeight, Vector3 carryEuler)
        {
            string path = $"{DataFolder}/{asset}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<ResourceDefinition>();
                AssetDatabase.CreateAsset(def, path);
                def.sellPrice = price; // fiyatı sadece ilk oluşturmada yaz; sonra Inspector'dan dengelenir
                def.slotCost = slots;
            }
            def.id = id;
            def.displayName = displayName;
            def.stackHeight = stackHeight;
            def.carryEuler = carryEuler;
            EditorUtility.SetDirty(def);
            return def;
        }

        static GameObject CreatePickupPrefab(string name, ResourceDefinition def, float mass, System.Action<GameObject> buildVisual)
        {
            var root = new GameObject(name);
            var body = root.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = 0.2f;
            body.angularDamping = 0.6f;
            root.AddComponent<NetworkIdentity>();
            var pickup = root.AddComponent<Pickup>();
            pickup.EditorSetDefinition(def);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            buildVisual(visual);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PickupFolder}/{name}.prefab");
            Object.DestroyImmediate(root);

            def.prefab = prefab.GetComponent<Pickup>();
            EditorUtility.SetDirty(def);
            return prefab;
        }

        static void AddMesh(GameObject go, Mesh mesh, params Material[] materials)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
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

        static GameObject CreatePlayerPrefab(GameObject characterVisualPrefab, int playerLayer)
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

            // Gövde önü: diğer oyuncular yükü göğüs hizasında, kollar arasında görür.
            var carryAnchor = new GameObject("CarryAnchor");
            carryAnchor.transform.SetParent(player.transform, false);
            carryAnchor.transform.localPosition = new Vector3(0f, 0.98f, 0.42f);

            // Birinci şahıs: ekranın alt ortasında, küçültülmüş ve görüşü kapatmayacak kadar aşağıda.
            var viewAnchor = new GameObject("CarryViewAnchor");
            viewAnchor.transform.SetParent(pivot.transform, false);
            viewAnchor.transform.localPosition = new Vector3(0f, -0.75f, 1.1f);

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

            player.AddComponent<PlayerCarry>().EditorSetup(carryAnchor.transform, viewAnchor.transform, animator);
            var interactor = player.AddComponent<PlayerInteractor>();
            interactor.EditorSetMask(~((1 << playerLayer) | (1 << 2)));

            foreach (var t in player.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = playerLayer;

            var prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);
            return prefab;
        }

        // ---------------------------------------------------------------- Scenes

        static void CreateMainMenuScene(PanelSettings panelSettings, GameObject playerPrefab,
            GameObject characterVisualPrefab, LowPolyWorldBuilder.Kit kit, ResourceSet resources)
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
                    LowPolyWorldBuilder.ApplyCozyLighting(light);
                }
            }

            LowPolyWorldBuilder.BuildPlot("MenuDiorama", new LowPolyWorldBuilder.PlotSettings
            {
                Seed = 7,
                HalfSize = 9f,
                Interactive = false,
                TreeCount = 7,
                RockCount = 4,
                GrassCount = 90,
                BushCount = 6,
                FlowerCount = 14,
                OuterTreeCount = 45,
                ClearCenter = new Vector3(0f, 0f, 2f),
                ClearRadius = 4f
            }, kit);

            var character = (GameObject)PrefabUtility.InstantiatePrefab(characterVisualPrefab);
            character.name = "MenuCharacter";
            character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 15f, 0f));

            var networkGo = new GameObject("NetworkManager");
            var kcp = networkGo.AddComponent<KcpTransport>();
            kcp.port = Core.SessionContext.DefaultPort;
            var manager = networkGo.AddComponent<LFNetworkManager>();
            manager.transport = kcp;
            manager.playerPrefab = playerPrefab;
            manager.spawnPrefabs = new List<GameObject>(resources.Prefabs);
            manager.autoCreatePlayer = true;
            manager.offlineScene = MainMenuScenePath;
            manager.onlineScene = GameScenePath;
            manager.maxConnections = Core.SessionContext.MaxCoopPlayers;
            manager.playerSpawnMethod = PlayerSpawnMethod.RoundRobin;
            manager.dontDestroyOnLoad = true;
            manager.runInBackground = true;

            var uiRoot = new GameObject("UI");
            var uiGo = new GameObject("MainMenuUI");
            uiGo.transform.SetParent(uiRoot.transform, false);
            AddDocument(uiGo, panelSettings, MainMenuUxmlPath, 0);
            uiGo.AddComponent<MainMenuController>();

            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
        }

        static void CreateGameScene(PanelSettings panelSettings, LowPolyWorldBuilder.Kit kit, ResourceSet resources)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<Camera>() != null)
                    Object.DestroyImmediate(root); // kamera oyuncu prefab'ında
                else if (root.TryGetComponent<Light>(out var light))
                    LowPolyWorldBuilder.ApplyCozyLighting(light);
            }

            float h = GamePlotHalfSize;
            Vector3 entrance = new Vector3(0f, 0f, -h + 3f);

            // Phase 2'de çalışma zamanında seed'den üretilecek; şimdilik editörde sabit seed.
            LowPolyWorldBuilder.BuildPlot("Plot", new LowPolyWorldBuilder.PlotSettings
            {
                Seed = 1234,
                HalfSize = h,
                Interactive = true,
                TreeCount = 26,
                RockCount = 18,
                GrassCount = 340,
                BushCount = 14,
                FlowerCount = 30,
                OuterTreeCount = 110,
                ClearCenter = entrance + Vector3.forward,
                ClearRadius = 4.5f,
                IncludeShack = true,
                IncludeSellStation = true,
                PriceBoardText = resources.PriceBoardText
            }, kit);

            var plotNetwork = new GameObject("PlotNetwork");
            plotNetwork.AddComponent<NetworkIdentity>();
            plotNetwork.AddComponent<PlotWorld>().EditorSetup(h, 1, resources.Log, resources.RockChunk, resources.GrassBale, resources.Plank);
            plotNetwork.AddComponent<CareerState>();

            var spawnRoot = new GameObject("SpawnPoints");
            Vector3[] offsets =
            {
                new Vector3(-1.5f, 0.2f, 0f), new Vector3(1.5f, 0.2f, 0f),
                new Vector3(-3f, 0.2f, 0f), new Vector3(3f, 0.2f, 0f)
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
            hudGo.AddComponent<HudController>();

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
            int docCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var doc in root.GetComponentsInChildren<UIDocument>(true))
            {
                // Bellekteki değer dolu görünse bile diske yazılmamış olabiliyor: her zaman yeniden ata.
                var so = new SerializedObject(doc);
                so.FindProperty("m_PanelSettings").objectReferenceValue = settings;
                so.ApplyModifiedProperties();
                docCount++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Diskteki dosyayı doğrula
            string text = System.IO.File.ReadAllText(scenePath);
            string guid = AssetDatabase.AssetPathToGUID(PanelSettingsPath);
            int linked = 0, index = 0;
            while ((index = text.IndexOf("m_PanelSettings: {fileID: 11400000, guid: " + guid, index, System.StringComparison.Ordinal)) >= 0)
            {
                linked++;
                index++;
            }
            if (linked < docCount)
                Debug.LogError($"[LandFlipper] {scenePath}: {docCount} UIDocument'tan sadece {linked} tanesi PanelSettings'e bağlı kaydedildi.");
        }
    }
}
