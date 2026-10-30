using UnityEngine;

namespace LandFlipper.Gameplay.Effects
{
    /// <summary>
    /// Stilize partikül efektleri. Her efekt türü için TEK dünya-uzayı ParticleSystem vardır; efektler
    /// Emit ile o sisteme eklenir (havuz, Instantiate yok, birkaç çizim çağrısı). Tamamen yerel/görsel:
    /// ağ durumuna dokunmaz, her istemci kendi görür.
    /// </summary>
    public static class StylizedFx
    {
        static FxLibrary library;
        static bool loaded;
        static Transform root;
        static ParticleSystem chips, puffs, leaves, sparkles;

        // palet
        public static readonly Color WoodChip = new Color(0.86f, 0.66f, 0.42f);
        public static readonly Color BarkChip = new Color(0.52f, 0.36f, 0.24f);
        public static readonly Color RockChip = new Color(0.68f, 0.68f, 0.66f);
        public static readonly Color GrassChip = new Color(0.52f, 0.76f, 0.30f);
        public static readonly Color LeafGreen = new Color(0.42f, 0.70f, 0.30f);
        public static readonly Color Dust = new Color(0.86f, 0.80f, 0.68f, 0.55f);
        public static readonly Color Gold = new Color(1f, 0.82f, 0.32f);

        static bool Ready()
        {
            if (!loaded)
            {
                loaded = true;
                library = Resources.Load<FxLibrary>(FxLibrary.ResourcePath);
            }
            if (library == null) return false;
            if (root == null) Build();
            return root != null;
        }

        // ------------------------------------------------------------ olaylar

        /// <summary>Kaynağa her vuruşta (balta/çekiç/levye).</summary>
        public static void NodeHit(ResourceNode node)
        {
            if (node == null || !Ready()) return;
            Vector3 p = node.transform.position;
            switch (node.Kind)
            {
                case NodeKind.Tree:
                    p = Facing(p + Vector3.up * 1.0f, 0.22f);
                    Chips(p, WoodChip, BarkChip, 9, 2.6f, 0.07f);
                    Puff(p, new Color(0.80f, 0.68f, 0.52f, 0.35f), 2, 0.35f);
                    break;
                case NodeKind.Rock:
                    var rock = node as RockNode;
                    float r = rock != null ? rock.Radius : 0.7f;
                    p = Facing(p + Vector3.up * (r * 0.55f), r * 0.55f);
                    Chips(p, RockChip, new Color(0.5f, 0.5f, 0.52f), 8, 3.0f, 0.075f);
                    Puff(p, new Color(0.82f, 0.82f, 0.80f, 0.45f), 3, 0.45f);
                    Sparkle(p, new Color(1f, 0.9f, 0.7f), 3, 0.8f, 0.09f);
                    break;
                case NodeKind.Shack:
                    p = Facing(p + Vector3.up * 1.2f, 1.6f);
                    Chips(p, WoodChip, BarkChip, 7, 2.4f, 0.08f);
                    Puff(p, Dust, 3, 0.5f);
                    break;
                default:
                    p = p + Vector3.up * 0.3f;
                    Chips(p, GrassChip, LeafGreen, 6, 1.6f, 0.05f);
                    break;
            }
        }

        /// <summary>Kaynak tükendiğinde (çim biçildi, taş kırıldı, çalı söküldü).</summary>
        public static void NodeDepleted(ResourceNode node)
        {
            if (node == null || !Ready()) return;
            Vector3 p = node.transform.position;
            switch (node.Kind)
            {
                case NodeKind.Grass:
                    if (node is FlowerNode flower)
                    {
                        // taç yaprakları yaprak gibi süzülür, birkaç yeşil kırpıntı
                        Leaves(p + Vector3.up * 0.3f, 10, 0.2f, flower.PetalColor);
                        Chips(p + Vector3.up * 0.2f, GrassChip, LeafGreen, 6, 1.4f, 0.045f, 0.5f);
                    }
                    else Chips(p + Vector3.up * 0.25f, GrassChip, new Color(0.70f, 0.86f, 0.38f), 14, 1.8f, 0.055f, 0.6f);
                    break;
                case NodeKind.Bush:
                    Leaves(p + Vector3.up * 0.5f, 14, 0.6f, LeafGreen);
                    Puff(p + Vector3.up * 0.2f, new Color(0.62f, 0.78f, 0.45f, 0.35f), 4, 0.6f);
                    break;
                case NodeKind.Rock:
                    var rock = node as RockNode;
                    float r = rock != null ? rock.Radius : 0.7f;
                    Chips(p + Vector3.up * (r * 0.4f), RockChip, new Color(0.48f, 0.48f, 0.5f), 18, 3.5f, 0.1f);
                    Puff(p + Vector3.up * 0.2f, new Color(0.84f, 0.83f, 0.80f, 0.55f), 8, 0.9f * Mathf.Max(0.7f, r));
                    break;
            }
        }

        /// <summary>Ağaç devrilmeye başlarken taçtan yaprak dökülür.</summary>
        public static void TreeFallStart(Vector3 canopy, float radius)
        {
            if (!Ready()) return;
            Leaves(canopy, 22, radius * 0.8f, LeafGreen);
        }

        /// <summary>Gövde yere vurduğunda toz ve yaprak.</summary>
        public static void TreeLanded(Vector3 from, Vector3 to)
        {
            if (!Ready()) return;
            for (int i = 0; i < 5; i++)
                Puff(Vector3.Lerp(from, to, (i + 0.5f) / 5f) + Vector3.up * 0.15f, Dust, 2, 0.8f);
            Leaves(to + Vector3.up * 0.6f, 14, 1.0f, LeafGreen);
        }

        /// <summary>Kulübeden bir parça söküldü.</summary>
        public static void PartRemoved(Vector3 p)
        {
            if (!Ready()) return;
            Puff(p, Dust, 6, 0.9f);
            Chips(p, WoodChip, BarkChip, 8, 2f, 0.09f);
        }

        /// <summary>Satış: altın parıltı + birkaç "para" zerresi yukarı fışkırır.</summary>
        public static void Sold(Vector3 p)
        {
            if (!Ready()) return;
            Sparkle(p, Gold, 16, 1.6f, 0.24f);
            Sparkle(p, new Color(1f, 0.96f, 0.8f), 8, 1.0f, 0.15f);
            Puff(p, new Color(1f, 0.92f, 0.6f, 0.3f), 3, 0.5f);
        }

        /// <summary>Alet yükseltme: örsten parlak kıvılcım ve parıltı.</summary>
        public static void Upgraded(Vector3 p)
        {
            if (!Ready()) return;
            Sparkle(p, new Color(1f, 0.74f, 0.35f), 18, 2.0f, 0.18f);
            Sparkle(p, Color.white, 8, 1.1f, 0.24f);
            Puff(p, new Color(1f, 0.95f, 0.85f, 0.4f), 3, 0.5f);
        }

        // ------------------------------------------------------------ temel yayıcılar

        static Vector3 Facing(Vector3 p, float radius)
        {
            var cam = Camera.main;
            if (cam == null) return p;
            Vector3 d = cam.transform.position - p;
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? p + d.normalized * radius : p;
        }

        public static void Chips(Vector3 p, Color a, Color b, int count, float speed, float size, float spread = 1f)
        {
            if (!Ready()) return;
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.9f + 0.35f;
                e.position = p + Random.insideUnitSphere * 0.06f * spread;
                e.velocity = dir.normalized * speed * Random.Range(0.45f, 1f);
                e.startSize = size * Random.Range(0.6f, 1.35f);
                e.startColor = Color.Lerp(a, b, Random.value);
                e.startLifetime = Random.Range(0.55f, 0.95f);
                e.rotation = Random.Range(0f, 360f);
                e.angularVelocity = Random.Range(-540f, 540f);
                chips.Emit(e, 1);
            }
        }

        public static void Puff(Vector3 p, Color color, int count, float size)
        {
            if (!Ready()) return;
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                var d = Random.insideUnitSphere;
                d.y = Mathf.Abs(d.y) * 0.5f;
                e.position = p + d * size * 0.3f;
                e.velocity = d * size * 0.9f + Vector3.up * 0.25f;
                e.startSize = size * Random.Range(0.6f, 1.1f);
                e.startColor = color;
                e.startLifetime = Random.Range(0.7f, 1.1f);
                e.rotation = Random.Range(0f, 360f);
                e.angularVelocity = Random.Range(-40f, 40f);
                puffs.Emit(e, 1);
            }
        }

        public static void Leaves(Vector3 p, int count, float radius, Color tint)
        {
            if (!Ready()) return;
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                e.position = p + Random.insideUnitSphere * radius;
                var v = Random.insideUnitSphere * 1.2f;
                v.y = Mathf.Abs(v.y) * 0.8f + 0.2f;
                e.velocity = v;
                e.startSize = Random.Range(0.09f, 0.15f);
                float shade = Random.Range(0.8f, 1.15f);
                e.startColor = new Color(tint.r * shade, tint.g * shade, tint.b * shade * 0.9f, 1f);
                e.startLifetime = Random.Range(1.8f, 2.8f);
                e.rotation = Random.Range(0f, 360f);
                e.angularVelocity = Random.Range(-220f, 220f);
                leaves.Emit(e, 1);
            }
        }

        public static void Sparkle(Vector3 p, Color color, int count, float speed, float size)
        {
            if (!Ready()) return;
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                var v = Random.onUnitSphere;
                v.y = Mathf.Abs(v.y) + 0.6f;
                e.position = p + Random.insideUnitSphere * 0.15f;
                e.velocity = v.normalized * speed * Random.Range(0.4f, 1f);
                e.startSize = size * Random.Range(0.6f, 1.3f);
                e.startColor = color;
                e.startLifetime = Random.Range(0.5f, 0.9f);
                e.rotation = Random.Range(0f, 90f);
                e.angularVelocity = Random.Range(-90f, 90f);
                sparkles.Emit(e, 1);
            }
        }

        // ------------------------------------------------------------ kurulum

        static void Build()
        {
            var go = new GameObject("[Stylized FX]");
            Object.DontDestroyOnLoad(go);
            root = go.transform;

            chips = Create("Chips", library.chip, 400, gravity: 1.6f, drag: 0.4f,
                size: Curve(0f, 1f, 0.75f, 1f, 1f, 0.2f), alpha: Fade(0.75f));
            puffs = Create("Puffs", library.puff, 250, gravity: -0.05f, drag: 2.5f,
                size: Curve(0f, 0.45f, 0.35f, 0.95f, 1f, 1.15f), alpha: Fade(0.15f, 0.45f));
            leaves = Create("Leaves", library.leaf, 300, gravity: 0.12f, drag: 1.4f,
                size: Curve(0f, 0.6f, 0.15f, 1f, 1f, 0.8f), alpha: Fade(0.8f));
            var noise = leaves.noise;   // yaprak gibi süzülme
            noise.enabled = true;
            noise.strength = 1.1f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.4f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            sparkles = Create("Sparkles", library.sparkle, 200, gravity: 0.35f, drag: 2f,
                size: Curve(0f, 0.2f, 0.2f, 1.2f, 1f, 0f), alpha: Fade(0.6f));
        }

        static ParticleSystem Create(string name, Material material, int max, float gravity, float drag,
            ParticleSystem.MinMaxCurve size, Gradient alpha)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true; // emission kapalı: yalnız Emit ile parçacık gelir, sistem hep canlı kalır
            main.playOnAwake = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = drag > 0f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = size;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = alpha;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = 0.5f;
            renderer.alignment = ParticleSystemRenderSpace.View;

            ps.Play();
            return ps;
        }

        static ParticleSystem.MinMaxCurve Curve(float t0, float v0, float t1, float v1, float t2, float v2) =>
            new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(t0, v0), new Keyframe(t1, v1), new Keyframe(t2, v2)));

        static Gradient Fade(float fadeStart, float fadeInEnd = 0f)
        {
            var g = new Gradient();
            var alphas = fadeInEnd > 0f
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd), new GradientAlphaKey(1f, fadeStart), new GradientAlphaKey(0f, 1f) }
                : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeStart), new GradientAlphaKey(0f, 1f) };
            if (fadeInEnd > 0f && fadeStart < fadeInEnd)
                alphas = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd * 0.5f), new GradientAlphaKey(0f, 1f) };
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas);
            return g;
        }
    }
}
