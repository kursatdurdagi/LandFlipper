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
            var ground = MeshObject("PlotGround", root.transform, SaveMesh(LowPolyMeshFactory.PlotTerrain(h, 2f, seed), $"PlotTerrain_{seed}"), k.Grass);
            ground.AddComponent<MeshCollider>();
            var outer = MeshObject("OuterGround", root.transform, SaveMesh(LowPolyMeshFactory.OuterTerrain(h, 320f, 7f, seed), $"OuterTerrain_{seed}"), k.OuterGrass);
            outer.AddComponent<MeshCollider>();
            Physics.SyncTransforms();

            // ---- rezerve alanlar
            placer.Reserve(s.ClearCenter, s.ClearRadius, 1.5f);
            Vector3 shackPos = new Vector3(-h * 0.5f, 0f, h * 0.45f);
            Vector3 stationPos = new Vector3(6.5f, 0f, -h + 4.5f);
            if (s.IncludeShack) placer.Reserve(shackPos, 4f, 3f);
            if (s.IncludeSellStation) placer.Reserve(stationPos, 5f, 3.5f);

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

        static void BuildShack(Transform parent, Vector3 p, int id, bool interactive)
        {
            var shack = new GameObject(interactive ? $"Shack_{id}" : "Shack (Ruin)");
            shack.transform.SetParent(parent, false);
            shack.transform.localPosition = Ground(p);
            shack.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
            var t = shack.transform;
            var parts = new List<Transform>();

            void Part(string n, Vector3 pos, Vector3 size, Vector3 euler, Material m)
            {
                var go = MeshObject(n, t, kit.Box, m);
                go.transform.localPosition = pos;
                go.transform.localScale = size;
                go.transform.localRotation = Quaternion.Euler(euler);
                go.AddComponent<BoxCollider>();
                parts.Add(go.transform);
            }

            // Sökülme sırası: yerdeki tahtalar → çatı → duvarlar → zemin
            Part("PlankA", new Vector3(2.8f, 0.12f, -0.9f), new Vector3(0.24f, 0.08f, 2.2f), new Vector3(0f, 25f, 4f), kit.WoodDark);
            Part("PlankB", new Vector3(2.5f, 0.18f, 0.3f), new Vector3(0.24f, 0.08f, 1.8f), new Vector3(0f, -15f, 8f), kit.Wood);
            Part("RoofR", new Vector3(1.2f, 2.35f, 0.4f), new Vector3(2.0f, 0.12f, 3.0f), new Vector3(0f, 0f, -34f), kit.Roof);
            Part("RoofL", new Vector3(-1.05f, 2.55f, 0f), new Vector3(2.4f, 0.12f, 4f), new Vector3(0f, 0f, 28f), kit.Roof);
            Part("WallRight", new Vector3(1.95f, 0.8f, 0.4f), new Vector3(0.12f, 1.4f, 2.5f), new Vector3(0f, 0f, 5f), kit.Wood);
            Part("WallFrontR", new Vector3(1.4f, 1.15f, -1.65f), new Vector3(1.2f, 2.1f, 0.12f), Vector3.zero, kit.Wood);
            Part("WallFrontL", new Vector3(-1.25f, 1.15f, -1.65f), new Vector3(1.5f, 2.1f, 0.12f), Vector3.zero, kit.Wood);
            Part("WallLeft", new Vector3(-1.95f, 1.15f, 0f), new Vector3(0.12f, 2.1f, 3.4f), Vector3.zero, kit.Wood);
            Part("WallBack", new Vector3(0f, 1.15f, 1.65f), new Vector3(4f, 2.1f, 0.12f), Vector3.zero, kit.Wood);
            Part("Floor", new Vector3(0f, 0.08f, 0f), new Vector3(4f, 0.16f, 3.4f), Vector3.zero, kit.WoodDark);

            if (interactive)
                shack.AddComponent<ShackNode>().EditorSetup(id, parts.ToArray(), 2);
        }

        static void BuildSellStation(Transform parent, Vector3 p, string priceText)
        {
            var stall = new GameObject("SellStation");
            stall.transform.SetParent(parent, false);
            stall.transform.localPosition = Ground(p);
            stall.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // ön yüz (-Z) girişe bakar
            var t = stall.transform;

            GameObject Part(string n, Transform parentT, Mesh mesh, Vector3 pos, Vector3 size, Vector3 euler, bool collide, params Material[] mats)
            {
                var go = MeshObject(n, parentT, mesh, mats);
                go.transform.localPosition = pos;
                go.transform.localScale = size;
                go.transform.localRotation = Quaternion.Euler(euler);
                if (collide) go.AddComponent<BoxCollider>();
                return go;
            }

            var b = kit.Box;
            Part("Platform", t, b, new Vector3(0f, 0.08f, 0.2f), new Vector3(3.8f, 0.16f, 3f), Vector3.zero, true, kit.WoodDark);
            Part("Counter", t, b, new Vector3(0f, 0.6f, -0.75f), new Vector3(3.2f, 0.95f, 0.6f), Vector3.zero, true, kit.Wood);
            Part("CounterTop", t, b, new Vector3(0f, 1.11f, -0.78f), new Vector3(3.45f, 0.09f, 0.78f), Vector3.zero, false, kit.WoodDark);
            Part("CounterStripe", t, b, new Vector3(0f, 0.6f, -1.06f), new Vector3(3.22f, 0.18f, 0.02f), Vector3.zero, false, kit.AwningRed);

            foreach (var x in new[] { -1.65f, 1.65f })
            foreach (var z in new[] { -1.05f, 1.35f })
                Part("Post", t, b, new Vector3(x, 1.35f, z), new Vector3(0.14f, 2.7f, 0.14f), Vector3.zero, true, kit.WoodDark);

            // çizgili tente
            var awning = new GameObject("Awning");
            awning.transform.SetParent(t, false);
            awning.transform.localPosition = new Vector3(0f, 2.82f, 0.15f);
            awning.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            const int stripes = 7;
            float width = 3.9f / stripes;
            for (int i = 0; i < stripes; i++)
            {
                var m = i % 2 == 0 ? kit.AwningRed : kit.AwningCream;
                Part("Stripe", awning.transform, b, new Vector3(-1.95f + width * (i + 0.5f), 0f, 0f), new Vector3(width, 0.06f, 3.0f), Vector3.zero, false, m);
                Part("Flap", awning.transform, b, new Vector3(-1.95f + width * (i + 0.5f), -0.12f, -1.5f), new Vector3(width, 0.25f, 0.04f), Vector3.zero, false, m);
            }

            // tabela (satış olunca zıplar)
            var sign = new GameObject("Sign");
            sign.transform.SetParent(t, false);
            sign.transform.localPosition = new Vector3(0f, 3.45f, -1.2f);
            Part("Board", sign.transform, b, Vector3.zero, new Vector3(2.2f, 0.6f, 0.08f), Vector3.zero, false, kit.Sign);
            Part("Frame", sign.transform, b, new Vector3(0f, 0f, 0.02f), new Vector3(2.32f, 0.72f, 0.06f), Vector3.zero, false, kit.WoodDark);
            Label(sign.transform, "SATIŞ", new Vector3(0f, 0f, -0.05f), 0.055f, new Color(0.33f, 0.2f, 0.12f));

            // fiyat panosu
            var board = new GameObject("PriceBoard");
            board.transform.SetParent(t, false);
            board.transform.localPosition = new Vector3(-2.45f, 0f, -0.9f);
            board.transform.localRotation = Quaternion.Euler(0f, -20f, 0f);
            Part("Leg", board.transform, b, new Vector3(0f, 0.7f, 0.05f), new Vector3(0.1f, 1.4f, 0.1f), Vector3.zero, true, kit.WoodDark);
            Part("Panel", board.transform, b, new Vector3(0f, 1.45f, 0f), new Vector3(1.15f, 1.0f, 0.06f), Vector3.zero, false, kit.Sign);
            Label(board.transform, priceText, new Vector3(0f, 1.45f, -0.04f), 0.0095f, new Color(0.25f, 0.18f, 0.12f));

            // kasalar ve ürünler
            for (int i = 0; i < 3; i++)
            {
                var crate = Part("Crate", t, b, new Vector3(-1.1f + i * 1.1f, 0.42f, 0.9f), new Vector3(0.75f, 0.5f, 0.55f), new Vector3(0f, Range(-6f, 6f), 0f), true, kit.Crate);
                var produce = i == 1 ? kit.ProduceOrange : kit.ProduceRed;
                for (int j = 0; j < 4; j++)
                {
                    var fruit = MeshObject("Produce", t, kit.Chunks[rng.Next(kit.Chunks.Length)], produce);
                    fruit.transform.localPosition = crate.transform.localPosition + new Vector3(Range(-0.25f, 0.25f), 0.3f, Range(-0.15f, 0.15f));
                    fruit.transform.localScale = Vector3.one * 0.11f;
                }
            }
            var bale = MeshObject("Bale", t, kit.Bale, kit.Straw, kit.Twine);
            bale.transform.localPosition = new Vector3(1.6f, 1.31f, -0.8f);
            bale.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);

            // teslim alanı ve fıçılar
            Part("Pad", t, kit.Disc, new Vector3(0f, 0.01f, -2.5f), new Vector3(1.8f, 0.035f, 1.8f), Vector3.zero, false, kit.Delivery);
            foreach (var x in new[] { -2.1f, 2.1f })
                Part("Barrel", t, kit.Disc, new Vector3(x, 0f, -1.9f), new Vector3(0.32f, 0.85f, 0.32f), Vector3.zero, true, kit.WoodDark);
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

            var station = stall.AddComponent<SellStation>();
            station.EditorSetup(sign.transform);

            foreach (var tr in stall.GetComponentsInChildren<Transform>(true))
                if (!tr.IsChildOf(sign.transform))
                    GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        static void Label(Transform parent, string text, Vector3 localPos, float size, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
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

        public static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = $"{MeshFolder}/{name}.asset";
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

        static Material Mat(string name, float r, float g, float b, float smoothness = 0.08f)
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
