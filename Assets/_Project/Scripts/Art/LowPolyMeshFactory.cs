using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LandFlipper.Art
{
    /// <summary>
    /// Düz gölgelendirilmiş (faceted) low poly mesh üretici. Her üçgen kendi vertex'lerine
    /// sahiptir; bu yüzden normaller yüzey başına düz çıkar. Seed ile deterministiktir.
    /// </summary>
    public static class LowPolyMeshFactory
    {
        // ------------------------------------------------------------ builder

        public class MeshBuilder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<List<int>> submeshes = new List<List<int>>();

            /// <summary>Üçgeni, 'inside' noktasından dışarı bakacak şekilde ekler.</summary>
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, int submesh = 0)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0f) (b, c) = (c, b);
                Add(a, b, c, submesh);
            }

            /// <summary>Üçgeni verilen yöne bakacak şekilde ekler.</summary>
            public void TriFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, int submesh = 0)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(n, facing) < 0f) (b, c) = (c, b);
                Add(a, b, c, submesh);
            }

            public void DoubleSided(Vector3 a, Vector3 b, Vector3 c, int submesh = 0)
            {
                Add(a, b, c, submesh);
                Add(a, c, b, submesh);
            }

            void Add(Vector3 a, Vector3 b, Vector3 c, int submesh)
            {
                while (submeshes.Count <= submesh) submeshes.Add(new List<int>());
                int i = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                var list = submeshes[submesh];
                list.Add(i);
                list.Add(i + 1);
                list.Add(i + 2);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.subMeshCount = Mathf.Max(1, submeshes.Count);
                for (int i = 0; i < submeshes.Count; i++)
                    mesh.SetTriangles(submeshes[i], i);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }

        // ------------------------------------------------------------ primitives

        static readonly Vector3[] IcoVertices;
        static readonly int[] IcoFaces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        static LowPolyMeshFactory()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            IcoVertices = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < IcoVertices.Length; i++) IcoVertices[i] = IcoVertices[i].normalized;
        }

        /// <summary>Pürüzlü (jitter) ikosfer. Paylaşılan köşeler birlikte oynar; yüzey kapalı kalır.</summary>
        public static void Icosphere(MeshBuilder mb, Vector3 center, Vector3 radii, int subdivisions, float jitter,
            System.Random rng, int submesh = 0)
        {
            var tris = new List<(Vector3, Vector3, Vector3)>();
            for (int i = 0; i < IcoFaces.Length; i += 3)
                tris.Add((IcoVertices[IcoFaces[i]], IcoVertices[IcoFaces[i + 1]], IcoVertices[IcoFaces[i + 2]]));

            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<(Vector3, Vector3, Vector3)>(tris.Count * 4);
                foreach (var (a, b, c) in tris)
                {
                    Vector3 ab = ((a + b) * 0.5f).normalized;
                    Vector3 bc = ((b + c) * 0.5f).normalized;
                    Vector3 ca = ((c + a) * 0.5f).normalized;
                    next.Add((a, ab, ca));
                    next.Add((b, bc, ab));
                    next.Add((c, ca, bc));
                    next.Add((ab, bc, ca));
                }
                tris = next;
            }

            var offsets = new Dictionary<Vector3Int, float>();
            Vector3 Displace(Vector3 p)
            {
                var key = Vector3Int.RoundToInt(p * 1000f);
                if (!offsets.TryGetValue(key, out float k))
                {
                    k = 1f + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                    offsets[key] = k;
                }
                return center + Vector3.Scale(p * k, radii);
            }

            foreach (var (a, b, c) in tris)
                mb.Tri(Displace(a), Displace(b), Displace(c), center, submesh);
        }

        /// <summary>
        /// Kesik koni / prizma. alongZ=false ise Y ekseninde (taban y=0), true ise Z ekseninde ortalanmış.
        /// r1=0 verilirse koni olur.
        /// </summary>
        public static void Prism(MeshBuilder mb, Vector3 origin, int sides, float r0, float r1, float height,
            bool alongZ, float jitter, System.Random rng, int sideSubmesh = 0, int capSubmesh = 0, float startAngle = 0f)
        {
            var bottom = new Vector3[sides];
            var top = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = startAngle + i * Mathf.PI * 2f / sides;
                float jb = 1f + ((float)(rng?.NextDouble() ?? 0.5) * 2f - 1f) * jitter;
                float jt = 1f + ((float)(rng?.NextDouble() ?? 0.5) * 2f - 1f) * jitter;
                float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                if (alongZ)
                {
                    bottom[i] = origin + new Vector3(cx * r0 * jb, cy * r0 * jb, -height * 0.5f);
                    top[i] = origin + new Vector3(cx * r1 * jt, cy * r1 * jt, height * 0.5f);
                }
                else
                {
                    bottom[i] = origin + new Vector3(cx * r0 * jb, 0f, cy * r0 * jb);
                    top[i] = origin + new Vector3(cx * r1 * jt, height, cy * r1 * jt);
                }
            }

            Vector3 axisBottom = alongZ ? origin + Vector3.back * height * 0.5f : origin;
            Vector3 axisTop = alongZ ? origin + Vector3.forward * height * 0.5f : origin + Vector3.up * height;
            Vector3 axisMid = (axisBottom + axisTop) * 0.5f;

            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                mb.Tri(bottom[i], top[i], top[j], axisMid, sideSubmesh);
                mb.Tri(bottom[i], top[j], bottom[j], axisMid, sideSubmesh);
            }

            Vector3 axis = (axisTop - axisBottom).normalized;
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                mb.TriFacing(axisBottom, bottom[i], bottom[j], -axis, capSubmesh);
                if (r1 > 1e-4f) mb.TriFacing(axisTop, top[i], top[j], axis, capSubmesh);
            }
        }

        public static void Box(MeshBuilder mb, Vector3 center, Vector3 size, int submesh = 0)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + new Vector3(x * h.x, y * h.y, z * h.z);
            var c = new[]
            {
                P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1),
                P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1)
            };
            int[,] faces = { { 0, 1, 2, 3 }, { 5, 4, 7, 6 }, { 4, 0, 3, 7 }, { 1, 5, 6, 2 }, { 3, 2, 6, 7 }, { 4, 5, 1, 0 } };
            for (int f = 0; f < 6; f++)
            {
                mb.Tri(c[faces[f, 0]], c[faces[f, 1]], c[faces[f, 2]], center, submesh);
                mb.Tri(c[faces[f, 0]], c[faces[f, 2]], c[faces[f, 3]], center, submesh);
            }
        }

        // ------------------------------------------------------------ assets

        public static Mesh RoundCanopy(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new MeshBuilder();
            Icosphere(mb, Vector3.zero, new Vector3(1f, 0.85f, 1f), 1, 0.16f, rng);
            Icosphere(mb, new Vector3(0.55f, -0.25f, 0.2f), Vector3.one * 0.6f, 1, 0.15f, rng);
            Icosphere(mb, new Vector3(-0.45f, -0.2f, -0.35f), Vector3.one * 0.55f, 1, 0.15f, rng);
            return mb.ToMesh($"Canopy_Round_{seed}");
        }

        public static Mesh PineCanopy(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new MeshBuilder();
            float y = 0f;
            float r = 1f;
            for (int tier = 0; tier < 3; tier++)
            {
                float h = 1.1f - tier * 0.15f;
                Prism(mb, new Vector3(0f, y, 0f), 7, r, 0f, h, false, 0.1f, rng, 0, 0, (float)rng.NextDouble());
                y += h * 0.55f;
                r *= 0.72f;
            }
            return mb.ToMesh($"Canopy_Pine_{seed}");
        }

        /// <summary>Birim yükseklikte gövde (Y ekseni). Sahnede yüksekliğe göre ölçeklenir.</summary>
        public static Mesh Trunk(int seed)
        {
            var mb = new MeshBuilder();
            Prism(mb, Vector3.zero, 6, 1f, 0.72f, 1f, false, 0.08f, new System.Random(seed));
            return mb.ToMesh($"Trunk_{seed}");
        }

        public static Mesh Stump(int seed)
        {
            var mb = new MeshBuilder();
            Prism(mb, Vector3.zero, 6, 1f, 0.9f, 1f, false, 0.08f, new System.Random(seed), 0, 1);
            return mb.ToMesh($"Stump_{seed}");
        }

        /// <summary>Kütük: Z ekseninde, submesh 0 = kabuk, 1 = kesik yüzey.</summary>
        public static Mesh Log(int seed, float length, float radius)
        {
            var mb = new MeshBuilder();
            Prism(mb, Vector3.zero, 7, radius, radius * 0.94f, length, true, 0.06f, new System.Random(seed), 0, 1);
            return mb.ToMesh($"Log_{seed}");
        }

        public static Mesh Rock(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new MeshBuilder();
            Icosphere(mb, Vector3.zero, new Vector3(1f, 0.7f, 0.9f), 1, 0.22f, rng);
            return mb.ToMesh($"Rock_{seed}");
        }

        public static Mesh RockChunk(int seed)
        {
            var mb = new MeshBuilder();
            Icosphere(mb, Vector3.zero, new Vector3(1f, 0.8f, 0.9f), 0, 0.25f, new System.Random(seed));
            return mb.ToMesh($"RockChunk_{seed}");
        }

        public static Mesh Bush(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new MeshBuilder();
            Icosphere(mb, new Vector3(0f, 0.35f, 0f), new Vector3(0.6f, 0.45f, 0.6f), 1, 0.18f, rng);
            Icosphere(mb, new Vector3(0.4f, 0.25f, 0.15f), Vector3.one * 0.35f, 0, 0.15f, rng);
            Icosphere(mb, new Vector3(-0.35f, 0.22f, -0.2f), Vector3.one * 0.32f, 0, 0.15f, rng);
            return mb.ToMesh($"Bush_{seed}");
        }

        /// <summary>Çiçek kümesi: submesh 0 = yapraklar, 1 = çiçekler.</summary>
        public static Mesh FlowerCluster(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new MeshBuilder();
            Icosphere(mb, new Vector3(0f, 0.08f, 0f), new Vector3(0.3f, 0.12f, 0.3f), 0, 0.2f, rng, 0);
            for (int i = 0; i < 5; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = 0.08f + (float)rng.NextDouble() * 0.2f;
                var p = new Vector3(Mathf.Cos(a) * d, 0.18f + (float)rng.NextDouble() * 0.12f, Mathf.Sin(a) * d);
                Icosphere(mb, p, Vector3.one * 0.06f, 0, 0.2f, rng, 1);
            }
            return mb.ToMesh($"Flowers_{seed}");
        }

        /// <summary>Çim öbeği: çift yüzlü üçgen yapraklar.</summary>
        public static Mesh GrassPatch(int seed, int blades = 14, float radius = 0.38f)
        {
            var rng = new System.Random(seed);
            var mb = new MeshBuilder();
            for (int i = 0; i < blades; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = Mathf.Sqrt((float)rng.NextDouble()) * radius;
                var basePos = new Vector3(Mathf.Cos(a) * d, -0.02f, Mathf.Sin(a) * d);
                float facing = (float)rng.NextDouble() * Mathf.PI * 2f;
                var side = new Vector3(Mathf.Cos(facing), 0f, Mathf.Sin(facing));
                float width = 0.07f + (float)rng.NextDouble() * 0.05f;
                float height = 0.32f + (float)rng.NextDouble() * 0.35f;
                var lean = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.2f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.2f);
                mb.DoubleSided(basePos - side * width * 0.5f, basePos + Vector3.up * height + lean, basePos + side * width * 0.5f);
            }
            return mb.ToMesh($"GrassPatch_{seed}");
        }

        /// <summary>Çim balyası: submesh 0 = saman, 1 = ip.</summary>
        public static Mesh Bale()
        {
            var mb = new MeshBuilder();
            Box(mb, Vector3.zero, new Vector3(0.52f, 0.3f, 0.34f), 0);
            Box(mb, new Vector3(-0.12f, 0f, 0f), new Vector3(0.035f, 0.31f, 0.35f), 1);
            Box(mb, new Vector3(0.12f, 0f, 0f), new Vector3(0.035f, 0.31f, 0.35f), 1);
            return mb.ToMesh("Bale");
        }

        public static Mesh FencePost()
        {
            var mb = new MeshBuilder();
            Prism(mb, Vector3.zero, 4, 0.1f, 0.1f, 1.1f, false, 0f, null, 0, 0, Mathf.PI * 0.25f);
            Prism(mb, new Vector3(0f, 1.1f, 0f), 4, 0.1f, 0f, 0.14f, false, 0f, null, 0, 0, Mathf.PI * 0.25f);
            return mb.ToMesh("FencePost");
        }

        public static Mesh Disc(int sides, string name)
        {
            var mb = new MeshBuilder();
            Prism(mb, Vector3.zero, sides, 1f, 0.96f, 1f, false, 0f, null);
            return mb.ToMesh(name);
        }

        public static Mesh UnitBox(string name)
        {
            var mb = new MeshBuilder();
            Box(mb, Vector3.zero, Vector3.one);
            return mb.ToMesh(name);
        }

        // ------------------------------------------------------------ terrain

        public static float PlotHeight(float x, float z, int seed)
        {
            float o = seed % 997;
            return (Mathf.PerlinNoise((x + o) * 0.13f, (z + o) * 0.13f) - 0.5f) * 0.18f;
        }

        public static Mesh PlotTerrain(float halfSize, float cell, int seed)
        {
            var mb = new MeshBuilder();
            int n = Mathf.CeilToInt(halfSize * 2f / cell);
            float step = halfSize * 2f / n;
            Vector3 V(int i, int j)
            {
                float x = -halfSize + i * step, z = -halfSize + j * step;
                return new Vector3(x, PlotHeight(x, z, seed), z);
            }
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                AddQuad(mb, V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1), (i + j) % 2 == 0);
            return mb.ToMesh("PlotTerrain");
        }

        public static Mesh OuterTerrain(float plotHalfSize, float size, float cell, int seed)
        {
            var mb = new MeshBuilder();
            int n = Mathf.CeilToInt(size / cell);
            float half = size * 0.5f;
            float step = size / n;
            float o = seed % 991;

            float H(float x, float z)
            {
                float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - plotHalfSize;
                if (d < 0.5f) return -0.25f;
                float near = Mathf.SmoothStep(0f, 1f, d / 6f);
                float far = Mathf.SmoothStep(0f, 1f, (d - 15f) / 60f);
                float hills = Mathf.PerlinNoise((x + o) * 0.025f, (z + o) * 0.025f);
                float bumps = Mathf.PerlinNoise((x + o) * 0.18f, (z + o) * 0.18f) - 0.5f;
                return -0.05f + bumps * 0.5f * near + far * (3f + hills * 14f);
            }

            Vector3 V(int i, int j)
            {
                float x = -half + i * step, z = -half + j * step;
                return new Vector3(x, H(x, z), z);
            }
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                AddQuad(mb, V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1), (i + j) % 2 == 0);
            return mb.ToMesh("OuterTerrain");
        }

        static void AddQuad(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool flip)
        {
            if (flip)
            {
                mb.TriFacing(a, b, c, Vector3.up);
                mb.TriFacing(a, c, d, Vector3.up);
            }
            else
            {
                mb.TriFacing(a, b, d, Vector3.up);
                mb.TriFacing(b, c, d, Vector3.up);
            }
        }
    }
}
