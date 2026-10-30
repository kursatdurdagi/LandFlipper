using System.Collections.Generic;
using kcp2k;
using LandFlipper.Gameplay;
using LandFlipper.Network;
using LandFlipper.Player;
using LandFlipper.Player.Animation;
using LandFlipper.Player.Tools;
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
        const string SkillTreeUxmlPath = Root + "/UI/SkillTree/SkillTree.uxml";
        const string SkillsFolder = Root + "/Data/Skills";
        const string SettingsUxmlPath = Root + "/UI/Settings/Settings.uxml";
        const string WorkbenchUxmlPath = Root + "/UI/Workbench/Workbench.uxml";
        const string ToolsFolder = Root + "/Data/Tools";
        const string ToolPrefabFolder = Root + "/Prefabs/Tools";

        const float GamePlotHalfSize = 18f;

        static UnityEngine.Rendering.VolumeProfile volumeProfile;
        static Material skyMaterial;

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

            LightingSetup.ConfigurePipeline();
            volumeProfile = LightingSetup.CreateProfile();
            skyMaterial = LightingSetup.CreateSkybox();
            LightingSetup.CreateSunFlare();

            var panelSettings = CreatePanelSettings();
            var kit = LowPolyWorldBuilder.LoadKit();
            var resources = CreateResources(kit);
            var skills = CreateSkills();
            var tools = CreateTools(kit);
            var characterVisual = CreateCharacterVisualPrefab();
            var playerPrefab = CreatePlayerPrefab(characterVisual, playerLayer);
            CreateGameScene(panelSettings, kit, resources, skills, tools);
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

        static void AddSettingsDocument(GameObject uiRoot, PanelSettings panelSettings)
        {
            var settingsGo = new GameObject("Settings");
            settingsGo.transform.SetParent(uiRoot.transform, false);
            AddDocument(settingsGo, panelSettings, SettingsUxmlPath, 20);
            settingsGo.AddComponent<SettingsController>();
        }

        static UIDocument AddDocument(GameObject go, PanelSettings settings, string uxmlPath, int sortingOrder)
        {
            var doc = go.AddComponent<UIDocument>();
            // Kurulum sırasında yapılan asset işlemleri eldeki referansı geçersiz kılabiliyor
            // (sahneye boş yazılıyordu); bu yüzden PanelSettings'i her seferinde diskten yükle.
            var freshSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var so = new SerializedObject(doc);
            so.FindProperty("m_PanelSettings").objectReferenceValue = freshSettings;
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
                // stackHeight = nesnenin gerçek yüksekliği (elde boşluksuz üst üste dizilsin)
                GrassBale = Definition("RD_GrassBale", "grass_bale", "Çim Balyası", 4, 1, 0.29f, Vector3.zero),
                RockChunk = Definition("RD_RockChunk", "rock_chunk", "Taş", 6, 1, 0.24f, Vector3.zero),
                // Tek elde taşındığı için uzun nesneler ileri doğru durur.
                Log = Definition("RD_Log", "log", "Kütük", 15, 2, 0.33f, Vector3.zero),
                Plank = Definition("RD_Plank", "plank", "Tahta", 8, 1, 0.085f, Vector3.zero)
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

        // ---------------------------------------------------------------- Skills

        static SkillDefinition[] CreateSkills()
        {
            LowPolyWorldBuilder.EnsureFolder(SkillsFolder);

            var arms = Skill("SK_CarryArms", "carry_arms", "Güçlü Kollar", "Elinde daha fazla kaynak taşı.",
                "carry", "Taşıma", 0, "K", SkillStat.CarryCapacity, 1f, 4, null, 1);
            var basket = Skill("SK_CarryBasket", "carry_basket", "Sırt Sepeti", "Sırtına bağladığın sepetle yükünü büyüt.",
                "carry", "Taşıma", 1, "S", SkillStat.CarryCapacity, 1f, 2, arms, 2);

            var light = Skill("SK_MoveLight", "move_light", "Hafif Adım", "Arazide daha hızlı yürü ve koş.",
                "move", "Hareket", 0, "H", SkillStat.MoveSpeed, 0.08f, 3, null, 1);
            var sprint = Skill("SK_MoveSprint", "move_sprint", "Koşucu", "Koşarken çok daha hızlı ilerle.",
                "move", "Hareket", 1, "R", SkillStat.SprintSpeed, 0.12f, 3, light, 1);

            var hands = Skill("SK_GatherHands", "gather_hands", "Seri Eller", "Biçme, kesme ve kırma vuruşların hızlanır.",
                "gather", "Toplama", 0, "E", SkillStat.HitSpeed, 0.12f, 3, null, 1);

            var trader = Skill("SK_YieldTrader", "yield_trader", "Pazarlıkçı", "Tezgâhta kaynaklarını daha iyi fiyata sat.",
                "yield", "Verim", 0, "P", SkillStat.SellBonus, 0.06f, 3, null, 1);

            return new[] { arms, basket, light, sprint, hands, trader };
        }

        static SkillDefinition Skill(string asset, string id, string name, string description, string branchId, string branchName,
            int order, string icon, SkillStat stat, float value, int maxLevel, SkillDefinition prerequisite, int prerequisiteLevel)
        {
            string path = $"{SkillsFolder}/{asset}.asset";
            var skill = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);
            if (skill == null)
            {
                skill = ScriptableObject.CreateInstance<SkillDefinition>();
                AssetDatabase.CreateAsset(skill, path);
                // denge değerlerini sadece ilk oluşturmada yaz; sonra Inspector'dan ayarlanır
                skill.valuePerLevel = value;
                skill.maxLevel = maxLevel;
                skill.costPerLevel = 1;
            }
            skill.id = id;
            skill.displayName = name;
            skill.description = description;
            skill.branchId = branchId;
            skill.branchName = branchName;
            skill.branchOrder = order;
            skill.iconText = icon;
            skill.stat = stat;
            skill.prerequisite = prerequisite;
            skill.prerequisiteLevel = prerequisiteLevel;
            EditorUtility.SetDirty(skill);
            return skill;
        }

        // ---------------------------------------------------------------- Tools

        /// <summary>
        /// Alet kademeleri ve dummy modelleri. Model kökü = tutma noktası, +Y aletin başı, +Z vuran yüz.
        /// Gerçek model atanmışsa (Inspector'dan visualPrefab değiştirildiyse) üzerine yazılmaz.
        /// </summary>
        static ToolDefinition[] CreateTools(LowPolyWorldBuilder.Kit kit)
        {
            LowPolyWorldBuilder.EnsureFolder(ToolsFolder);
            LowPolyWorldBuilder.EnsureFolder(ToolPrefabFolder);

            var metal = LowPolyWorldBuilder.Mat("M_ToolMetal", 0.70f, 0.72f, 0.76f, 0.55f);
            var metalDark = LowPolyWorldBuilder.Mat("M_ToolMetalDark", 0.36f, 0.37f, 0.40f, 0.35f);
            var steel = LowPolyWorldBuilder.Mat("M_ToolSteel", 0.84f, 0.87f, 0.92f, 0.7f);
            var plasticRed = LowPolyWorldBuilder.Mat("M_ToolPlasticRed", 0.92f, 0.30f, 0.26f, 0.4f);
            var plasticBlue = LowPolyWorldBuilder.Mat("M_ToolPlasticBlue", 0.30f, 0.55f, 0.92f, 0.4f);
            var handleBlack = LowPolyWorldBuilder.Mat("M_ToolHandleBlack", 0.13f, 0.12f, 0.12f, 0.3f);
            var handleGreen = LowPolyWorldBuilder.Mat("M_ToolHandleGreen", 0.25f, 0.55f, 0.28f, 0.3f);
            var handleRed = LowPolyWorldBuilder.Mat("M_ToolHandleRed", 0.72f, 0.20f, 0.16f, 0.3f);
            var stone = LowPolyWorldBuilder.Mat("M_ToolStone", 0.55f, 0.53f, 0.50f);
            var box = kit.Box;

            GameObject P(Transform parent, string n, Vector3 pos, Vector3 size, Material m, Vector3 euler = default)
            {
                var go = new GameObject(n);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                go.transform.localScale = size;
                go.transform.localRotation = Quaternion.Euler(euler);
                AddMesh(go, box, m);
                return go;
            }

            // ---- makaslar: iki yarı (ağız + sap) vida ekseni Z etrafında açılır
            System.Action<GameObject> Shears(float bladeLength, float handleLength, float bladeWidth, Material blade, Material handleA, Material handleB, float ring)
            {
                return root =>
                {
                    // tutma noktası halkalarda: vida, sap boyu kadar yukarıda
                    float pivotY = handleLength + ring * 0.3f;
                    Transform Half(string n, float side, Material handle)
                    {
                        var half = new GameObject(n).transform;
                        half.SetParent(root.transform, false);
                        half.localPosition = new Vector3(0f, pivotY, 0f);
                        // ağız: vidadan yukarı, uca doğru incelir
                        P(half, "Blade", new Vector3(side * bladeWidth * 0.25f, bladeLength * 0.5f, side * 0.004f),
                            new Vector3(bladeWidth, bladeLength, 0.006f), blade, new Vector3(0f, 0f, -side * 2f));
                        P(half, "Tip", new Vector3(side * bladeWidth * 0.1f, bladeLength + 0.01f, side * 0.004f),
                            new Vector3(bladeWidth * 0.5f, 0.025f, 0.006f), blade);
                        // sap: vidadan aşağı, ucunda halka
                        P(half, "Arm", new Vector3(side * 0.012f, -handleLength * 0.5f, side * 0.004f),
                            new Vector3(0.014f, handleLength, 0.012f), handle, new Vector3(0f, 0f, side * 6f));
                        P(half, "Ring", new Vector3(side * (0.012f + ring * 0.35f), -handleLength - ring * 0.3f, side * 0.004f),
                            new Vector3(ring, ring * 0.7f, 0.016f), handle);
                        return half;
                    }
                    var a = Half("BladeA", 1f, handleA);
                    var b = Half("BladeB", -1f, handleB);
                    P(root.transform, "Screw", new Vector3(0f, pivotY, 0f), new Vector3(0.016f, 0.016f, 0.02f), metalDark);
                    root.AddComponent<ToolVisual>().EditorSetup(a, b, Vector3.forward, 16f);
                };
            }

            // ---- balta: sap +Y, ağız +Z yönünde
            System.Action<GameObject> Axe(float length, Material handle, Material head, Vector3 headSize, Material edge, bool lashing)
            {
                return root =>
                {
                    float bottom = -0.1f;
                    P(root.transform, "Handle", new Vector3(0f, bottom + length * 0.5f, 0f), new Vector3(0.034f, length, 0.038f), handle);
                    float headY = bottom + length - 0.09f;
                    P(root.transform, "Head", new Vector3(0f, headY, headSize.z * 0.35f), headSize, head);
                    P(root.transform, "Edge", new Vector3(0f, headY, headSize.z * 0.35f + headSize.z * 0.5f),
                        new Vector3(headSize.x * 0.6f, headSize.y * 1.35f, 0.025f), edge);
                    P(root.transform, "Poll", new Vector3(0f, headY, -0.03f), new Vector3(headSize.x * 1.1f, headSize.y * 0.8f, 0.05f), head);
                    if (lashing)
                    {
                        P(root.transform, "LashA", new Vector3(0f, headY + 0.045f, 0f), new Vector3(0.05f, 0.018f, 0.05f), kit.Twine, new Vector3(0f, 0f, 20f));
                        P(root.transform, "LashB", new Vector3(0f, headY - 0.045f, 0f), new Vector3(0.05f, 0.018f, 0.05f), kit.Twine, new Vector3(0f, 0f, -20f));
                    }
                };
            }

            // ---- çekiç: sap +Y, baş Z ekseninde iki yana
            System.Action<GameObject> Hammer(float length, Material handle, Material head, Vector3 headSize, bool lashing, bool rough)
            {
                return root =>
                {
                    float bottom = -0.1f;
                    P(root.transform, "Handle", new Vector3(0f, bottom + length * 0.5f, 0f), new Vector3(0.034f, length, 0.034f), handle);
                    float headY = bottom + length - headSize.y * 0.45f;
                    P(root.transform, "Head", new Vector3(0f, headY, 0f), headSize, head, rough ? new Vector3(8f, 12f, -6f) : Vector3.zero);
                    if (rough)
                        P(root.transform, "Lump", new Vector3(0.01f, headY + 0.02f, headSize.z * 0.3f), headSize * 0.6f, head, new Vector3(-15f, 30f, 10f));
                    else
                        P(root.transform, "Face", new Vector3(0f, headY, headSize.z * 0.5f), new Vector3(headSize.x * 1.15f, headSize.y * 1.15f, 0.02f), metalDark);
                    if (lashing)
                        P(root.transform, "Lash", new Vector3(0f, headY - headSize.y * 0.6f, 0f), new Vector3(0.05f, 0.03f, 0.05f), kit.Twine);
                };
            }

            return new[]
            {
                Tool("TL_Shears0", "shears_kid", ToolKind.Shears, 0, "Çocuk Makası", "Küçük, kör uçlu bir okul makası. Çimi zar zor keser.",
                    0, 1, 1f, Shears(0.085f, 0.045f, 0.02f, metal, plasticRed, plasticBlue, 0.05f),
                    ModelShears("child_scissor", "Shears_Child", 0.17f, 22f)),
                Tool("TL_Shears1", "shears_tailor", ToolKind.Shears, 1, "Terzi Makası", "Uzun ve keskin ağızlar; çimi çok daha hızlı biçer.",
                    120, 2, 1.8f, Shears(0.13f, 0.06f, 0.02f, steel, handleBlack, handleBlack, 0.045f),
                    ModelShears("adultscissor", "Shears_Adult", 0.25f, 18f)),
                Tool("TL_Shears2", "shears_garden", ToolKind.Shears, 2, "Bahçe Makası", "Uzun saplı budama makası. Çim ve çalılar için ideal.",
                    380, 4, 3f, Shears(0.17f, 0.2f, 0.035f, metalDark, handleGreen, handleGreen, 0.05f),
                    ModelShears("gardenscissor", "Shears_Garden", 0.5f, 16f)),

                Tool("TL_Axe0", "axe_stone", ToolKind.Axe, 0, "Taş Balta", "Sopaya bağlanmış yontma taş. Ağaçları yavaş keser.",
                    0, 1, 1f, Axe(0.5f, kit.Wood, stone, new Vector3(0.06f, 0.1f, 0.13f), stone, true)),
                Tool("TL_Axe1", "axe_iron", ToolKind.Axe, 1, "Demir Balta", "Ağır demir ağız: her vuruşta iki kat iş.",
                    200, 3, 2f, Axe(0.62f, kit.WoodDark, metalDark, new Vector3(0.035f, 0.12f, 0.15f), metal, false)),
                Tool("TL_Axe2", "axe_steel", ToolKind.Axe, 2, "Çelik Balta", "Keskin çelik ağız, ağaçları birkaç darbede devirir.",
                    520, 5, 3f, Axe(0.72f, handleRed, steel, new Vector3(0.035f, 0.15f, 0.18f), steel, false)),

                Tool("TL_Hammer0", "hammer_stone", ToolKind.Hammer, 0, "Taş Çekiç", "Sapa bağlanmış yuvarlak bir taş. Kayaları zor kırar.",
                    0, 1, 1f, Hammer(0.42f, kit.Wood, stone, new Vector3(0.09f, 0.08f, 0.14f), true, true)),
                Tool("TL_Hammer1", "hammer_iron", ToolKind.Hammer, 1, "Demir Çekiç", "Taşları ve kulübe tahtalarını daha kolay söker.",
                    160, 2, 2f, Hammer(0.48f, kit.WoodDark, metalDark, new Vector3(0.06f, 0.06f, 0.17f), false, false)),
                Tool("TL_Hammer2", "hammer_sledge", ToolKind.Hammer, 2, "Balyoz", "Koca bir balyoz: kayalar birkaç darbede paramparça.",
                    480, 4, 3f, Hammer(0.78f, kit.WoodDark, metalDark, new Vector3(0.11f, 0.11f, 0.28f), false, false)),
            };
        }

        /// <summary>Art/Models altındaki sanatçı makas modelinden alet prefab'ı (yoksa null → dummy kalır).</summary>
        /// <summary>SFx/Cuts altındaki tek vuruşluk sesleri (Wood_*, Rock_*, Snip_*) kütüphaneye bağlar.</summary>
        static SfxLibrary CreateSfxLibrary()
        {
            const string cuts = Root + "/SFx/Cuts";
            const string path = Root + "/Data/SfxLibrary.asset";
            var library = AssetDatabase.LoadAssetAtPath<SfxLibrary>(path);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SfxLibrary>();
                AssetDatabase.CreateAsset(library, path);
            }

            AudioClip[] Load(string prefix)
            {
                var list = new List<AudioClip>();
                if (!AssetDatabase.IsValidFolder(cuts)) return list.ToArray();
                foreach (var guid in AssetDatabase.FindAssets($"{prefix}_ t:AudioClip", new[] { cuts }))
                {
                    string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetImporter.GetAtPath(clipPath) is AudioImporter importer)
                    {
                        // kısa vuruşlar: belleğe açık yükle → Play() anında başlar, gecikme olmaz
                        var settings = importer.defaultSampleSettings;
                        if (settings.loadType != AudioClipLoadType.DecompressOnLoad || !importer.forceToMono)
                        {
                            settings.loadType = AudioClipLoadType.DecompressOnLoad;
                            settings.compressionFormat = AudioCompressionFormat.ADPCM;
                            importer.defaultSampleSettings = settings;
                            importer.forceToMono = true;
                            importer.SaveAndReimport();
                        }
                    }
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                    if (clip != null) list.Add(clip);
                }
                list.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
                return list.ToArray();
            }

            library.wood = Load("Wood");
            library.rock = Load("Rock");
            library.snip = Load("Snip");
            EditorUtility.SetDirty(library);
            return library;
        }

        static GameObject ModelShears(string model, string prefabName, float length, float openAngle)
        {
            string modelPath = $"Assets/_Project/Art/Models/{model}.fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null) return null;
            return ToolModelBuilder.BuildShears(modelPath, $"{ToolPrefabFolder}/{prefabName}.prefab", length, openAngle);
        }

        static ToolDefinition Tool(string asset, string id, ToolKind kind, int tier, string name, string description,
            int price, int requiredLevel, float power, System.Action<GameObject> buildVisual, GameObject modelPrefab = null)
        {
            string path = $"{ToolsFolder}/{asset}.asset";
            var tool = AssetDatabase.LoadAssetAtPath<ToolDefinition>(path);
            if (tool == null)
            {
                tool = ScriptableObject.CreateInstance<ToolDefinition>();
                AssetDatabase.CreateAsset(tool, path);
                // denge değerleri sadece ilk oluşturmada; sonra Inspector'dan ayarlanır
                tool.price = price;
                tool.requiredLevel = requiredLevel;
                tool.power = power;
            }
            tool.id = id;
            tool.kind = kind;
            tool.tier = tier;
            tool.displayName = name;
            tool.description = description;
            tool.iconText = ToolDefinition.KindName(kind).Substring(0, 1);

            // dummy model (gerçek model atanmışsa dokunma)
            string prefabPath = $"{ToolPrefabFolder}/Dummy_{asset}.prefab";
            var root = new GameObject($"Dummy_{asset}");
            buildVisual(root);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            if (modelPrefab != null)
            {
                tool.visualPrefab = modelPrefab;                 // sanatçı modeli her zaman öncelikli
                tool.gripPositionOffset = Vector3.zero;
                tool.gripEulerOffset = Vector3.zero;
            }
            else if (tool.visualPrefab == null || AssetDatabase.GetAssetPath(tool.visualPrefab) == prefabPath)
                tool.visualPrefab = prefab;

            EditorUtility.SetDirty(tool);
            return tool;
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
            LightingSetup.ConfigureCamera(cam); // post-processing + SMAA

            // Gövdenin sağ önü: diğer oyuncular yükü sağ avuçta, göğüs hizasında görür.
            var carryAnchor = new GameObject("CarryAnchor");
            carryAnchor.transform.SetParent(player.transform, false);
            carryAnchor.transform.localPosition = new Vector3(0.24f, 1.12f, 0.36f);

            // Birinci şahıs: sağ elin uzanabileceği mesafede, ekranın sağ altında, küçültülmüş.
            var viewAnchor = new GameObject("CarryViewAnchor");
            viewAnchor.transform.SetParent(pivot.transform, false);
            viewAnchor.transform.localPosition = new Vector3(0.17f, -0.3f, 0.46f);

            player.AddComponent<NetworkIdentity>();

            var netTransform = player.AddComponent<NetworkTransformReliable>();
            netTransform.target = player.transform;
            netTransform.syncDirection = SyncDirection.ClientToServer;
            netTransform.syncScale = false;

            var controller = player.AddComponent<FirstPersonController>();
            controller.syncDirection = SyncDirection.ClientToServer; // bakış eğimi SyncVar'ı
            var so = new SerializedObject(controller);
            so.FindProperty("cameraPivot").objectReferenceValue = pivot.transform;
            so.FindProperty("playerCamera").objectReferenceValue = cam;
            so.FindProperty("characterAnimator").objectReferenceValue = animator;
            var renderers = so.FindProperty("bodyRenderers");
            renderers.arraySize = bodyRenderers.Length;
            for (int i = 0; i < bodyRenderers.Length; i++)
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = bodyRenderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            var playerCarry = player.AddComponent<PlayerCarry>();
            playerCarry.EditorSetup(carryAnchor.transform, viewAnchor.transform, animator);

            // Birinci şahıs kolları: karakterin ikinci kopyası, omuzlar kameranın hemen altında/önünde.
            // Gövde kameranın arkasında kalır; baş ve bacaklar FirstPersonArms tarafından gizlenir.
            var arms = (GameObject)PrefabUtility.InstantiatePrefab(characterVisualPrefab);
            arms.name = "FirstPersonArms";
            arms.transform.SetParent(pivot.transform, false);
            arms.transform.localPosition = new Vector3(0f, -1.70f, 0.13f);
            Object.DestroyImmediate(arms.GetComponent<ProceduralCharacterAnimator>());
            foreach (var smr in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.updateWhenOffscreen = true;
            }
            var firstPersonArms = arms.AddComponent<FirstPersonArms>();
            firstPersonArms.EditorSetup(controller, playerCarry, viewAnchor.transform);
            player.AddComponent<PlayerSkills>();
            var playerTools = player.AddComponent<PlayerTools>();
            playerTools.EditorSetup(animator, firstPersonArms, pivot.transform);
            playerTools.EditorSetSfx(CreateSfxLibrary());
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
                    LightingSetup.ConfigureCamera(cam);
                }
                else if (root.TryGetComponent<Light>(out var light))
                {
                    LightingSetup.ApplySceneLighting(light, skyMaterial);
                }
            }
            LightingSetup.AddGlobalVolume(volumeProfile);

            var diorama = LowPolyWorldBuilder.BuildPlot("MenuDiorama", new LowPolyWorldBuilder.PlotSettings
            {
                Seed = 7,
                HalfSize = 9f,
                Interactive = false,
                TreeCount = 7,
                RockCount = 4,
                GrassCount = 110,
                BushCount = 6,
                FlowerCount = 18,
                OuterTreeCount = 45,
                IncludeShack = true,           // düzeltilmeyi bekleyen eski kulübe
                ClearCenter = new Vector3(0f, 0f, 2f),
                ClearRadius = 4f
            }, kit);
            // oyunun konusu tek karede: satılık arazi, kütük ve balya yığını
            LowPolyWorldBuilder.BuildForSaleSign(diorama.transform, new Vector3(2.1f, 0f, -0.9f), 195f);
            LowPolyWorldBuilder.BuildPropPile(diorama.transform, new Vector3(-1.6f, 0f, -1.6f), 20f);

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
            AddSettingsDocument(uiRoot, panelSettings);

            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
        }

        static void CreateGameScene(PanelSettings panelSettings, LowPolyWorldBuilder.Kit kit, ResourceSet resources, SkillDefinition[] skills,
            ToolDefinition[] tools)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<Camera>() != null)
                    Object.DestroyImmediate(root); // kamera oyuncu prefab'ında
                else if (root.TryGetComponent<Light>(out var light))
                    LightingSetup.ApplySceneLighting(light, skyMaterial);
            }
            LightingSetup.AddGlobalVolume(volumeProfile);

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
                IncludeWorkbench = true,
                PriceBoardText = resources.PriceBoardText
            }, kit);

            var plotNetwork = new GameObject("PlotNetwork");
            plotNetwork.AddComponent<NetworkIdentity>();
            plotNetwork.AddComponent<PlotWorld>().EditorSetup(h, 1, resources.Log, resources.RockChunk, resources.GrassBale, resources.Plank);
            var career = plotNetwork.AddComponent<CareerState>();
            career.EditorSetSkills(skills);
            career.EditorSetTools(tools);

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

            var skillTreeGo = new GameObject("SkillTree");
            skillTreeGo.transform.SetParent(uiRoot.transform, false);
            AddDocument(skillTreeGo, panelSettings, SkillTreeUxmlPath, 5);
            skillTreeGo.AddComponent<SkillTreeController>();

            var benchGo = new GameObject("Workbench");
            benchGo.transform.SetParent(uiRoot.transform, false);
            AddDocument(benchGo, panelSettings, WorkbenchUxmlPath, 6);
            benchGo.AddComponent<WorkbenchController>();

            var menuGo = new GameObject("GameMenu");
            menuGo.transform.SetParent(uiRoot.transform, false);
            AddDocument(menuGo, panelSettings, GameMenuUxmlPath, 10);
            menuGo.AddComponent<GameMenuController>();
            AddSettingsDocument(uiRoot, panelSettings);

            EditorSceneManager.SaveScene(scene, GameScenePath);
        }

        /// <summary>
        /// Sahneyi yeniden açıp PanelSettings'i boş kalan UIDocument'lara bağlar.
        /// (Yeni oluşturulan sahnede atama bazen diske yazılmıyor.)
        /// </summary>
        static void RelinkPanelSettings(string scenePath, PanelSettings settings)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath); // taze referans
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
