using UnityEditor;
using UnityEngine;

namespace LandFlipper.EditorTools
{
    /// <summary>
    /// Primitive'lerden "dummy" arazi kurar. Phase 2'deki seed tabanlı gerçek arazi
    /// üretimi gelene kadar FPS kontrolcüyü ve atmosferi test etmek için kullanılır.
    /// Sabit seed kullanır; aynı seed her seferinde aynı düzeni üretir.
    /// </summary>
    public static class PlaceholderWorldBuilder
    {
        public const string MaterialsFolder = "Assets/_Project/Art/Materials/Placeholder";

        public class Palette
        {
            public Material Grass, OuterGrass, Dirt, Bark, Leaves, LeavesDark, TallGrass, Rock,
                Wood, WoodDark, Roof, Debris, Delivery, Sign;
        }

        public struct PlotSettings
        {
            public int Seed;
            public float HalfSize;
            public float Density;
            public Vector3 ClearCenter;
            public float ClearRadius;
            public bool IncludeShack;
            public bool IncludeDelivery;
            public int OuterTreeCount;
        }

        static System.Random rng;

        public static Palette LoadPalette()
        {
            return new Palette
            {
                Grass = Mat("M_Grass", new Color(0.52f, 0.62f, 0.32f), 0.1f),
                OuterGrass = Mat("M_GrassOuter", new Color(0.42f, 0.56f, 0.30f), 0.1f),
                Dirt = Mat("M_Dirt", new Color(0.50f, 0.39f, 0.27f), 0.05f),
                Bark = Mat("M_Bark", new Color(0.40f, 0.28f, 0.18f), 0.1f),
                Leaves = Mat("M_Leaves", new Color(0.36f, 0.58f, 0.27f), 0.15f),
                LeavesDark = Mat("M_LeavesDark", new Color(0.26f, 0.46f, 0.24f), 0.15f),
                TallGrass = Mat("M_TallGrass", new Color(0.63f, 0.70f, 0.33f), 0.1f),
                Rock = Mat("M_Rock", new Color(0.56f, 0.55f, 0.52f), 0.2f),
                Wood = Mat("M_Wood", new Color(0.70f, 0.53f, 0.34f), 0.15f),
                WoodDark = Mat("M_WoodDark", new Color(0.45f, 0.33f, 0.22f), 0.1f),
                Roof = Mat("M_Roof", new Color(0.55f, 0.27f, 0.20f), 0.2f),
                Debris = Mat("M_Debris", new Color(0.47f, 0.45f, 0.41f), 0.2f),
                Delivery = Mat("M_Delivery", new Color(0.95f, 0.75f, 0.25f), 0.3f),
                Sign = Mat("M_Sign", new Color(0.95f, 0.92f, 0.82f), 0.2f)
            };
        }

        public static GameObject BuildPlot(string name, PlotSettings s, Palette p)
        {
            rng = new System.Random(s.Seed);
            var root = new GameObject(name);
            float h = s.HalfSize;

            // Zemin: arazi + dışarıdaki geniş çayır
            var ground = Prim(PrimitiveType.Plane, "PlotGround", root.transform, Vector3.zero,
                new Vector3(h * 0.2f, 1f, h * 0.2f), Quaternion.identity, p.Grass, true);
            ground.isStatic = true;
            Prim(PrimitiveType.Plane, "OuterGround", root.transform, new Vector3(0f, -0.02f, 0f),
                new Vector3(30f, 1f, 30f), Quaternion.identity, p.OuterGrass, true);

            var dirt = Group("DirtPatches", root);
            for (int i = 0; i < Count(8, s); i++)
            {
                float r = Range(1.5f, 3.5f);
                Prim(PrimitiveType.Cylinder, "Dirt", dirt, RandomPoint(h - 2f, s, 0.003f + i * 0.0005f),
                    new Vector3(r, 0.005f, r * Range(0.6f, 1f)), Quaternion.Euler(0, Range(0, 360), 0), p.Dirt, false);
            }

            BuildFence(root, h, p);

            var trees = Group("Trees", root);
            for (int i = 0; i < Count(22, s); i++)
                Tree(trees, RandomPoint(h - 1.5f, s, 0f), Range(0.8f, 1.25f), p, true);

            var outer = Group("OuterTrees", root);
            for (int i = 0; i < s.OuterTreeCount; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                float d = Range(h + 5f, h + 45f);
                Tree(outer, new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), Range(1.2f, 2f), p, false);
            }

            var rocks = Group("Rocks", root);
            for (int i = 0; i < Count(18, s); i++)
            {
                var scale = new Vector3(Range(0.6f, 1.5f), Range(0.4f, 0.9f), Range(0.6f, 1.5f));
                var pos = RandomPoint(h - 1f, s, scale.y * 0.3f);
                Prim(PrimitiveType.Sphere, "Rock", rocks, pos, scale,
                    Quaternion.Euler(Range(-10, 10), Range(0, 360), Range(-10, 10)), p.Rock, true);
            }

            var grass = Group("TallGrass", root);
            for (int i = 0; i < Count(70, s); i++)
            {
                var center = RandomPoint(h - 0.8f, s, 0f);
                for (int b = 0; b < 4; b++)
                {
                    float height = Range(0.35f, 0.75f);
                    var offset = new Vector3(Range(-0.25f, 0.25f), height * 0.5f, Range(-0.25f, 0.25f));
                    Prim(PrimitiveType.Cube, "Blade", grass, center + offset, new Vector3(0.05f, height, 0.05f),
                        Quaternion.Euler(Range(-15, 15), Range(0, 360), Range(-15, 15)), p.TallGrass, false);
                }
            }

            var debris = Group("Debris", root);
            for (int i = 0; i < Count(14, s); i++)
            {
                bool plank = rng.NextDouble() < 0.5;
                var scale = plank
                    ? new Vector3(Range(1.2f, 2.2f), 0.06f, 0.2f)
                    : Vector3.one * Range(0.25f, 0.6f);
                Prim(PrimitiveType.Cube, plank ? "Plank" : "Junk", debris, RandomPoint(h - 1f, s, scale.y * 0.5f),
                    scale, Quaternion.Euler(Range(-8, 8), Range(0, 360), Range(-8, 8)), plank ? p.WoodDark : p.Debris, true);
            }

            if (s.IncludeShack) Shack(root, new Vector3(h * 0.45f, 0f, h * 0.45f), p);
            if (s.IncludeDelivery) DeliveryZone(root, new Vector3(h - 4f, 0f, -h + 4f), p);

            foreach (var t in root.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);

            return root;
        }

        public static void ApplyCozyLighting(Light sun)
        {
            if (sun != null)
            {
                sun.color = new Color(1f, 0.93f, 0.80f);
                sun.intensity = 1.35f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.84f, 0.86f);
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 140f;
        }

        // ------------------------------------------------------------ parçalar

        static void BuildFence(GameObject root, float h, Palette p)
        {
            var fence = Group("Fence", root);
            const float spacing = 2f;
            const float gate = 2.5f; // güney tarafındaki giriş boşluğunun yarısı

            for (float x = -h; x <= h + 0.01f; x += spacing)
            {
                Post(fence, new Vector3(x, 0f, h), p);
                if (Mathf.Abs(x) >= gate) Post(fence, new Vector3(x, 0f, -h), p);
            }
            for (float z = -h + spacing; z <= h - spacing + 0.01f; z += spacing)
            {
                Post(fence, new Vector3(-h, 0f, z), p);
                Post(fence, new Vector3(h, 0f, z), p);
            }

            foreach (float y in new[] { 0.45f, 0.95f })
            {
                Rail(fence, new Vector3(0f, y, h), new Vector3(h * 2f, 0.12f, 0.06f), p);
                Rail(fence, new Vector3(-h, y, 0f), new Vector3(0.06f, 0.12f, h * 2f), p);
                Rail(fence, new Vector3(h, y, 0f), new Vector3(0.06f, 0.12f, h * 2f), p);
                float half = (h - gate) * 0.5f;
                Rail(fence, new Vector3(-gate - half, y, -h), new Vector3(h - gate, 0.12f, 0.06f), p);
                Rail(fence, new Vector3(gate + half, y, -h), new Vector3(h - gate, 0.12f, 0.06f), p);
            }
        }

        static void Post(Transform parent, Vector3 pos, Palette p) =>
            Prim(PrimitiveType.Cube, "Post", parent, pos + Vector3.up * 0.6f, new Vector3(0.15f, 1.2f, 0.15f),
                Quaternion.Euler(0, Range(-4, 4), Range(-2, 2)), p.WoodDark, true);

        static void Rail(Transform parent, Vector3 pos, Vector3 scale, Palette p) =>
            Prim(PrimitiveType.Cube, "Rail", parent, pos, scale, Quaternion.identity, p.Wood, true);

        static void Tree(Transform parent, Vector3 pos, float size, Palette p, bool collider)
        {
            var tree = new GameObject("Tree");
            tree.transform.SetParent(parent, false);
            tree.transform.localPosition = pos;
            tree.transform.localRotation = Quaternion.Euler(0f, Range(0, 360), 0f);

            float trunkHeight = Range(2f, 3.2f) * size;
            float trunkRadius = Range(0.35f, 0.5f) * size;
            Prim(PrimitiveType.Cylinder, "Trunk", tree.transform, new Vector3(0f, trunkHeight * 0.5f, 0f),
                new Vector3(trunkRadius, trunkHeight * 0.5f, trunkRadius), Quaternion.identity, p.Bark, collider, true);

            float canopy = Range(2.2f, 3.2f) * size;
            Prim(PrimitiveType.Sphere, "Canopy", tree.transform, new Vector3(0f, trunkHeight + canopy * 0.3f, 0f),
                new Vector3(canopy, canopy * 0.85f, canopy), Quaternion.identity, p.Leaves, false, true);
            Prim(PrimitiveType.Sphere, "Canopy2", tree.transform,
                new Vector3(canopy * 0.3f, trunkHeight + canopy * 0.05f, canopy * 0.2f),
                Vector3.one * canopy * 0.65f, Quaternion.identity, p.LeavesDark, false, true);
        }

        static void Shack(GameObject root, Vector3 pos, Palette p)
        {
            var shack = new GameObject("Shack (Ruin)");
            shack.transform.SetParent(root.transform, false);
            shack.transform.localPosition = pos;
            shack.transform.localRotation = Quaternion.Euler(0f, -20f, 0f);
            var t = shack.transform;

            Prim(PrimitiveType.Cube, "Floor", t, new Vector3(0f, 0.05f, 0f), new Vector3(4f, 0.1f, 3.4f), Quaternion.identity, p.WoodDark, true, true);
            Prim(PrimitiveType.Cube, "WallBack", t, new Vector3(0f, 1.2f, 1.65f), new Vector3(4f, 2.3f, 0.12f), Quaternion.identity, p.Wood, true, true);
            Prim(PrimitiveType.Cube, "WallLeft", t, new Vector3(-1.95f, 1.2f, 0f), new Vector3(0.12f, 2.3f, 3.4f), Quaternion.identity, p.Wood, true, true);
            Prim(PrimitiveType.Cube, "WallRight", t, new Vector3(1.95f, 0.8f, 0.3f), new Vector3(0.12f, 1.5f, 2.6f), Quaternion.Euler(0, 0, 4f), p.Wood, true, true);
            Prim(PrimitiveType.Cube, "WallFrontL", t, new Vector3(-1.25f, 1.2f, -1.65f), new Vector3(1.5f, 2.3f, 0.12f), Quaternion.identity, p.Wood, true, true);
            Prim(PrimitiveType.Cube, "WallFrontR", t, new Vector3(1.35f, 1.2f, -1.65f), new Vector3(1.3f, 2.3f, 0.12f), Quaternion.identity, p.Wood, true, true);
            Prim(PrimitiveType.Cube, "Roof", t, new Vector3(-0.3f, 2.45f, 0f), new Vector3(3.6f, 0.12f, 4f), Quaternion.Euler(0, 0, 12f), p.Roof, true, true);
            Prim(PrimitiveType.Cube, "FallenPlank", t, new Vector3(2.7f, 0.15f, -0.8f), new Vector3(0.25f, 0.08f, 2.2f), Quaternion.Euler(0, 25f, 6f), p.WoodDark, true, true);
        }

        static void DeliveryZone(GameObject root, Vector3 pos, Palette p)
        {
            var zone = new GameObject("DeliveryZone (Placeholder)");
            zone.transform.SetParent(root.transform, false);
            zone.transform.localPosition = pos;
            var t = zone.transform;

            Prim(PrimitiveType.Cylinder, "Pad", t, new Vector3(0f, 0.02f, 0f), new Vector3(3f, 0.02f, 3f), Quaternion.identity, p.Delivery, false, true);
            Prim(PrimitiveType.Cube, "SignPost", t, new Vector3(1.8f, 0.9f, 0f), new Vector3(0.12f, 1.8f, 0.12f), Quaternion.identity, p.WoodDark, true, true);
            Prim(PrimitiveType.Cube, "SignBoard", t, new Vector3(1.8f, 1.65f, 0f), new Vector3(0.9f, 0.5f, 0.06f), Quaternion.identity, p.Sign, true, true);
        }

        // ------------------------------------------------------------ yardımcılar

        static Transform Group(string name, GameObject root)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go.transform;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale,
            Quaternion rotation, Material mat, bool keepCollider, bool local = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Prim(PrimitiveType type, string name, GameObject parent, Vector3 pos, Vector3 scale,
            Quaternion rotation, Material mat, bool keepCollider) =>
            Prim(type, name, parent.transform, pos, scale, rotation, mat, keepCollider);

        static Vector3 RandomPoint(float extent, PlotSettings s, float y)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                var point = new Vector3(Range(-extent, extent), y, Range(-extent, extent));
                var flat = new Vector3(point.x - s.ClearCenter.x, 0f, point.z - s.ClearCenter.z);
                if (flat.magnitude > s.ClearRadius) return point;
            }
            return new Vector3(extent, y, extent);
        }

        static int Count(int baseCount, PlotSettings s) => Mathf.Max(1, Mathf.RoundToInt(baseCount * s.Density));

        static float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        static Material Mat(string name, Color color, float smoothness)
        {
            EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
