using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LandFlipper.EditorTools
{
    /// <summary>
    /// Stilize çimen ve çiçek modelleri + "LandFlipper/StylizedFoliage" malzemeleri.
    /// Yapraklar kavisli, uca doğru incelir; vertex color alpha = kökten uca oran (renk geçişi ve rüzgâr),
    /// RGB = ton çeşitliliği. Çiçeklerin rengi mesh'e işlenir; hepsi tek malzemeyle tek çizimde çizilir.
    /// </summary>
    public static class StylizedFoliage
    {
        public const string ShaderName = "LandFlipper/StylizedFoliage";

        public static readonly Color[] PetalColors =
        {
            new Color(0.98f, 0.60f, 0.72f), // pembe
            new Color(1.00f, 0.86f, 0.32f), // sarı
            new Color(0.98f, 0.97f, 0.93f), // beyaz
            new Color(0.66f, 0.58f, 0.96f), // lila
        };

        public static void Apply(LowPolyWorldBuilder.Kit k)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[LandFlipper] {ShaderName} bulunamadı; çimen URP/Lit ile kalır.");
                k.Flower = k.FlowerPink;
                return;
            }

            // biçilebilir çimen öbeği: canlı, uçları güneşte sararan
            Setup(k.GrassBlade, shader, new Color(0.22f, 0.44f, 0.17f), new Color(0.72f, 0.88f, 0.36f), 0.10f, 0.9f);
            // çiçek: renk tamamen vertex color'dan
            k.Flower = LowPolyWorldBuilder.Mat("M_Flower", 1f, 1f, 1f);
            Setup(k.Flower, shader, Color.white, Color.white, 0.06f, 1f);
            k.Flower.SetFloat("_Translucency", 0.2f);

            k.GrassPatches = Many("SGrassTuft", 4, s => Tuft(s, 16, 0.36f, 0.40f, 0.72f, 0.065f, 0.10f));
            var flowers = new List<Mesh>();
            for (int c = 0; c < PetalColors.Length; c++)
                for (int v = 0; v < 2; v++)
                    flowers.Add(LowPolyWorldBuilder.SaveMesh(FlowerPlant(2000 + c * 31 + v * 7, PetalColors[c]), $"SFlower_{c}_{v}"));
            k.Flowers = flowers.ToArray();
        }

        /// <summary>Renge göre çiçek mesh'i (0 pembe, 1 sarı, 2 beyaz, 3 lila).</summary>
        public static Mesh FlowerOf(LowPolyWorldBuilder.Kit k, int color, int variant) =>
            k.Flowers[((color % PetalColors.Length) * 2 + (variant & 1)) % k.Flowers.Length];

        static void Setup(Material m, Shader shader, Color root, Color tip, float wind, float ambient)
        {
            if (m == null) return;
            m.shader = shader;
            m.SetColor("_BaseColor", root);
            m.SetColor("_TipColor", tip);
            m.SetFloat("_WindStrength", wind);
            m.SetFloat("_AmbientBoost", ambient);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        static Mesh[] Many(string prefix, int count, System.Func<int, Mesh> factory)
        {
            var result = new Mesh[count];
            for (int i = 0; i < count; i++)
                result[i] = LowPolyWorldBuilder.SaveMesh(factory(3000 + i * 13), $"{prefix}_{i}");
            return result;
        }

        // ------------------------------------------------------------ mesh üretimi

        class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();

            public int Add(Vector3 p, Vector3 n, Color c)
            {
                V.Add(p); N.Add(n); C.Add(c);
                return V.Count - 1;
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetColors(C);
                m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>
        /// Kavisli, uca doğru incelen yaprak. Kök orijinde (base), 'facing' yaprağın düz yüz yönü.
        /// alpha: kökte 0, uçta 1.
        /// </summary>
        static void Blade(Builder b, Vector3 basePos, Vector3 side, Vector3 lean, float height, float width, Color tint,
            int segments = 4, float alphaScale = 1f)
        {
            Vector3 up = Vector3.up;
            Vector3 normal = Vector3.Cross(side, up + lean).normalized;
            int prevL = -1, prevR = -1;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                // kavis: eğim uca doğru artar (yay gibi)
                Vector3 spine = basePos + up * (height * t) + lean * (height * t * t);
                spine.y -= lean.magnitude * height * t * t * 0.35f;
                float w = width * (1f - Mathf.Pow(t, 1.4f)) * (s == 0 ? 1.15f : 1f);
                var c = new Color(tint.r, tint.g, tint.b, t * alphaScale);
                if (s == segments)
                {
                    int tip = b.Add(spine, normal, c);
                    b.T.AddRange(new[] { prevL, tip, prevR });
                    break;
                }
                int l = b.Add(spine - side * (w * 0.5f), normal, c);
                int r = b.Add(spine + side * (w * 0.5f), normal, c);
                if (prevL >= 0)
                {
                    b.T.AddRange(new[] { prevL, l, r });
                    b.T.AddRange(new[] { prevL, r, prevR });
                }
                prevL = l; prevR = r;
            }
        }

        /// <summary>Çimen öbeği: ortada uzun, kenarda kısa, dışa doğru açılan yapraklar.</summary>
        public static Mesh Tuft(int seed, int blades, float radius, float minH, float maxH, float minW, float maxW, int segments = 4)
        {
            var rng = new System.Random(seed);
            float Rnd(float a, float b2) => a + (float)rng.NextDouble() * (b2 - a);
            var b = new Builder();
            for (int i = 0; i < blades; i++)
            {
                float a = Rnd(0f, Mathf.PI * 2f);
                float d = Mathf.Sqrt((float)rng.NextDouble()) * radius;
                var basePos = new Vector3(Mathf.Cos(a) * d, -0.03f, Mathf.Sin(a) * d);
                float centre = 1f - d / Mathf.Max(0.001f, radius);
                float height = Mathf.Lerp(minH, maxH, Mathf.Clamp01(centre * 0.6f + (float)rng.NextDouble() * 0.6f));
                var outward = new Vector3(basePos.x, 0f, basePos.z);
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var lean = (outward * Rnd(0.15f, 0.45f) + new Vector3(Rnd(-0.15f, 0.15f), 0f, Rnd(-0.15f, 0.15f)));
                float facing = Mathf.Atan2(outward.z, outward.x) + Mathf.PI * 0.5f + Rnd(-0.5f, 0.5f);
                var side = new Vector3(Mathf.Cos(facing), 0f, Mathf.Sin(facing));
                // ton: sarıya ya da maviye hafif kayma, parlaklık farkı
                float hue = Rnd(-1f, 1f), lum = Rnd(0.86f, 1.1f);
                var tint = new Color(lum * (1f + hue * 0.08f), lum, lum * (1f - hue * 0.1f));
                Blade(b, basePos, side, lean, height, Rnd(minW, maxW), tint, segments);
            }
            return b.ToMesh($"Tuft_{seed}");
        }

        /// <summary>Çiçek kümesi: geniş taban yaprakları, ince saplar, 5 yapraklı çanak çiçekler.</summary>
        public static Mesh FlowerPlant(int seed, Color petal)
        {
            var rng = new System.Random(seed);
            float Rnd(float a, float b2) => a + (float)rng.NextDouble() * (b2 - a);
            var b = new Builder();
            var stemDark = new Color(0.24f, 0.46f, 0.20f);
            var stemLight = new Color(0.50f, 0.72f, 0.30f);

            // taban yaprakları (geniş, kısa)
            int leaves = 5 + rng.Next(3);
            for (int i = 0; i < leaves; i++)
            {
                float a = i / (float)leaves * Mathf.PI * 2f + Rnd(-0.3f, 0.3f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = new Vector3(-dir.z, 0f, dir.x);
                Blade(b, dir * 0.03f + Vector3.down * 0.01f, side, dir * Rnd(0.35f, 0.6f), Rnd(0.16f, 0.26f), Rnd(0.08f, 0.11f),
                    Color.Lerp(stemDark, stemLight, Rnd(0.3f, 0.7f)), 3, 0.5f);
            }

            var center = petal.r > 0.95f && petal.g > 0.8f && petal.b < 0.5f
                ? new Color(0.95f, 0.55f, 0.20f)    // sarı çiçekte turuncu göbek
                : new Color(1.00f, 0.80f, 0.28f);
            int heads = 3 + rng.Next(3);
            for (int i = 0; i < heads; i++)
            {
                float a = Rnd(0f, Mathf.PI * 2f);
                float d = Rnd(0.02f, 0.16f);
                var basePos = new Vector3(Mathf.Cos(a) * d, -0.02f, Mathf.Sin(a) * d);
                float h = Rnd(0.22f, 0.4f);
                var lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Rnd(0.05f, 0.25f);
                // sap: iki çapraz ince yaprak
                for (int k = 0; k < 2; k++)
                {
                    float f = a + k * Mathf.PI * 0.5f;
                    Blade(b, basePos, new Vector3(Mathf.Cos(f), 0f, Mathf.Sin(f)), lean, h + 0.02f, 0.038f,
                        Color.Lerp(stemDark, stemLight, 0.35f), 3);
                }
                // sapın ucu (Blade'deki kavisle aynı formül)
                var top = basePos + Vector3.up * h + lean * h;
                top.y -= lean.magnitude * h * 0.35f;
                float size = Rnd(0.075f, 0.11f);
                var tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up + lean * 0.8f).normalized);
                Head(b, top, tilt, size, petal * Rnd(0.92f, 1.05f), center, Rnd(0f, 360f));
            }
            return b.ToMesh($"Flower_{seed}");
        }

        /// <summary>5 yuvarlak taç yaprak (hafif çanak) + kubbe göbek. alpha = 1 (sapın ucuyla birlikte salınır).</summary>
        static void Head(Builder b, Vector3 pos, Quaternion tilt, float size, Color petal, Color center, float spin)
        {
            const int petals = 5;
            const int arc = 5;
            Vector3 up = tilt * Vector3.up;
            for (int p = 0; p < petals; p++)
            {
                float a0 = (spin + p * 360f / petals) * Mathf.Deg2Rad;
                // yaprağın kökü göbekte, ucu yuvarlak
                var dir = tilt * new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var side = tilt * new Vector3(-Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var cPetal = new Color(petal.r, petal.g, petal.b, 1f);
                var cInner = new Color(petal.r * 0.9f, petal.g * 0.86f, petal.b * 0.88f, 1f);
                int root = b.Add(pos + up * 0.004f, up, cInner);
                int prev = -1;
                for (int s = 0; s <= arc; s++)
                {
                    float u = s / (float)arc;              // 0..1 bir kenardan diğerine
                    float ang = (u - 0.5f) * Mathf.PI;     // yarım daire uç
                    float reach = size * (0.55f + 0.45f * Mathf.Cos(ang));
                    float half = size * 0.42f * Mathf.Sin(ang);
                    var pt = pos + dir * reach + side * half + up * (size * 0.28f * (reach / size)); // çanak
                    int idx = b.Add(pt, (up + dir * 0.25f).normalized, cPetal);
                    if (prev >= 0) b.T.AddRange(new[] { root, idx, prev });
                    prev = idx;
                }
            }
            // göbek: küçük kubbe
            const int ring = 6;
            int top = b.Add(pos + up * (size * 0.32f), up, new Color(center.r, center.g, center.b, 1f));
            int first = -1, last = -1;
            for (int i = 0; i < ring; i++)
            {
                float a = i / (float)ring * Mathf.PI * 2f;
                var off = tilt * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (size * 0.3f);
                int idx = b.Add(pos + off + up * (size * 0.12f), (up + off.normalized * 0.5f).normalized,
                    new Color(center.r * 0.85f, center.g * 0.8f, center.b * 0.8f, 1f));
                if (last >= 0) b.T.AddRange(new[] { top, idx, last });
                else first = idx;
                last = idx;
            }
            b.T.AddRange(new[] { top, first, last });
        }
    }
}
