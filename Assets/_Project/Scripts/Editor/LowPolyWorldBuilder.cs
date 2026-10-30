using System.Collections.Generic;
using LandFlipper.Art;
using LandFlipper.Gameplay;
using UnityEditor;
using UnityEngine;

namespace LandFlipper.EditorTools
{
    /// <summary>
    /// Low poly arazi kurucu. Phase 2'deki çalışma-zamanı seed üretimi gelene kadar arazi
    /// editörde, sabit seed ile üretilir. Kaynak düğümlerine deterministik ObjectID verilir.
    /// </summary>
    public static class LowPolyWorldBuilder
    {
        public const string MeshFolder = "Assets/_Project/Art/Generated/Meshes";
        public const string MaterialFolder = "Assets/_Project/Art/Materials/LowPoly";

        // ================================================================ kit

        public class Kit
        {
            public Material Grass, OuterGrass, GrassBlade, Dirt, Bark, WoodCut, Leaves, LeavesLight, Pine, Rock, RockDark,
                Wood, WoodDark, Roof, Straw, Twine, Bush, FlowerPink, FlowerYellow, FlowerWhite,
                AwningRed, AwningCream, Delivery, Sign, Crate, ProduceRed, ProduceOrange, SignText;

            public Mesh[] RoundCanopies, PineCanopies, Trunks, Stumps, Rocks, Chunks, GrassPatches, Bushes, Flowers;
            public Mesh Log, Bale, Post, Disc, Box;
        }

        public static Kit LoadKit()
        {
            PlaceholderFolders();
            var k = new Kit
            {
                Grass = Mat("M_Ground", 0.47f, 0.64f, 0.30f),
                OuterGrass = Mat("M_GroundOuter", 0.42f, 0.60f, 0.31f),
                GrassBlade = Mat("M_GrassBlade", 0.56f, 0.74f, 0.30f),
                Dirt = Mat("M_Dirt", 0.56f, 0.43f, 0.29f),
                Bark = Mat("M_Bark", 0.43f, 0.30f, 0.20f),
                WoodCut = Mat("M_WoodCut", 0.87f, 0.71f, 0.48f),
                Leaves = Mat("M_Leaves", 0.36f, 0.62f, 0.28f),
                LeavesLight = Mat("M_LeavesLight", 0.52f, 0.72f, 0.30f),
                Pine = Mat("M_Pine", 0.22f, 0.47f, 0.32f),
                Rock = Mat("M_Rock", 0.62f, 0.62f, 0.60f),
                RockDark = Mat("M_RockDark", 0.49f, 0.49f, 0.48f),
                Wood = Mat("M_Wood", 0.75f, 0.57f, 0.37f),
                WoodDark = Mat("M_WoodDark", 0.50f, 0.36f, 0.24f),
                Roof = Mat("M_Roof", 0.63f, 0.31f, 0.23f),
                Straw = Mat("M_Straw", 0.91f, 0.79f, 0.43f),
                Twine = Mat("M_Twine", 0.56f, 0.39f, 0.23f),
                Bush = Mat("M_Bush", 0.30f, 0.56f, 0.29f),
                FlowerPink = Mat("M_FlowerPink", 0.96f, 0.56f, 0.66f),
                FlowerYellow = Mat("M_FlowerYellow", 0.98f, 0.86f, 0.36f),
                FlowerWhite = Mat("M_FlowerWhite", 0.97f, 0.96f, 0.93f),
                AwningRed = Mat("M_AwningRed", 0.86f, 0.34f, 0.29f),
                AwningCream = Mat("M_AwningCream", 0.97f, 0.93f, 0.81f),
                Delivery = Mat("M_DeliveryPad", 0.96f, 0.77f, 0.31f),
                Sign = Mat("M_SignBoard", 0.95f, 0.90f, 0.78f),
                Crate = Mat("M_Crate", 0.81f, 0.63f, 0.41f),
                ProduceRed = Mat("M_ProduceRed", 0.88f, 0.26f, 0.21f),
                ProduceOrange = Mat("M_ProduceOrange", 0.96f, 0.56f, 0.19f)
            };

            k.RoundCanopies = Meshes("Canopy_Round", 4, LowPolyMeshFactory.RoundCanopy, 100);
            k.PineCanopies = Meshes("Canopy_Pine", 3, LowPolyMeshFactory.PineCanopy, 200);
            k.Trunks = Meshes("Trunk", 3, LowPolyMeshFactory.Trunk, 300);
            k.Stumps = Meshes("Stump", 2, LowPolyMeshFactory.Stump, 400);
            k.Rocks = Meshes("Rock", 4, LowPolyMeshFactory.Rock, 500);
            k.Chunks = Meshes("RockChunk", 3, LowPolyMeshFactory.RockChunk, 600);
            k.GrassPatches = Meshes("GrassPatch", 4, s => LowPolyMeshFactory.GrassPatch(s), 700);
            k.Bushes = Meshes("Bush", 3, LowPolyMeshFactory.Bush, 800);
            k.Flowers = Meshes("Flowers", 3, LowPolyMeshFactory.FlowerCluster, 900);
            k.Log = SaveMesh(LowPolyMeshFactory.Log(1000, 1.1f, 0.17f), "Log");
            k.Bale = SaveMesh(LowPolyMeshFactory.Bale(), "Bale");
            k.Post = SaveMesh(LowPolyMeshFactory.FencePost(), "FencePost");
            k.Disc = SaveMesh(LowPolyMeshFactory.Disc(8, "Disc8"), "Disc8");
            k.Box = SaveMesh(LowPolyMeshFactory.UnitBox("UnitBox"), "UnitBox");

            // Stilize çevre: ProBuilder ile yumuşak modeller + boyalı dokular (low poly sürümün yerine)
            StylizedKit.Apply(k);
            return k;
        }

        // ================================================================ plot

        public struct PlotSettings
        {
            public int Seed;
            public float HalfSize;
            public bool Interactive;
            public int TreeCount, RockCount, GrassCount, BushCount, FlowerCount, OuterTreeCount;
            public Vector3 ClearCenter;
            public float ClearRadius;
            public bool IncludeShack;
            public bool IncludeSellStation;
            public bool IncludeWorkbench;
            public string PriceBoardText;
        }

        static System.Random rng;
        static Kit kit;
        static int seed;

        public static GameObject BuildPlot(string name, PlotSettings s, Kit k)
        {
            rng = new System.Random(s.Seed);
            kit = k;
            seed = s.Seed;
            float h = s.HalfSize;
            var root = new GameObject(name);
            var placer = new Placer(rng, h);

            // ---- zemin
            var plotMesh = LowPolyMeshFactory.PlotTerrain(h, 2f, seed);
            StylizedKit.SoftenTerrain(plotMesh, 0.22f);
            var ground = MeshObject("PlotGround", root.transform, SaveMesh(plotMesh, $"PlotTerrain_{seed}"), k.Grass);
            ground.AddComponent<MeshCollider>();
            var outerMesh = LowPolyMeshFactory.OuterTerrain(h, 320f, 7f, seed);
            StylizedKit.SoftenTerrain(outerMesh, 0.12f);
            var outer = MeshObject("OuterGround", root.transform, SaveMesh(outerMesh, $"OuterTerrain_{seed}"), k.OuterGrass);
            outer.AddComponent<MeshCollider>();
            Physics.SyncTransforms();

            // ---- rezerve alanlar
            placer.Reserve(s.ClearCenter, s.ClearRadius, 1.5f);
            Vector3 shackPos = new Vector3(-h * 0.5f, 0f, h * 0.45f);
            Vector3 stationPos = new Vector3(6.5f, 0f, -h + 4.5f);
            if (s.IncludeShack) placer.Reserve(shackPos, 4f, 3f);
            if (s.IncludeSellStation) placer.Reserve(stationPos, 5f, 3.5f);
            Vector3 benchPos = new Vector3(-6.5f, 0f, -h + 4.5f); // girişin solunda, satış tezgâhının karşısında
            if (s.IncludeWorkbench) placer.Reserve(benchPos, 4.5f, 3.5f);

            var staticGroup = Group("Static", root.transform);
            BuildFence(staticGroup, h);

            var dirt = Group("DirtPatches", staticGroup);
            for (int i = 0; i < Mathf.RoundToInt(h * 0.5f); i++)
            {
                var p = placer.RandomPoint(h - 2f);
                float r = Range(1.2f, 3f);
                var d = MeshObject("Dirt", dirt, k.Disc, k.Dirt);
                d.transform.localPosition = Ground(p) + Vector3.down * 0.01f;
                d.transform.localScale = new Vector3(r, 0.035f, r * Range(0.6f, 1f));
                d.transform.localRotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);
            }

            // ---- kaynak düğümleri (ObjectID sırası deterministik)
            int nextId = 1;
            var nodes = Group(s.Interactive ? "ResourceNodes" : "Decor_Nodes", root.transform);
            var trees = Group("Trees", nodes);
            for (int i = 0; i < s.TreeCount; i++)
                if (placer.TryPlace(2.1f, 0.5f, h - 1.5f, out var p)) BuildTree(trees, p, nextId++, s.Interactive);

            var rocks = Group("Rocks", nodes);
            for (int i = 0; i < s.RockCount; i++)
            {
                float size = Range(0.5f, 1.1f);
                if (placer.TryPlace(size + 0.8f, size * 0.9f, h - 1.2f, out var p)) BuildRock(rocks, p, size, nextId++, s.Interactive);
            }

            var grass = Group("Grass", nodes);
            for (int i = 0; i < s.GrassCount; i++)
                if (placer.TryPlaceSmall(0.32f, h - 0.6f, out var p)) BuildGrass(grass, p, nextId++, s.Interactive);

            var bushes = Group("Bushes", nodes);
            for (int i = 0; i < s.BushCount; i++)
                if (placer.TryPlaceSmall(0.7f, h - 0.8f, out var p))
                    BuildBush(bushes, p, Range(0.8f, 1.3f), nextId++, s.Interactive);

            if (s.IncludeShack) BuildShack(nodes, shackPos, nextId++, s.Interactive);

            // ---- dekor
            var decor = Group("Decor", staticGroup);
            for (int i = 0; i < s.FlowerCount; i++)
                if (placer.TryPlaceSmall(0.35f, h - 0.6f, out var p))
                {
                    var flower = rng.Next(3) switch { 0 => k.FlowerPink, 1 => k.FlowerYellow, _ => k.FlowerWhite };
                    Decor(decor, k.Flowers[rng.Next(k.Flowers.Length)], new[] { k.Bush, flower }, p, Range(0.9f, 1.4f));
                }

            var outerDecor = Group("OuterDecor", staticGroup);
            for (int i = 0; i < s.OuterTreeCount; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                float dist = Range(h + 4f, h + 55f);
                var p = new Vector3(Mathf.Cos(a) * dist, 0f, Mathf.Sin(a) * dist);
                if (Mathf.Abs(p.x) < 5f && p.z < 0f && p.z > -h - 8f) continue; // giriş önünü açık bırak
                BuildDecorTree(outerDecor, p, Range(1.2f, 2.1f));
            }
            for (int i = 0; i < s.OuterTreeCount / 3; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                float dist = Range(h + 3f, h + 40f);
                var p = new Vector3(Mathf.Cos(a) * dist, 0f, Mathf.Sin(a) * dist);
                Decor(outerDecor, k.Rocks[rng.Next(k.Rocks.Length)], new[] { k.RockDark }, p, Range(0.8f, 2.2f));
            }

            if (s.IncludeSellStation) BuildSellStation(root.transform, stationPos, s.PriceBoardText);
            if (s.IncludeWorkbench) BuildWorkbench(root.transform, benchPos);

            foreach (var t in staticGroup.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            GameObjectUtility.SetStaticEditorFlags(outer, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);

            return root;
        }

        // ================================================================ parçalar

        static void BuildTree(Transform parent, Vector3 p, int id, bool interactive)
        {
            bool pine = rng.NextDouble() < 0.4;
            float height = Range(2.3f, 4.2f);
            float trunkRadius = Range(0.17f, 0.26f);
            float canopy = pine ? Range(1.0f, 1.35f) : Range(1.3f, 1.85f);

            var tree = new GameObject(interactive ? $"Tree_{id}" : "Tree");
            tree.transform.SetParent(parent, false);
            tree.transform.localPosition = Ground(p) + Vector3.down * 0.05f;
            tree.transform.localRotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);

            var pivot = new GameObject("FallPivot");
            pivot.transform.SetParent(tree.transform, false);

            var trunk = MeshObject("Trunk", pivot.transform, kit.Trunks[rng.Next(kit.Trunks.Length)], kit.Bark);
            trunk.transform.localScale = new Vector3(trunkRadius, height, trunkRadius);

            Mesh canopyMesh = pine ? kit.PineCanopies[rng.Next(kit.PineCanopies.Length)] : kit.RoundCanopies[rng.Next(kit.RoundCanopies.Length)];
            Material leaves = pine ? kit.Pine : (rng.NextDouble() < 0.5 ? kit.Leaves : kit.LeavesLight);
            var crown = MeshObject("Canopy", pivot.transform, canopyMesh, leaves);
            crown.transform.localPosition = new Vector3(0f, pine ? height * 0.45f : height + canopy * 0.45f, 0f);
            crown.transform.localScale = pine ? new Vector3(canopy, canopy * 1.25f, canopy) : Vector3.one * canopy;
            crown.transform.localRotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);

            var trunkCollider = pivot.AddComponent<CapsuleCollider>();
            trunkCollider.radius = trunkRadius;
            trunkCollider.height = height;
            trunkCollider.center = new Vector3(0f, height * 0.5f, 0f);

            if (!interactive) return;

            var stump = MeshObject("Stump", tree.transform, kit.Stumps[rng.Next(kit.Stumps.Length)], kit.Bark, kit.WoodCut);
            stump.transform.localScale = new Vector3(trunkRadius * 1.08f, 0.38f, trunkRadius * 1.08f);
            var stumpCollider = stump.AddComponent<CapsuleCollider>();
            stumpCollider.radius = 1f;
            stumpCollider.height = 1f;
            stumpCollider.center = new Vector3(0f, 0.5f, 0f);
            stump.SetActive(false);

            int hits = 3 + Mathf.RoundToInt((height - 2.3f) * 1.1f);
            tree.AddComponent<TreeNode>().EditorSetup(id, hits, pivot.transform, stump, trunkCollider, height, canopy);
        }

        static void BuildDecorTree(Transform parent, Vector3 p, float scale)
        {
            BuildTree(parent, p, 0, false);
            var t = parent.GetChild(parent.childCount - 1);
            t.localScale = Vector3.one * scale;
            t.localPosition = new Vector3(p.x, OuterHeightProbe(t.localPosition), p.z);
        }

        static void BuildRock(Transform parent, Vector3 p, float size, int id, bool interactive)
        {
            var rock = new GameObject(interactive ? $"Rock_{id}" : "Rock");
            rock.transform.SetParent(parent, false);
            rock.transform.localPosition = Ground(p);
            rock.transform.localRotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);

            var visual = MeshObject("Visual", rock.transform, kit.Rocks[rng.Next(kit.Rocks.Length)], rng.NextDouble() < 0.5 ? kit.Rock : kit.RockDark);
            visual.transform.localPosition = new Vector3(0f, size * 0.35f, 0f);
            visual.transform.localScale = new Vector3(size, size * Range(0.7f, 0.95f), size * Range(0.8f, 1.1f));
            var collider = visual.AddComponent<MeshCollider>();
            collider.convex = true;

            if (!interactive) return;
            int hits = 2 + Mathf.RoundToInt(size * 3f);
            int chunks = 2 + Mathf.RoundToInt(size * 4f);
            rock.AddComponent<RockNode>().EditorSetup(id, hits, visual.transform, collider, chunks, size);
        }

        static void BuildGrass(Transform parent, Vector3 p, int id, bool interactive)
        {
            var patch = new GameObject(interactive ? $"Grass_{id}" : "Grass");
            patch.transform.SetParent(parent, false);
            patch.transform.localPosition = Ground(p);
            patch.transform.localRotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);

            var visual = MeshObject("Visual", patch.transform, kit.GrassPatches[rng.Next(kit.GrassPatches.Length)], kit.GrassBlade);
            float s = Range(0.9f, 1.4f);
            visual.transform.localScale = new Vector3(s, Range(0.85f, 1.3f), s);
            visual.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (!interactive) return;
            var trigger = patch.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.85f * s, 0.6f, 0.85f * s);
            trigger.center = new Vector3(0f, 0.3f, 0f);
            patch.AddComponent<GrassNode>().EditorSetup(id, visual.transform, trigger);
        }

        static void BuildFence(Transform parent, float h)
        {
            var fence = Group("Fence", parent);
            const float spacing = 2f;
            const float gate = 2.5f;

            void Post(Vector3 p)
            {
                var post = MeshObject("Post", fence, kit.Post, kit.WoodDark);
                post.transform.localPosition = Ground(p) + Vector3.down * 0.05f;
                post.transform.localRotation = Quaternion.Euler(Range(-3f, 3f), Range(-8f, 8f), Range(-3f, 3f));
                var c = post.AddComponent<BoxCollider>();
                c.size = new Vector3(0.2f, 1.25f, 0.2f);
                c.center = new Vector3(0f, 0.62f, 0f);
            }

            void Rail(Vector3 center, Vector3 size)
            {
                var rail = MeshObject("Rail", fence, kit.Box, kit.Wood);
                rail.transform.localPosition = center;
                rail.transform.localScale = size;
                rail.AddComponent<BoxCollider>();
            }

            for (float x = -h; x <= h + 0.01f; x += spacing)
            {
                Post(new Vector3(x, 0f, h));
                if (Mathf.Abs(x) >= gate) Post(new Vector3(x, 0f, -h));
            }
            for (float z = -h + spacing; z <= h - spacing + 0.01f; z += spacing)
            {
                Post(new Vector3(-h, 0f, z));
                Post(new Vector3(h, 0f, z));
            }

            foreach (float y in new[] { 0.42f, 0.88f })
            {
                Rail(new Vector3(0f, y, h), new Vector3(h * 2f, 0.1f, 0.06f));
                Rail(new Vector3(-h, y, 0f), new Vector3(0.06f, 0.1f, h * 2f));
                Rail(new Vector3(h, y, 0f), new Vector3(0.06f, 0.1f, h * 2f));
                float half = (h - gate) * 0.5f;
                Rail(new Vector3(-gate - half, y, -h), new Vector3(h - gate, 0.1f, 0.06f));
                Rail(new Vector3(gate + half, y, -h), new Vector3(h - gate, 0.1f, 0.06f));
            }

            // giriş kemeri
            var archL = MeshObject("GatePostL", fence, kit.Box, kit.WoodDark);
            archL.transform.localPosition = new Vector3(-gate, 1.2f, -h);
            archL.transform.localScale = new Vector3(0.22f, 2.4f, 0.22f);
            archL.AddComponent<BoxCollider>();
            var archR = MeshObject("GatePostR", fence, kit.Box, kit.WoodDark);
            archR.transform.localPosition = new Vector3(gate, 1.2f, -h);
            archR.transform.localScale = new Vector3(0.22f, 2.4f, 0.22f);
            archR.AddComponent<BoxCollider>();
            var beam = MeshObject("GateBeam", fence, kit.Box, kit.Wood);
            beam.transform.localPosition = new Vector3(0f, 2.35f, -h);
            beam.transform.localScale = new Vector3(gate * 2f + 0.6f, 0.2f, 0.18f);
        }

        static void BuildBush(Transform parent, Vector3 p, float scale, int id, bool interactive)
        {
            var bush = new GameObject(interactive ? $"Bush_{id}" : "Bush");
            bush.transform.SetParent(parent, false);
            bush.transform.localPosition = Ground(p);
            bush.transform.localRotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);

            var visual = MeshObject("Visual", bush.transform, kit.Bushes[rng.Next(kit.Bushes.Length)], kit.Bush);
            visual.transform.localScale = Vector3.one * scale;

            if (!interactive) return;
            var collider = bush.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.2f, 0.8f, 1.2f) * scale;
            collider.center = new Vector3(0f, 0.4f * scale, 0f);
            int bales = scale > 1.15f ? 2 : 1;
            bush.AddComponent<BushNode>().EditorSetup(id, 2, visual.transform, collider, bales);
        }

        /// <summary>
        /// Stilize, eski ahşap kulübe: her parça (duvar, çatı, zemin...) ProBuilder ile üretilmiş
        /// pahlı, hafif eğri, dokulu tahtalardan oluşan bir gruptur. Sökülme sırası ve grup kökündeki
        /// collider'lar ShackNode ile aynı kalır.
        /// </summary>
        static void BuildShack(Transform parent, Vector3 p, int id, bool interactive)
        {
            var shack = new GameObject(interactive ? $"Shack_{id}" : "Shack (Ruin)");
            shack.transform.SetParent(parent, false);
            shack.transform.localPosition = Ground(p);
            shack.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
            var t = shack.transform;
            var parts = new List<Transform>();
            var rng = new System.Random(id * 7 + 3);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float Q(float v) => Mathf.Round(v * 20f) / 20f;   // 5 cm adım: aynı boydaki tahtalar mesh paylaşır
            int variant = 0;

            Transform Part(string n, Vector3 pos, Vector3 euler)
            {
                var g = new GameObject(n).transform;
                g.SetParent(t, false);
                g.localPosition = pos;
                g.localRotation = Quaternion.Euler(euler);
                parts.Add(g);
                return g;
            }

            void Board(Transform g, Vector3 pos, Vector3 size, Vector3 euler, Material m, bool roof = false)
            {
                var mesh = StylizedKit.Board(size, variant++ % 3, roof ? 0.85f : 0.6f, roof ? "SRoofPanel" : "SBoard");
                var go = MeshObject(roof ? "Panel" : "Board", g, mesh, m);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(euler);
            }

            // Dikey tahta duvar. along: tahtaların dizildiği yerel eksen (X veya Z), outward: dış yüz yönü (±1)
            void Wall(Transform g, float width, float height, bool alongX, float outward, int skip = -1)
            {
                int n = Mathf.Max(2, Mathf.RoundToInt(width / 0.31f));
                float w = width / n;
                for (int i = 0; i < n; i++)
                {
                    if (i == skip) continue;
                    float h = Q(height + R(-0.12f, 0.1f));
                    float o = -width * 0.5f + w * (i + 0.5f);
                    var size = alongX ? new Vector3(w - 0.015f, h, 0.07f) : new Vector3(0.07f, h, w - 0.015f);
                    var pos = alongX ? new Vector3(o, h * 0.5f - height * 0.5f, 0f) : new Vector3(0f, h * 0.5f - height * 0.5f, o);
                    float tilt = R(-1.8f, 1.8f);
                    Board(g, pos, size, alongX ? new Vector3(0f, 0f, tilt) : new Vector3(tilt, 0f, 0f), (i % 4 == 2) ? kit.WoodDark : kit.Wood);
                }
                // dışta iki yatay kuşak
                foreach (float y in new[] { -height * 0.28f, height * 0.26f })
                {
                    var size = alongX ? new Vector3(width + 0.06f, 0.12f, 0.05f) : new Vector3(0.05f, 0.12f, width + 0.06f);
                    var pos = alongX ? new Vector3(0f, y, 0.06f * outward) : new Vector3(0.06f * outward, y, 0f);
                    Board(g, pos, size, Vector3.zero, kit.WoodDark);
                }
            }

            void Collide(Transform g)
            {
                var bounds = new Bounds();
                bool any = false;
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
                {
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var local = g.InverseTransformPoint(mf.transform.TransformPoint(corner));
                        if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                        else bounds.Encapsulate(local);
                    }
                }
                var c = g.gameObject.AddComponent<BoxCollider>();
                c.center = bounds.center;
                c.size = bounds.size;
            }

            // Sökülme sırası: yerdeki tahtalar → çatı → duvarlar → zemin
            var plankA = Part("PlankA", new Vector3(2.8f, 0.1f, -0.9f), new Vector3(0f, 25f, 4f));
            Board(plankA, Vector3.zero, new Vector3(0.24f, 0.07f, 2.2f), Vector3.zero, kit.WoodDark);
            var plankB = Part("PlankB", new Vector3(2.5f, 0.16f, 0.3f), new Vector3(0f, -15f, 8f));
            Board(plankB, Vector3.zero, new Vector3(0.24f, 0.07f, 1.8f), Vector3.zero, kit.Wood);

            // Çatı: kiremit dokulu kalın panel + altta koyu saçak tahtası (RoofR'de +X aşağıda, RoofL'de -X)
            var roofR = Part("RoofR", new Vector3(1.2f, 2.35f, 0.4f), new Vector3(0f, 0f, -34f));
            Board(roofR, Vector3.zero, new Vector3(2.0f, 0.1f, 3.0f), Vector3.zero, kit.Roof, true);
            Board(roofR, new Vector3(0.98f, -0.04f, 0f), new Vector3(0.12f, 0.12f, 3.1f), Vector3.zero, kit.WoodDark);
            var roofL = Part("RoofL", new Vector3(-1.05f, 2.55f, 0f), new Vector3(0f, 0f, 28f));
            Board(roofL, Vector3.zero, new Vector3(2.4f, 0.1f, 4.0f), Vector3.zero, kit.Roof, true);
            Board(roofL, new Vector3(-1.18f, -0.04f, 0f), new Vector3(0.12f, 0.12f, 4.1f), Vector3.zero, kit.WoodDark);
            Board(roofL, new Vector3(1.18f, -0.05f, 0f), new Vector3(0.15f, 0.15f, 4.2f), Vector3.zero, kit.WoodDark); // mahya kirişi

            // Duvarlar (sağ duvar yıkık: kısa ve bir tahtası eksik)
            var wallRight = Part("WallRight", new Vector3(1.95f, 0.8f, 0.4f), new Vector3(0f, 0f, 5f));
            Wall(wallRight, 2.5f, 1.4f, false, 1f, 5);
            var wallFrontR = Part("WallFrontR", new Vector3(1.4f, 1.15f, -1.65f), Vector3.zero);
            Wall(wallFrontR, 1.2f, 2.1f, true, -1f);
            Board(wallFrontR, new Vector3(0.62f, 0.05f, -0.03f), new Vector3(0.14f, 2.3f, 0.14f), Vector3.zero, kit.WoodDark); // köşe dikmesi
            var wallFrontL = Part("WallFrontL", new Vector3(-1.25f, 1.15f, -1.65f), Vector3.zero);
            Wall(wallFrontL, 1.5f, 2.1f, true, -1f);
            Board(wallFrontL, new Vector3(-0.77f, 0.05f, -0.03f), new Vector3(0.14f, 2.3f, 0.14f), Vector3.zero, kit.WoodDark);
            Board(wallFrontL, new Vector3(0.78f, 0.05f, -0.03f), new Vector3(0.12f, 2.3f, 0.12f), Vector3.zero, kit.WoodDark); // kapı kasası
            var wallLeft = Part("WallLeft", new Vector3(-1.95f, 1.15f, 0f), Vector3.zero);
            Wall(wallLeft, 3.4f, 2.1f, false, -1f);
            var wallBack = Part("WallBack", new Vector3(0f, 1.15f, 1.65f), Vector3.zero);
            Wall(wallBack, 4f, 2.1f, true, 1f);
            Board(wallBack, new Vector3(-1.98f, 0.05f, 0.03f), new Vector3(0.14f, 2.3f, 0.14f), Vector3.zero, kit.WoodDark);
            Board(wallBack, new Vector3(1.98f, 0.05f, 0.03f), new Vector3(0.14f, 2.3f, 0.14f), Vector3.zero, kit.WoodDark);

            // Zemin: X boyunca uzanan tahtalar
            var floor = Part("Floor", new Vector3(0f, 0.08f, 0f), Vector3.zero);
            {
                int n = 11;
                float w = 3.4f / n;
                for (int i = 0; i < n; i++)
                    Board(floor, new Vector3(R(-0.04f, 0.04f), R(-0.01f, 0.015f), -1.7f + w * (i + 0.5f)),
                        new Vector3(4f, 0.07f, w - 0.012f), new Vector3(0f, R(-0.6f, 0.6f), 0f), i % 3 == 1 ? kit.Wood : kit.WoodDark);
                Board(floor, new Vector3(0f, -0.06f, -1.45f), new Vector3(4.05f, 0.1f, 0.12f), Vector3.zero, kit.WoodDark); // altlık
                Board(floor, new Vector3(0f, -0.06f, 1.45f), new Vector3(4.05f, 0.1f, 0.12f), Vector3.zero, kit.WoodDark);
            }

            foreach (var part in parts) Collide(part);
            if (interactive)
                shack.AddComponent<ShackNode>().EditorSetup(id, parts.ToArray(), 2);
        }

        // ================================================================ stilize ahşap yapı yardımcıları

        static int boardVariant;
        static Material iron;

        static Material Iron() => iron != null ? iron : (iron = Mat("M_Iron", 0.30f, 0.32f, 0.36f, 0.32f));

        /// <summary>Pahlı, dokulu ProBuilder tahtası. wobble: eğrilik/yamuk uç miktarı (0 = dümdüz).</summary>
        static GameObject SB(string n, Transform parent, Vector3 pos, Vector3 size, Vector3 euler, Material m,
            bool collide = false, float wobble = 1f, float tiling = 0.6f, string prefix = "SBoard")
        {
            var mesh = StylizedKit.Board(size, boardVariant++ % 3, tiling, prefix, wobble);
            var go = MeshObject(n, parent, mesh, m);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            if (collide) go.AddComponent<BoxCollider>();
            return go;
        }

        /// <summary>Tahta çerçeveli tabela panosu (yazı z = -thickness/2 - 0.012'ye konur).</summary>
        static void FramedBoard(Transform parent, Vector2 size, float thickness, Material board)
        {
            SB("Board", parent, Vector3.zero, new Vector3(size.x, size.y, thickness), Vector3.zero, board, false, 0.25f, 0.9f);
            float f = 0.085f, d = thickness + 0.04f;
            SB("FrameTop", parent, new Vector3(0f, size.y * 0.5f, 0.01f), new Vector3(size.x + f * 2f, f, d), Vector3.zero, kit.WoodDark, false, 0.4f);
            SB("FrameBottom", parent, new Vector3(0f, -size.y * 0.5f, 0.01f), new Vector3(size.x + f * 2f, f, d), Vector3.zero, kit.WoodDark, false, 0.4f);
            SB("FrameL", parent, new Vector3(-size.x * 0.5f - f * 0.5f, 0f, 0.01f), new Vector3(f, size.y, d), Vector3.zero, kit.WoodDark, false, 0.2f);
            SB("FrameR", parent, new Vector3(size.x * 0.5f + f * 0.5f, 0f, 0.01f), new Vector3(f, size.y, d), Vector3.zero, kit.WoodDark, false, 0.2f);
        }

        /// <summary>Çıtalı tahta kasa (merkez = kasanın ortası). Collider kökte.</summary>
        static Transform Crate(Transform parent, Vector3 pos, Vector3 size, float yaw)
        {
            var c = new GameObject("Crate").transform;
            c.SetParent(parent, false);
            c.localPosition = pos;
            c.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float h = size.y, slat = h * 0.38f, t = 0.04f;
            foreach (float y in new[] { -h * 0.25f, h * 0.25f })
            {
                SB("Slat", c, new Vector3(0f, y, -size.z * 0.5f + t * 0.5f), new Vector3(size.x, slat, t), Vector3.zero, kit.Crate, false, 0.5f);
                SB("Slat", c, new Vector3(0f, y, size.z * 0.5f - t * 0.5f), new Vector3(size.x, slat, t), Vector3.zero, kit.Crate, false, 0.5f);
                SB("Slat", c, new Vector3(-size.x * 0.5f + t * 0.5f, y, 0f), new Vector3(t, slat, size.z - t * 2f), Vector3.zero, kit.Crate, false, 0.5f);
                SB("Slat", c, new Vector3(size.x * 0.5f - t * 0.5f, y, 0f), new Vector3(t, slat, size.z - t * 2f), Vector3.zero, kit.Crate, false, 0.5f);
            }
            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                SB("Corner", c, new Vector3(x * (size.x * 0.5f - 0.03f), 0f, z * (size.z * 0.5f - 0.03f)), new Vector3(0.06f, h, 0.06f), Vector3.zero, kit.WoodDark, false, 0.2f);
            SB("Bottom", c, new Vector3(0f, -h * 0.5f + 0.02f, 0f), new Vector3(size.x - 0.04f, 0.04f, size.z - 0.04f), Vector3.zero, kit.WoodDark, false, 0.2f);
            SB("Fill", c, new Vector3(0f, h * 0.3f, 0f), new Vector3(size.x - 0.08f, 0.04f, size.z - 0.08f), Vector3.zero, kit.WoodDark, false, 0f);
            var col = c.gameObject.AddComponent<BoxCollider>();
            col.size = size;
            return c;
        }

        static void Barrel(Transform parent, Vector3 pos, float radius, float height)
        {
            var barrel = MeshObject("Barrel", parent, StylizedKit.Barrel(), kit.Crate, kit.WoodCut);
            barrel.transform.localPosition = pos;
            barrel.transform.localScale = new Vector3(radius, height, radius);
            foreach (float y in new[] { 0.18f, 0.82f })
            {
                float bulge = 0.9f + Mathf.Sin(y * Mathf.PI) * 0.1f;
                var hoop = MeshObject("Hoop", barrel.transform, StylizedKit.RoundSlab(), Iron(), Iron());
                hoop.transform.localPosition = new Vector3(0f, y - 0.035f, 0f);
                hoop.transform.localScale = new Vector3(bulge * 1.025f, 0.07f, bulge * 1.025f);
            }
            var col = barrel.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.5f, 0f);
            col.height = 1f;
            col.radius = 0.95f;
        }

        static BoxCollider GroupCollider(Transform group, Vector3 center, Vector3 size)
        {
            var c = group.gameObject.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
            return c;
        }

        /// <summary>Satış tezgâhı: pahlı tahtalar, kanvas çizgili tente ve yuvarlak saçak dilleri, çerçeveli tabelalar.</summary>
        static void BuildSellStation(Transform parent, Vector3 p, string priceText)
        {
            var stall = new GameObject("SellStation");
            stall.transform.SetParent(parent, false);
            stall.transform.localPosition = Ground(p);
            stall.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // ön yüz (-Z) girişe bakar
            var t = stall.transform;

            // zemin: X boyunca tahtalar + altlık kirişleri
            var platform = Group("Platform", t);
            {
                const int n = 9;
                float w = 3f / n;
                for (int i = 0; i < n; i++)
                    SB("Plank", platform, new Vector3(Range(-0.03f, 0.03f), 0.12f, -1.3f + w * (i + 0.5f)), new Vector3(3.8f, 0.08f, w - 0.015f),
                        new Vector3(0f, Range(-0.5f, 0.5f), 0f), i % 3 == 1 ? kit.Wood : kit.WoodDark, false, 0.4f);
                foreach (float z in new[] { -1.15f, 0.2f, 1.55f })
                    SB("Beam", platform, new Vector3(0f, 0.04f, z), new Vector3(3.9f, 0.1f, 0.14f), Vector3.zero, kit.WoodDark, false, 0.2f);
                GroupCollider(platform, new Vector3(0f, 0.08f, 0.2f), new Vector3(3.8f, 0.16f, 3f));
            }

            // tezgâh: dikey tahta ön yüz, yan paneller, kırmızı kuşak, kalın üst tabla
            var counter = Group("Counter", t);
            {
                const int n = 10;
                float w = 3.2f / n;
                for (int i = 0; i < n; i++)
                {
                    float h = 0.92f + Range(-0.02f, 0.03f);
                    SB("Front", counter, new Vector3(-1.6f + w * (i + 0.5f), 0.14f + h * 0.5f, -1.03f), new Vector3(w - 0.012f, h, 0.06f),
                        new Vector3(0f, 0f, Range(-1f, 1f)), i % 4 == 1 ? kit.WoodDark : kit.Wood, false, 0.5f);
                }
                foreach (float x in new[] { -1.57f, 1.57f })
                    SB("Side", counter, new Vector3(x, 0.6f, -0.75f), new Vector3(0.06f, 0.92f, 0.58f), Vector3.zero, kit.Wood, false, 0.3f);
                SB("Stripe", counter, new Vector3(0f, 0.62f, -1.08f), new Vector3(3.26f, 0.17f, 0.045f), Vector3.zero, kit.AwningRed, false, 0.25f);
                SB("Top", counter, new Vector3(0f, 1.11f, -0.78f), new Vector3(3.45f, 0.1f, 0.8f), Vector3.zero, kit.WoodDark, false, 0.3f);
                SB("TopLip", counter, new Vector3(0f, 1.07f, -1.17f), new Vector3(3.5f, 0.1f, 0.07f), Vector3.zero, kit.Wood, false, 0.3f);
                GroupCollider(counter, new Vector3(0f, 0.62f, -0.75f), new Vector3(3.3f, 1.05f, 0.7f));
            }

            // direkler + köşe payandaları
            foreach (var x in new[] { -1.65f, 1.65f })
            foreach (var z in new[] { -1.05f, 1.35f })
            {
                SB("Post", t, new Vector3(x, 1.35f, z), new Vector3(0.16f, 2.7f, 0.16f), Vector3.zero, kit.WoodDark, true, 0.3f);
                float inward = -Mathf.Sign(x);
                SB("Brace", t, new Vector3(x + inward * 0.2f, 2.42f, z), new Vector3(0.07f, 0.5f, 0.07f), new Vector3(0f, 0f, inward * -45f), kit.WoodDark, false, 0.2f);
            }
            foreach (var z in new[] { -1.05f, 1.35f })
                SB("Header", t, new Vector3(0f, 2.62f, z), new Vector3(3.5f, 0.12f, 0.12f), Vector3.zero, kit.WoodDark, false, 0.3f);

            // çizgili kanvas tente + yuvarlak saçak dilleri
            var awning = new GameObject("Awning").transform;
            awning.SetParent(t, false);
            awning.localPosition = new Vector3(0f, 2.82f, 0.15f);
            awning.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            {
                const int stripes = 7;
                float width = 3.9f / stripes;
                var slab = StylizedKit.RoundSlab();
                for (int i = 0; i < stripes; i++)
                {
                    var m = i % 2 == 0 ? kit.AwningRed : kit.AwningCream;
                    float x = -1.95f + width * (i + 0.5f);
                    SB("Stripe", awning, new Vector3(x, 0f, 0f), new Vector3(width - 0.004f, 0.05f, 3.0f), Vector3.zero, m, false, 0.35f, 0.8f);
                    SB("Flap", awning, new Vector3(x, -0.07f, -1.5f), new Vector3(width - 0.004f, 0.13f, 0.035f), Vector3.zero, m, false, 0f, 0.8f);
                    var tongue = MeshObject("FlapRound", awning, slab, m, m);
                    tongue.transform.localPosition = new Vector3(x, -0.135f, -1.5f - 0.0175f);
                    tongue.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    tongue.transform.localScale = new Vector3(width * 0.5f - 0.004f, 0.035f, 0.12f); // basık yarım daire: üst yarısı saçak tahtasında gizli
                }
                SB("Ridge", awning, new Vector3(0f, 0.05f, 1.45f), new Vector3(3.95f, 0.08f, 0.12f), Vector3.zero, kit.WoodDark, false, 0.3f);
            }

            // tabela (satış olunca zıplar): askı çubuklarıyla tentenin üstünde
            var sign = new GameObject("Sign");
            sign.transform.SetParent(t, false);
            sign.transform.localPosition = new Vector3(0f, 3.45f, -1.2f);
            FramedBoard(sign.transform, new Vector2(2.2f, 0.6f), 0.08f, kit.Sign);
            foreach (float x in new[] { -0.8f, 0.8f })
                SB("Hanger", sign.transform, new Vector3(x, -0.42f, 0.04f), new Vector3(0.06f, 0.3f, 0.06f), Vector3.zero, kit.WoodDark, false, 0f);
            Label(sign.transform, "SATIŞ", new Vector3(0f, 0f, -0.055f), 0.055f, SignInk);

            // fiyat panosu: ayak + çerçeveli pano + küçük çatı
            var board = new GameObject("PriceBoard");
            board.transform.SetParent(t, false);
            board.transform.localPosition = new Vector3(-2.45f, 0f, -0.9f);
            board.transform.localRotation = Quaternion.Euler(0f, -20f, 0f);
            SB("Leg", board.transform, new Vector3(0f, 0.7f, 0.06f), new Vector3(0.11f, 1.4f, 0.11f), Vector3.zero, kit.WoodDark, true, 0.3f);
            var panel = new GameObject("Panel").transform;
            panel.SetParent(board.transform, false);
            panel.localPosition = new Vector3(0f, 1.45f, 0f);
            FramedBoard(panel, new Vector2(1.15f, 1.0f), 0.06f, kit.Sign);
            SB("Cap", panel, new Vector3(0f, 0.6f, 0.01f), new Vector3(1.45f, 0.06f, 0.24f), new Vector3(0f, 0f, 0f), kit.Roof, false, 0.3f, 0.85f, "SRoofPanel");
            Label(board.transform, priceText, new Vector3(0f, 1.45f, -0.045f), 0.0095f, SignInk);

            // kasalar ve ürünler
            for (int i = 0; i < 3; i++)
            {
                var crate = Crate(t, new Vector3(-1.1f + i * 1.1f, 0.42f, 0.9f), new Vector3(0.75f, 0.5f, 0.55f), Range(-6f, 6f));
                var produce = i == 1 ? kit.ProduceOrange : kit.ProduceRed;
                for (int j = 0; j < 5; j++)
                {
                    var fruit = MeshObject("Produce", crate, kit.Chunks[rng.Next(kit.Chunks.Length)], produce);
                    fruit.transform.localPosition = new Vector3(Range(-0.24f, 0.24f), 0.22f, Range(-0.15f, 0.15f));
                    fruit.transform.localScale = Vector3.one * 0.11f;
                }
            }
            var bale = MeshObject("Bale", t, kit.Bale, kit.Straw, kit.Twine);
            bale.transform.localPosition = new Vector3(1.6f, 1.31f, -0.8f);
            bale.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);

            // teslim alanı (yuvarlak boyalı plaka) ve fıçılar
            var pad = MeshObject("Pad", t, StylizedKit.RoundSlab(), kit.WoodDark, kit.Delivery);
            pad.transform.localPosition = new Vector3(0f, 0.005f, -2.5f);
            pad.transform.localScale = new Vector3(1.8f, 0.05f, 1.8f);
            foreach (var x in new[] { -2.1f, 2.1f })
                Barrel(t, new Vector3(x, 0f, -1.9f), 0.34f, 0.85f);
            foreach (var x in new[] { -1.4f, 1.4f })
            {
                var flowers = MeshObject("Flowers", t, kit.Flowers[rng.Next(kit.Flowers.Length)], kit.Bush, x < 0 ? kit.FlowerYellow : kit.FlowerPink);
                flowers.transform.localPosition = new Vector3(x, 0.15f, -1.35f);
                flowers.transform.localScale = Vector3.one * 1.2f;
            }

            // Tezgâhın herhangi bir yerine bakınca "Sat" ipucu çıksın.
            var targetArea = stall.AddComponent<BoxCollider>();
            targetArea.isTrigger = true;
            targetArea.center = new Vector3(0f, 1.4f, -0.2f);
            targetArea.size = new Vector3(4.2f, 2.8f, 2.6f);

            var intake = new GameObject("IntakePoint");
            intake.transform.SetParent(t, false);
            intake.transform.localPosition = new Vector3(0f, 1.3f, -0.75f);

            var station = stall.AddComponent<SellStation>();
            station.EditorSetup(sign.transform, intake.transform);

            foreach (var tr in stall.GetComponentsInChildren<Transform>(true))
                if (!tr.IsChildOf(sign.transform))
                    GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        /// <summary>Ana menü dioraması için "SATILIK" tabelası (oyunun konusu: arazi al, düzelt, sat).</summary>
        public static void BuildForSaleSign(Transform parent, Vector3 p, float yaw)
        {
            var sign = new GameObject("ForSaleSign");
            sign.transform.SetParent(parent, false);
            sign.transform.localPosition = Ground(p);
            sign.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var t = sign.transform;
            var b = kit.Box;

            GameObject Part(string n, Vector3 pos, Vector3 size, Vector3 euler, params Material[] mats)
            {
                var go = MeshObject(n, t, b, mats);
                go.transform.localPosition = pos;
                go.transform.localScale = size;
                go.transform.localRotation = Quaternion.Euler(euler);
                return go;
            }

            Part("Post", new Vector3(0f, 0.8f, 0f), new Vector3(0.11f, 1.6f, 0.11f), new Vector3(0f, 0f, 2f), kit.WoodDark);
            Part("Arm", new Vector3(0.38f, 1.52f, 0f), new Vector3(0.86f, 0.08f, 0.08f), Vector3.zero, kit.WoodDark);
            var board = new GameObject("Board");
            board.transform.SetParent(t, false);
            board.transform.localPosition = new Vector3(0.45f, 1.2f, 0f);
            board.transform.localRotation = Quaternion.Euler(0f, 0f, -3f);
            var bp = MeshObject("Panel", board.transform, b, kit.AwningRed);
            bp.transform.localScale = new Vector3(0.82f, 0.46f, 0.05f);
            var trim = MeshObject("Trim", board.transform, b, kit.AwningCream);
            trim.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            trim.transform.localScale = new Vector3(0.9f, 0.54f, 0.04f);
            Label(board.transform, "SATILIK", new Vector3(0f, 0f, -0.035f), 0.02f, new Color(1f, 0.97f, 0.9f));
            foreach (var x in new[] { 0.15f, 0.75f })
                Part("Chain", new Vector3(x, 1.4f, 0f), new Vector3(0.02f, 0.16f, 0.02f), Vector3.zero, kit.RockDark);
        }

        /// <summary>Kütük ve balya yığını (dekor).</summary>
        public static void BuildPropPile(Transform parent, Vector3 p, float yaw)
        {
            var pile = new GameObject("PropPile");
            pile.transform.SetParent(parent, false);
            pile.transform.localPosition = Ground(p);
            pile.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var t = pile.transform;

            for (int i = 0; i < 3; i++)
            {
                var log = MeshObject("Log", t, kit.Log, kit.Bark, kit.WoodCut);
                log.transform.localPosition = new Vector3(0f, 0.17f, -0.36f + i * 0.36f);
                log.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            for (int i = 0; i < 2; i++)
            {
                var log = MeshObject("Log", t, kit.Log, kit.Bark, kit.WoodCut);
                log.transform.localPosition = new Vector3(0.04f, 0.47f, -0.18f + i * 0.36f);
                log.transform.localRotation = Quaternion.Euler(0f, 87f + i * 6f, 0f);
            }
            for (int i = 0; i < 3; i++)
            {
                var bale = MeshObject("Bale", t, kit.Bale, kit.Straw, kit.Twine);
                bale.transform.localPosition = new Vector3(1.0f + (i == 2 ? 0.25f : 0f), 0.15f + (i == 2 ? 0.3f : 0f), -0.3f + (i == 2 ? 0.25f : i * 0.55f));
                bale.transform.localRotation = Quaternion.Euler(0f, 10f + i * 25f, 0f);
                bale.transform.localScale = Vector3.one * 1.6f;
            }
        }

        /// <summary>Alet tezgâhı: tahta platform, kalın çalışma masası, alet panosu, kiremit çatı, kütük üstünde örs.</summary>
        static void BuildWorkbench(Transform parent, Vector3 p)
        {
            var bench = new GameObject("Workbench");
            bench.transform.SetParent(parent, false);
            bench.transform.localPosition = Ground(p);
            bench.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); // ön yüz (-Z) arazinin ortasına bakar
            var t = bench.transform;

            // platform
            var platform = Group("Platform", t);
            {
                const int n = 8;
                float w = 2.7f / n;
                for (int i = 0; i < n; i++)
                    SB("Plank", platform, new Vector3(Range(-0.03f, 0.03f), 0.1f, -1.15f + w * (i + 0.5f)), new Vector3(3.6f, 0.08f, w - 0.015f),
                        new Vector3(0f, Range(-0.5f, 0.5f), 0f), i % 3 == 2 ? kit.Wood : kit.WoodDark, false, 0.4f);
                foreach (float z in new[] { -1.0f, 1.4f })
                    SB("Beam", platform, new Vector3(0f, 0.03f, z), new Vector3(3.7f, 0.09f, 0.14f), Vector3.zero, kit.WoodDark, false, 0.2f);
                GroupCollider(platform, new Vector3(0f, 0.07f, 0.2f), new Vector3(3.6f, 0.14f, 2.7f));
            }

            // çalışma masası
            var table = Group("Table", t);
            {
                for (int i = 0; i < 3; i++)
                    SB("Top", table, new Vector3(-0.3f, 0.98f, -0.17f + i * 0.32f), new Vector3(2.4f, 0.1f, 0.31f), Vector3.zero, i == 1 ? kit.WoodDark : kit.Wood, false, 0.35f);
                SB("Apron", table, new Vector3(-0.3f, 0.86f, -0.31f), new Vector3(2.3f, 0.14f, 0.05f), Vector3.zero, kit.WoodDark, false, 0.3f);
                for (int i = 0; i < 3; i++)
                    SB("Shelf", table, new Vector3(-0.3f, 0.38f, -0.06f + i * 0.26f), new Vector3(2.2f, 0.06f, 0.25f), Vector3.zero, kit.WoodDark, false, 0.4f);
                foreach (var x in new[] { -1.4f, 0.8f })
                foreach (var z in new[] { -0.25f, 0.55f })
                    SB("Leg", table, new Vector3(x, 0.52f, z), new Vector3(0.13f, 0.92f, 0.13f), Vector3.zero, kit.WoodDark, false, 0.3f);
                GroupCollider(table, new Vector3(-0.3f, 0.55f, 0.15f), new Vector3(2.45f, 1.05f, 0.95f));

                // masa üstü: demir mengene, kesilmiş tahta, testere
                SB("Vise", table, new Vector3(-1.2f, 1.13f, -0.1f), new Vector3(0.22f, 0.18f, 0.26f), Vector3.zero, Iron(), false, 0f);
                SB("ViseJaw", table, new Vector3(-1.2f, 1.25f, -0.24f), new Vector3(0.26f, 0.08f, 0.06f), Vector3.zero, Iron(), false, 0f);
                SB("ViseScrew", table, new Vector3(-1.2f, 1.18f, -0.36f), new Vector3(0.03f, 0.03f, 0.2f), Vector3.zero, Iron(), false, 0f);
                SB("Plank", table, new Vector3(-0.2f, 1.07f, 0.25f), new Vector3(0.9f, 0.05f, 0.22f), new Vector3(0f, 12f, 0f), kit.WoodCut, false, 0.6f);
                SB("SawBlade", table, new Vector3(0.45f, 1.045f, -0.05f), new Vector3(0.55f, 0.012f, 0.12f), new Vector3(0f, -20f, 0f), kit.Rock, false, 0f);
                SB("SawHandle", table, new Vector3(0.18f, 1.06f, 0.05f), new Vector3(0.14f, 0.04f, 0.1f), new Vector3(0f, -20f, 0f), kit.AwningRed, false, 0f);
            }

            // arka pano: dikey tahtalar, asılı aletler
            var peg = Group("PegWall", t);
            {
                SB("PegPostL", peg, new Vector3(-1.55f, 1.3f, 0.75f), new Vector3(0.14f, 2.6f, 0.14f), Vector3.zero, kit.WoodDark, true, 0.3f);
                SB("PegPostR", peg, new Vector3(0.95f, 1.3f, 0.75f), new Vector3(0.14f, 2.6f, 0.14f), Vector3.zero, kit.WoodDark, true, 0.3f);
                const int n = 8;
                float w = 2.36f / n;
                for (int i = 0; i < n; i++)
                    SB("Panel", peg, new Vector3(-1.48f + w * (i + 0.5f), 1.75f, 0.77f), new Vector3(w - 0.012f, 1.25f + Range(-0.04f, 0.04f), 0.05f),
                        Vector3.zero, i % 3 == 1 ? kit.Crate : kit.Sign, false, 0.4f);
                SB("Rail", peg, new Vector3(-0.3f, 2.2f, 0.72f), new Vector3(2.5f, 0.09f, 0.05f), Vector3.zero, kit.WoodDark, false, 0.3f);
                SB("Rail", peg, new Vector3(-0.3f, 1.25f, 0.72f), new Vector3(2.5f, 0.09f, 0.05f), Vector3.zero, kit.WoodDark, false, 0.3f);
                // balta
                SB("AxeHandle", peg, new Vector3(-1.05f, 1.72f, 0.7f), new Vector3(0.06f, 0.85f, 0.04f), new Vector3(0f, 0f, 8f), kit.Wood, false, 0.4f);
                SB("AxeHead", peg, new Vector3(-0.98f, 2.07f, 0.69f), new Vector3(0.28f, 0.16f, 0.04f), new Vector3(0f, 0f, 8f), Iron(), false, 0f);
                // çekiç
                SB("HammerHandle", peg, new Vector3(-0.35f, 1.67f, 0.7f), new Vector3(0.05f, 0.7f, 0.04f), Vector3.zero, kit.Wood, false, 0.4f);
                SB("HammerHead", peg, new Vector3(-0.35f, 2.0f, 0.69f), new Vector3(0.26f, 0.12f, 0.06f), Vector3.zero, Iron(), false, 0f);
                // makas
                SB("ShearBladeA", peg, new Vector3(0.38f, 1.85f, 0.7f), new Vector3(0.045f, 0.45f, 0.02f), new Vector3(0f, 0f, 14f), kit.Rock, false, 0f);
                SB("ShearBladeB", peg, new Vector3(0.48f, 1.85f, 0.7f), new Vector3(0.045f, 0.45f, 0.02f), new Vector3(0f, 0f, -14f), kit.Rock, false, 0f);
                SB("ShearHandles", peg, new Vector3(0.43f, 1.52f, 0.69f), new Vector3(0.22f, 0.12f, 0.05f), Vector3.zero, kit.AwningRed, false, 0f);
            }

            // kiremit çatı
            var roof = new GameObject("Roof").transform;
            roof.SetParent(t, false);
            roof.localPosition = new Vector3(-0.3f, 2.72f, 0.3f);
            roof.localRotation = Quaternion.Euler(-16f, 0f, 0f);
            SB("Panel", roof, Vector3.zero, new Vector3(2.9f, 0.1f, 2.4f), Vector3.zero, kit.Roof, false, 0.3f, 0.85f, "SRoofPanel");
            SB("Eave", roof, new Vector3(0f, -0.04f, -1.22f), new Vector3(3.0f, 0.12f, 0.12f), Vector3.zero, kit.WoodDark, false, 0.3f);
            SB("Ridge", roof, new Vector3(0f, -0.05f, 1.2f), new Vector3(3.0f, 0.14f, 0.14f), Vector3.zero, kit.WoodDark, false, 0.3f);
            SB("FrontPostL", t, new Vector3(-1.55f, 1.2f, -0.65f), new Vector3(0.14f, 2.4f, 0.14f), Vector3.zero, kit.WoodDark, true, 0.3f);
            SB("FrontPostR", t, new Vector3(0.95f, 1.2f, -0.65f), new Vector3(0.14f, 2.4f, 0.14f), Vector3.zero, kit.WoodDark, true, 0.3f);
            SB("Header", t, new Vector3(-0.3f, 2.36f, -0.65f), new Vector3(2.7f, 0.12f, 0.12f), Vector3.zero, kit.WoodDark, false, 0.3f);

            // tabela
            var sign = new GameObject("Sign");
            sign.transform.SetParent(t, false);
            sign.transform.localPosition = new Vector3(-0.3f, 2.66f, -0.95f);
            FramedBoard(sign.transform, new Vector2(1.9f, 0.5f), 0.08f, kit.Sign);
            Label(sign.transform, "ALETLER", new Vector3(0f, 0f, -0.055f), 0.026f, SignInk);

            // kütük üstünde örs (yükseltme olunca zıplar)
            var stump = MeshObject("AnvilStump", t, kit.Stumps[0], kit.Bark, kit.WoodCut);
            stump.transform.localPosition = new Vector3(1.55f, 0.14f, -0.35f);
            stump.transform.localScale = new Vector3(0.4f, 0.55f, 0.4f);
            var stumpCol = stump.AddComponent<CapsuleCollider>();
            stumpCol.center = new Vector3(0f, 0.5f, 0f);
            stumpCol.height = 1f;
            stumpCol.radius = 1f;
            var anvil = new GameObject("Anvil");
            anvil.transform.SetParent(t, false);
            anvil.transform.localPosition = new Vector3(1.55f, 0.69f, -0.35f);
            var a = anvil.transform;
            SB("AnvilBase", a, new Vector3(0f, 0.06f, 0f), new Vector3(0.26f, 0.12f, 0.32f), Vector3.zero, Iron(), false, 0f);
            SB("AnvilNeck", a, new Vector3(0f, 0.17f, 0f), new Vector3(0.14f, 0.12f, 0.18f), Vector3.zero, Iron(), false, 0f);
            SB("AnvilTop", a, new Vector3(0f, 0.27f, 0f), new Vector3(0.24f, 0.1f, 0.5f), Vector3.zero, Iron(), false, 0f);
            SB("AnvilHorn", a, new Vector3(0f, 0.275f, -0.32f), new Vector3(0.12f, 0.07f, 0.16f), new Vector3(10f, 0f, 0f), Iron(), false, 0f);

            // yanında odun yığını ve fıçı
            for (int i = 0; i < 3; i++)
            {
                var log = MeshObject("Log", t, kit.Log, kit.Bark, kit.WoodCut);
                log.transform.localPosition = new Vector3(-2.1f, 0.25f + (i == 2 ? 0.3f : 0f), -0.1f + (i == 2 ? 0.17f : i * 0.34f));
                log.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                log.transform.localScale = Vector3.one * 0.8f;
            }
            Barrel(t, new Vector3(1.4f, 0.14f, 0.9f), 0.3f, 0.75f);

            // Tezgâhın herhangi bir yerine bakınca ipucu çıksın.
            var targetArea = bench.AddComponent<BoxCollider>();
            targetArea.isTrigger = true;
            targetArea.center = new Vector3(-0.1f, 1.3f, 0f);
            targetArea.size = new Vector3(3.8f, 2.6f, 2.0f);

            bench.AddComponent<Workbench>().EditorSetup(anvil.transform);

            foreach (var tr in bench.GetComponentsInChildren<Transform>(true))
                if (!tr.IsChildOf(anvil.transform))
                    GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        /// <summary>Tabela yazıları: krem tahta üzerinde net okunsun diye neredeyse siyah.</summary>
        static readonly Color SignInk = new Color(0.07f, 0.055f, 0.045f);

        static void Label(Transform parent, string text, Vector3 localPos, float size, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            // Keskin SDF yazı (eski TextMesh soluk görünüyordu)
            var font = SignFont();
            if (font != null)
            {
                var tmp = go.AddComponent<TMPro.TextMeshPro>();
                tmp.font = font;
                tmp.text = text;
                tmp.fontSize = size * 96f * 0.95f;
                tmp.fontStyle = TMPro.FontStyles.Bold;
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                tmp.color = color;
                tmp.rectTransform.sizeDelta = new Vector2(20f, 10f);
                return;
            }

            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 96;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        }

        const string SignFontFolder = "Assets/_Project/Art/Fonts";
        const string SignFontTtf = SignFontFolder + "/Roboto-Bold.ttf";
        const string SignFontAssetPath = SignFontFolder + "/SignFont SDF.asset";
        const string SignFontSource = "Packages/com.unity.searcher/Editor/Resources/FlatSkin/Font/Roboto-Bold.ttf";
        static TMPro.TMP_FontAsset signFont;

        /// <summary>Tabela fontu: Roboto Bold (Apache 2.0) projeye kopyalanır, dinamik SDF font asset'i üretilir.</summary>
        static TMPro.TMP_FontAsset SignFont()
        {
            if (signFont != null) return signFont;
            signFont = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(SignFontAssetPath);
            if (signFont != null) return signFont;

            EnsureFolder(SignFontFolder);
            if (AssetDatabase.LoadAssetAtPath<Font>(SignFontTtf) == null)
            {
                string src = System.IO.Path.GetFullPath(SignFontSource);
                if (!System.IO.File.Exists(src)) return null;
                System.IO.File.Copy(src, System.IO.Path.GetFullPath(SignFontTtf), true);
                AssetDatabase.ImportAsset(SignFontTtf);
            }

            var ttf = AssetDatabase.LoadAssetAtPath<Font>(SignFontTtf);
            if (ttf == null) return null;
            var asset = TMPro.TMP_FontAsset.CreateFontAsset(ttf, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            if (asset == null) return null;
            asset.name = "SignFont SDF";
            AssetDatabase.CreateAsset(asset, SignFontAssetPath);
            if (asset.atlasTextures != null && asset.atlasTextures.Length > 0 && asset.atlasTextures[0] != null)
            {
                asset.atlasTextures[0].name = "SignFont Atlas";
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            }
            if (asset.material != null)
            {
                asset.material.name = "SignFont Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
            AssetDatabase.SaveAssets();
            signFont = asset;
            return signFont;
        }

        static void Decor(Transform parent, Mesh mesh, Material[] mats, Vector3 p, float scale)
        {
            var go = MeshObject(mesh.name, parent, mesh, mats);
            go.transform.localPosition = new Vector3(p.x, OuterHeightProbe(Ground(p)), p.z);
            go.transform.localRotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);
            go.transform.localScale = Vector3.one * scale;
        }

        // ================================================================ lighting

        public static void ApplyCozyLighting(Light sun)
        {
            if (sun != null)
            {
                sun.color = new Color(1f, 0.93f, 0.80f);
                sun.intensity = 1.4f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(45f, -38f, 0f);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.74f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.62f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.30f, 0.24f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.80f, 0.86f, 0.88f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 170f;
        }

        // ================================================================ helpers

        /// <summary>Arazi içindeyse arazi yüksekliği; dışarıdaysa dış zemini ışınla bulur.</summary>
        static Vector3 Ground(Vector3 p) => new Vector3(p.x, LowPolyMeshFactory.PlotHeight(p.x, p.z, seed), p.z);

        static float OuterHeightProbe(Vector3 p)
        {
            // Sadece zemin mesh'lerine bak (ağaç gövdesi/çit üstüne konmasın).
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, 200f, p.z), Vector3.down, 400f))
                if (hit.collider.name.EndsWith("Ground") && hit.point.y > best)
                    best = hit.point.y;
            return float.IsNegativeInfinity(best) ? p.y : best - 0.05f;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, params Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }

        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        static Mesh[] Meshes(string prefix, int count, System.Func<int, Mesh> factory, int seedBase)
        {
            var result = new Mesh[count];
            for (int i = 0; i < count; i++)
                result[i] = SaveMesh(factory(seedBase + i), $"{prefix}_{i}");
            return result;
        }

        public static Mesh SaveMesh(Mesh mesh, string name) => SaveMeshAt(mesh, $"{MeshFolder}/{name}.asset");

        /// <summary>Mesh'i verilen yola kaydeder; varsa üzerine yazar (referanslar bozulmaz).</summary>
        public static Mesh SaveMeshAt(Mesh mesh, string path)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            mesh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static Material Mat(string name, float r, float g, float b, float smoothness = 0.08f)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", new Color(r, g, b));
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SpecularHighlights", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void PlaceholderFolders()
        {
            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolder);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        /// <summary>Çakışmasız rastgele yerleşim. Büyük nesneler "block", küçükler "core" yarıçapına bakar.</summary>
        class Placer
        {
            readonly List<(Vector2 p, float block, float core)> occupied = new List<(Vector2, float, float)>();
            readonly System.Random random;
            readonly float halfSize;

            public Placer(System.Random random, float halfSize)
            {
                this.random = random;
                this.halfSize = halfSize;
            }

            public void Reserve(Vector3 center, float block, float core) =>
                occupied.Add((new Vector2(center.x, center.z), block, core));

            public Vector3 RandomPoint(float extent)
            {
                float x = ((float)random.NextDouble() * 2f - 1f) * extent;
                float z = ((float)random.NextDouble() * 2f - 1f) * extent;
                return new Vector3(x, 0f, z);
            }

            public bool TryPlace(float block, float core, float extent, out Vector3 point)
            {
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    point = RandomPoint(extent);
                    var p2 = new Vector2(point.x, point.z);
                    bool ok = true;
                    foreach (var o in occupied)
                        if (Vector2.Distance(p2, o.p) < Mathf.Max(block, o.block))
                        {
                            ok = false;
                            break;
                        }
                    if (!ok) continue;
                    occupied.Add((p2, block, core));
                    return true;
                }
                point = default;
                return false;
            }

            public bool TryPlaceSmall(float radius, float extent, out Vector3 point)
            {
                for (int attempt = 0; attempt < 25; attempt++)
                {
                    point = RandomPoint(extent);
                    var p2 = new Vector2(point.x, point.z);
                    bool ok = true;
                    foreach (var o in occupied)
                        if (Vector2.Distance(p2, o.p) < radius + o.core)
                        {
                            ok = false;
                            break;
                        }
                    if (!ok) continue;
                    occupied.Add((p2, radius, radius));
                    return true;
                }
                point = default;
                return false;
            }
        }
    }
}
