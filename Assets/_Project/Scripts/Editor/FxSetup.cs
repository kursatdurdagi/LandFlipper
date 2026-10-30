using System;
using System.IO;
using LandFlipper.Gameplay.Effects;
using UnityEditor;
using UnityEngine;

namespace LandFlipper.EditorTools
{
    /// <summary>
    /// Stilize partikül dokuları (prosedürel, küçük), URP partikül malzemeleri ve Resources'taki FxLibrary.
    /// Tekrar çalıştırmak güvenlidir.
    /// </summary>
    public static class FxSetup
    {
        const string TextureFolder = "Assets/_Project/Art/Textures/Fx";
        const string MaterialFolder = "Assets/_Project/Art/Materials/Fx";
        const string ResourcesFolder = "Assets/_Project/Resources";
        const string LibraryPath = ResourcesFolder + "/" + FxLibrary.ResourcePath + ".asset";
        const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";
        const int Size = 128;

        [MenuItem("Land Flipper/Setup/Create FX Library")]
        public static FxLibrary CreateLibrary()
        {
            LowPolyWorldBuilder.EnsureFolder(TextureFolder);
            LowPolyWorldBuilder.EnsureFolder(MaterialFolder);
            LowPolyWorldBuilder.EnsureFolder(ResourcesFolder);

            var lib = AssetDatabase.LoadAssetAtPath<FxLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<FxLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            lib.puff = MakeMaterial("M_FxPuff", Paint("T_FxPuff", PuffPattern), false);
            lib.chip = MakeMaterial("M_FxChip", Paint("T_FxChip", ChipPattern), false);
            lib.leaf = MakeMaterial("M_FxLeaf", Paint("T_FxLeaf", LeafPattern), false);
            lib.sparkle = MakeMaterial("M_FxSparkle", Paint("T_FxSparkle", SparklePattern), true);
            lib.mote = MakeMaterial("M_FxMote", Paint("T_FxMote", MotePattern), true);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        /// <summary>Sahneye ortam zerrelerini ekler (yoksa).</summary>
        public static void AddAmbient()
        {
            if (UnityEngine.Object.FindFirstObjectByType<AmbientFx>() != null) return;
            new GameObject("AmbientFx").AddComponent<AmbientFx>();
        }

        // ------------------------------------------------------------ malzeme

        static Material MakeMaterial(string name, Texture2D tex, bool additive)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var shader = Shader.Find(ParticleShader);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);                         // saydam
            m.SetFloat("_Blend", additive ? 2f : 0f);           // 0 alfa, 2 additive
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------ dokular (beyaz + alfa; renk partikülden)

        static Texture2D Paint(string name, Func<float, float, Color> pattern)
        {
            string path = $"{TextureFolder}/{name}.png";
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // 4x süper örnekleme: kenarlar pürüzsüz
                Color c = Color.clear;
                for (int s = 0; s < 4; s++)
                {
                    float u = (x + 0.25f + (s & 1) * 0.5f) / Size * 2f - 1f;
                    float v = (y + 0.25f + (s >> 1) * 0.5f) / Size * 2f - 1f;
                    c += pattern(u, v);
                }
                px[y * Size + x] = c * 0.25f;
            }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Edge(float d, float width) => Mathf.Clamp01(d / width);

        /// <summary>Tombul bulut: üst-sol açık, alt-sağ gölgeli iki ton (çizgi film duman).</summary>
        static Color PuffPattern(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float lump = 0.82f + 0.08f * Mathf.Sin(Mathf.Atan2(v, u) * 5f);
            float a = Edge(lump - r, 0.06f);
            float lit = Mathf.Clamp01(0.5f + (v * 0.6f - u * 0.4f));
            float tone = Mathf.Lerp(0.82f, 1f, Mathf.SmoothStep(0.35f, 0.55f, lit));
            return new Color(tone, tone, tone, a);
        }

        /// <summary>Yuvarlatılmış köşeli düzensiz yonga, üst kenarda açık şerit.</summary>
        static Color ChipPattern(float u, float v)
        {
            float x = Mathf.Abs(u) - 0.55f, y = Mathf.Abs(v + u * 0.25f) - 0.35f;
            float d = Mathf.Sqrt(Mathf.Max(x, 0f) * Mathf.Max(x, 0f) + Mathf.Max(y, 0f) * Mathf.Max(y, 0f)) + Mathf.Min(Mathf.Max(x, y), 0f) - 0.2f;
            float a = Edge(-d, 0.05f);
            float tone = v > 0.25f ? 1f : 0.86f;
            return new Color(tone, tone, tone, a);
        }

        /// <summary>Damla yaprak + orta damar.</summary>
        static Color LeafPattern(float u, float v)
        {
            // yaprak ekseni çapraz
            float a0 = 0.7853f;
            float x = u * Mathf.Cos(a0) - v * Mathf.Sin(a0);
            float y = u * Mathf.Sin(a0) + v * Mathf.Cos(a0);
            float t = (y + 0.9f) / 1.8f;                         // 0 sap, 1 uç
            float width = 0.42f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * (1f - t * 0.25f);
            float a = (t < 0f || t > 1f) ? 0f : Edge(width - Mathf.Abs(x), 0.05f);
            float vein = 1f - 0.25f * Edge(0.03f - Mathf.Abs(x), 0.02f);
            float side = x > 0f ? 1f : 0.85f;
            float tone = vein * side;
            return new Color(tone, tone, tone, a);
        }

        /// <summary>4 kollu yumuşak yıldız + merkez parıltı.</summary>
        static Color SparklePattern(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float star = Mathf.Max(Edge(0.12f - Mathf.Abs(u) * (0.3f + 3f * Mathf.Abs(v)), 0.08f) * Edge(1f - Mathf.Abs(v), 0.2f),
                Edge(0.12f - Mathf.Abs(v) * (0.3f + 3f * Mathf.Abs(u)), 0.08f) * Edge(1f - Mathf.Abs(u), 0.2f));
            float core = Mathf.Exp(-r * r * 18f);
            float a = Mathf.Clamp01(star + core);
            return new Color(1f, 1f, 1f, a);
        }

        /// <summary>Yumuşak nokta.</summary>
        static Color MotePattern(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01(Mathf.Exp(-r * r * 6f) - 0.02f);
            return new Color(1f, 1f, 1f, a);
        }
    }
}
