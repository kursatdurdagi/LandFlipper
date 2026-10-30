using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;

namespace LandFlipper.EditorTools
{
    /// <summary>
    /// Stilize çevre kiti: ProBuilder primitiflerinden (icosahedron, silindir, koni) üretilen,
    /// yumuşak gölgelemeli (smoothing group), organik şişkin modeller + el boyaması gibi
    /// prosedürel dokular. UV'ler modele göre açılır (kutu / silindir izdüşümü), dokular döşenebilir.
    /// Modeller mesh asset olarak kaydedilir; oyun sistemleri (ağaç devrilmesi, taş kırılması) aynı kalır.
    /// </summary>
    public static class StylizedKit
    {
        public const string TextureFolder = "Assets/_Project/Art/Textures/Stylized";
        const int TexSize = 512;

        // ================================================================ kit uygulama

        /// <summary>Low poly kitin ağaç/taş/çalı/çit/kütük modellerini ve malzemelerini stilize sürümle değiştirir.</summary>
        public static void Apply(LowPolyWorldBuilder.Kit k)
        {
            LowPolyWorldBuilder.EnsureFolder(TextureFolder);

            // --- dokular
            var leaves = Paint("T_Leaves", (u, v) => LeafPattern(u, v, new Color(0.36f, 0.66f, 0.30f), new Color(0.56f, 0.80f, 0.36f), 11));
            var leavesLight = Paint("T_LeavesLight", (u, v) => LeafPattern(u, v, new Color(0.52f, 0.74f, 0.30f), new Color(0.74f, 0.88f, 0.42f), 23));
            var pine = Paint("T_Pine", (u, v) => LeafPattern(u, v, new Color(0.18f, 0.45f, 0.36f), new Color(0.32f, 0.62f, 0.46f), 37));
            var bush = Paint("T_Bush", (u, v) => LeafPattern(u, v, new Color(0.28f, 0.56f, 0.30f), new Color(0.46f, 0.72f, 0.36f), 51));
            var bark = Paint("T_Bark", BarkPattern);
            var rock = Paint("T_Rock", RockPattern);
            var woodCut = Paint("T_WoodCut", RingsPattern);
            var wood = Paint("T_Wood", (u, v) => WoodPattern(u, v, new Color(0.80f, 0.60f, 0.40f)));
            var woodDark = Paint("T_WoodDark", (u, v) => WoodPattern(u, v, new Color(0.56f, 0.40f, 0.28f)));
            var ground = Paint("T_Ground", (u, v) => GroundPattern(u, v, new Color(0.47f, 0.70f, 0.34f), new Color(0.58f, 0.78f, 0.38f), true));
            var shingle = Paint("T_Shingle", ShinglePattern);
            var groundOuter = Paint("T_GroundOuter", (u, v) => GroundPattern(u, v, new Color(0.44f, 0.66f, 0.34f), new Color(0.52f, 0.72f, 0.36f), false));

            Textured(k.Leaves, leaves);
            Textured(k.LeavesLight, leavesLight);
            Textured(k.Pine, pine);
            Textured(k.Bush, bush);
            Textured(k.Bark, bark);
            Textured(k.Rock, rock, new Color(1f, 1f, 1f));
            Textured(k.RockDark, rock, new Color(0.82f, 0.82f, 0.86f));
            Textured(k.WoodCut, woodCut);
            Textured(k.Wood, wood);
            Textured(k.WoodDark, woodDark);
            Textured(k.Roof, shingle);
            Textured(k.Grass, ground);
            Textured(k.OuterGrass, groundOuter);

            // --- modeller (ProBuilder)
            k.RoundCanopies = Many("SCanopy_Round", 4, s => RoundCanopy(s));
            k.PineCanopies = Many("SCanopy_Pine", 3, s => PineCanopy(s));
            k.Trunks = Many("STrunk", 3, s => Trunk(s));
            k.Stumps = Many("SStump", 2, s => Stump(s));
            k.Rocks = Many("SRock", 4, s => Rock(s, 2, 0.16f, new Vector3(1f, 0.72f, 0.92f)));
            k.Chunks = Many("SRockChunk", 3, s => Rock(s + 70, 1, 0.12f, new Vector3(1f, 0.8f, 0.9f)));
            k.Bushes = Many("SBush", 3, s => Bush(s));
            k.Post = LowPolyWorldBuilder.SaveMesh(Post(), "SFencePost");
            k.Log = LowPolyWorldBuilder.SaveMesh(Log(1000, 1.1f, 0.17f), "SLog");
        }

        static Mesh[] Many(string prefix, int count, Func<int, Mesh> factory)
        {
            var result = new Mesh[count];
            for (int i = 0; i < count; i++)
                result[i] = LowPolyWorldBuilder.SaveMesh(factory(1000 + i * 17), $"{prefix}_{i}");
            return result;
        }

        static void Textured(Material m, Texture2D tex, Color? tint = null)
        {
            if (m == null) return;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint ?? Color.white);
            m.SetFloat("_Smoothness", 0.12f);
            EditorUtility.SetDirty(m);
        }

        // ================================================================ ProBuilder modeller

        /// <summary>ProBuilder modelini yumuşatıp (smoothing group) Unity mesh'ine çevirir ve geçici objeyi siler.</summary>
        static Mesh Bake(ProBuilderMesh pb, Func<Vector3, Vector3> deform, int hardSubmesh = -1)
        {
            if (deform != null)
            {
                var positions = new List<Vector3>(pb.positions);
                for (int i = 0; i < positions.Count; i++) positions[i] = deform(positions[i]);
                pb.positions = positions;
            }
            foreach (var f in pb.faces)
                f.smoothingGroup = f.submeshIndex == hardSubmesh ? 0 : 1;
            pb.ToMesh();
            pb.Refresh();
            var mesh = UnityEngine.Object.Instantiate(pb.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.DestroyImmediate(pb.gameObject);
            return mesh;
        }

        static ProBuilderMesh Sphere(float radius, int subdivisions) =>
            ShapeGenerator.GenerateIcosahedron(PivotLocation.Center, radius, subdivisions, true, false);

        /// <summary>Birkaç yumuşak "puf"tan oluşan yuvarlak ağaç tepesi (merkez orijinde, yarıçap ~1).</summary>
        static Mesh RoundCanopy(int seed)
        {
            var rng = new System.Random(seed);
            var parts = new List<(Mesh, Matrix4x4)>
            {
                (Puff(seed, 1f, 0.10f), Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, 0.82f, 1f))),
            };
            int extra = 3 + rng.Next(2);
            for (int i = 0; i < extra; i++)
            {
                float a = (float)(i / (double)extra * System.Math.PI * 2 + rng.NextDouble());
                float r = 0.5f + (float)rng.NextDouble() * 0.18f;
                var p = new Vector3(Mathf.Cos(a) * 0.62f, -0.22f + (float)rng.NextDouble() * 0.35f, Mathf.Sin(a) * 0.62f);
                parts.Add((Puff(seed + i * 7, 1f, 0.12f), Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * r)));
            }
            parts.Add((Puff(seed + 99, 1f, 0.1f), Matrix4x4.TRS(new Vector3(0.1f, 0.62f, -0.05f), Quaternion.identity, Vector3.one * 0.55f)));
            return Finish(Combine(parts), BoxUV, 1.2f, $"Canopy_{seed}");
        }

        static Mesh Puff(int seed, float radius, float lumps)
        {
            var pb = Sphere(radius, 2);
            float o = seed * 1.37f;
            return Bake(pb, p =>
            {
                Vector3 n = p.normalized;
                float d = 1f + (Noise3(n * 2.2f + Vector3.one * o) - 0.5f) * 2f * lumps;
                return n * radius * d;
            });
        }

        /// <summary>Üst üste yumuşak koni katmanlarından çam (taban orijinde).</summary>
        static Mesh PineCanopy(int seed)
        {
            var parts = new List<(Mesh, Matrix4x4)>();
            float y = 0f, r = 1f;
            for (int tier = 0; tier < 3; tier++)
            {
                float h = 1.15f - tier * 0.18f;
                var pb = ShapeGenerator.GenerateCone(PivotLocation.Center, 1f, 1f, 14);
                float tierSeed = seed + tier * 11;
                var mesh = Bake(pb, p =>
                {
                    // koniyi ortala, alt kenarı şişir ve dalgalandır (yaprak kümesi gibi)
                    var c = new Vector3(p.x, p.y + 0.5f, p.z);
                    float t = Mathf.Clamp01(c.y);
                    float ang = Mathf.Atan2(c.z, c.x);
                    float wave = 1f + Mathf.Sin(ang * 7f + tierSeed) * 0.06f * (1f - t);
                    float bulge = 1f + Mathf.Sin(t * Mathf.PI) * 0.18f;
                    return new Vector3(c.x * wave * bulge, c.y - (1f - t) * (1f - t) * 0.12f, c.z * wave * bulge);
                });
                parts.Add((mesh, Matrix4x4.TRS(new Vector3(0f, y, 0f), Quaternion.identity, new Vector3(r, h, r))));
                y += h * 0.55f;
                r *= 0.72f;
            }
            return Finish(Combine(parts), BoxUV, 1.1f, $"Pine_{seed}");
        }

        /// <summary>Birim gövde: yarıçap 1 → 0.72, yükseklik 1, taban orijinde, hafif eğri.</summary>
        static Mesh Trunk(int seed)
        {
            var pb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 12, 1f, 1f, 4, -1);
            float s = seed * 0.71f;
            var mesh = Bake(pb, p =>
            {
                var c = new Vector3(p.x, p.y + 0.5f, p.z);
                float t = Mathf.Clamp01(c.y);
                float taper = Mathf.Lerp(1.12f, 0.7f, Mathf.Pow(t, 0.8f));   // dipte hafif genişleme
                float bend = Mathf.Sin(t * 2.2f + s) * 0.18f;
                return new Vector3(c.x * taper + bend, c.y, c.z * taper);
            });
            return Finish(mesh, CylinderUV, 2.4f, $"Trunk_{seed}");
        }

        /// <summary>Kütük dibi: yan yüz kabuk (submesh 0), üst kesik yüz halkalar (submesh 1).</summary>
        static Mesh Stump(int seed)
        {
            var pb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 12, 1f, 1f, 1, -1);
            MarkCaps(pb, Vector3.up, 1);
            var mesh = Bake(pb, p =>
            {
                var c = new Vector3(p.x, p.y + 0.5f, p.z);
                float taper = Mathf.Lerp(1.08f, 0.94f, Mathf.Clamp01(c.y));
                return new Vector3(c.x * taper, c.y, c.z * taper);
            }, 1);
            return Finish(mesh, CylinderUV, 1f, $"Stump_{seed}");
        }

        /// <summary>Z ekseninde kütük; uçlar halkalı kesik yüz (submesh 1).</summary>
        static Mesh Log(int seed, float length, float radius)
        {
            var pb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 12, radius, length, 2, -1);
            MarkCaps(pb, Vector3.up, 1);
            MarkCaps(pb, Vector3.down, 1);
            var mesh = Bake(pb, p =>
            {
                float t = p.y / length;
                float bulge = 1f + (0.25f - t * t) * 0.12f;
                // Y ekseninden Z eksenine DÖNDÜR (eksen takası yansıma olur, üçgenler ters döner → arka yüz görünürdü)
                return new Vector3(p.x * bulge, -p.z * bulge, p.y);
            }, 1);
            return Finish(mesh, LogUV, 1f, $"Log_{seed}");
        }

        static Mesh Rock(int seed, int subdivisions, float lumps, Vector3 scale)
        {
            var pb = Sphere(1f, subdivisions);
            float o = seed * 2.13f;
            var mesh = Bake(pb, p =>
            {
                Vector3 n = p.normalized;
                float d = 1f + (Noise3(n * 1.6f + Vector3.one * o) - 0.5f) * 2f * lumps;
                Vector3 q = n * d;
                if (q.y < -0.25f) q.y = Mathf.Lerp(q.y, -0.25f, 0.6f);     // tabanı düzleştir
                return Vector3.Scale(q, scale);
            });
            return Finish(mesh, BoxUV, 1.4f, $"Rock_{seed}");
        }

        static Mesh Bush(int seed)
        {
            var rng = new System.Random(seed);
            var parts = new List<(Mesh, Matrix4x4)>
            {
                (Puff(seed, 1f, 0.1f), Matrix4x4.TRS(new Vector3(0f, 0.36f, 0f), Quaternion.identity, new Vector3(0.62f, 0.46f, 0.62f)))
            };
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + (float)rng.NextDouble();
                parts.Add((Puff(seed + i * 5, 1f, 0.12f), Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 0.4f, 0.24f, Mathf.Sin(a) * 0.4f),
                    Quaternion.identity, Vector3.one * (0.3f + (float)rng.NextDouble() * 0.08f))));
            }
            return Finish(Combine(parts), BoxUV, 1.8f, $"Bush_{seed}");
        }

        /// <summary>Yuvarlak tepeli çit direği (taban orijinde, ~1.24 m).</summary>
        static Mesh Post()
        {
            var body = Bake(ShapeGenerator.GenerateCylinder(PivotLocation.Center, 10, 0.1f, 1.12f, 1, -1), p => new Vector3(p.x, p.y + 0.56f, p.z));
            var cap = Bake(Sphere(0.1f, 1), p => new Vector3(p.x, Mathf.Max(p.y, -0.02f) * 0.9f, p.z));
            var mesh = Combine(new List<(Mesh, Matrix4x4)>
            {
                (body, Matrix4x4.identity),
                (cap, Matrix4x4.Translate(new Vector3(0f, 1.12f, 0f)))
            });
            return Finish(mesh, CylinderUV, 1f, "Post");
        }

        static readonly Dictionary<string, Mesh> boardCache = new Dictionary<string, Mesh>();

        /// <summary>
        /// Pahlı (bevel), hafif eğri, ucu yamuk kesilmiş tahta. Damar (doku u ekseni) en uzun eksen boyuncadır.
        /// Aynı boy + varyant mesh paylaşır ve asset olarak kaydedilir.
        /// </summary>
        public static Mesh Board(Vector3 size, int variant, float tiling, string prefix)
        {
            int cx = Mathf.RoundToInt(size.x * 100f), cy = Mathf.RoundToInt(size.y * 100f), cz = Mathf.RoundToInt(size.z * 100f);
            string key = $"{prefix}_{cx}x{cy}x{cz}_{variant}";
            if (boardCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            float minSide = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            bool beveled = false;
            try
            {
                var edges = new List<Edge>();
                foreach (var f in pb.faces) edges.AddRange(f.edges);
                var result = UnityEngine.ProBuilder.MeshOperations.Bevel.BevelEdges(pb, edges, Mathf.Min(0.02f, minSide * 0.28f));
                beveled = result != null && result.Count > 0;
            }
            catch (Exception e) { Debug.LogWarning($"[StylizedKit] Bevel başarısız ({key}): {e.Message}"); }

            int la = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;   // uzun eksen
            int ta = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;   // kalınlık
            if (ta == la) ta = (la + 1) % 3;
            int wa = 3 - la - ta;                                                            // genişlik
            var rng = new System.Random(variant * 977 + cx * 31 + cy * 17 + cz * 7);
            float bow = size[la] * 0.012f * ((float)rng.NextDouble() * 2f - 1f);
            float slope = ((float)rng.NextDouble() * 2f - 1f) * 0.16f;
            float half = size[la] * 0.5f;
            var mesh = Bake(pb, p =>
            {
                var q = p;
                float t = Mathf.Clamp01(p[la] / size[la] + 0.5f);
                q[ta] += Mathf.Sin(t * Mathf.PI) * bow;                          // hafif yay
                if (p[la] > half * 0.5f) q[la] += p[wa] * slope;                 // eğri kesilmiş uç
                return q;
            }, beveled ? -1 : 0);

            var uvOffset = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            mesh = FinishBoard(mesh, la, tiling, uvOffset, key);
            mesh = LowPolyWorldBuilder.SaveMesh(mesh, key);
            boardCache[key] = mesh;
            return mesh;
        }

        static Mesh FinishBoard(Mesh mesh, int longAxis, float tiling, Vector2 offset, string name)
        {
            var srcV = mesh.vertices;
            var srcN = mesh.normals;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = mesh.triangles;
            var outTris = new int[tris.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 a = srcV[tris[t]], b = srcV[tris[t + 1]], c = srcV[tris[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Vector3 abs = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                int axis = abs.y >= abs.x && abs.y >= abs.z ? 1 : abs.x >= abs.z ? 0 : 2;
                int u = longAxis != axis ? longAxis : (axis + 1) % 3;
                int v = 3 - axis - u;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = srcV[tris[t + k]];
                    outTris[t + k] = verts.Count;
                    verts.Add(p);
                    normals.Add(srcN.Length > 0 ? srcN[tris[t + k]] : n.normalized);
                    uvs.Add(new Vector2(p[u], p[v]) * tiling + offset);
                }
            }
            var result = new Mesh { name = name };
            result.SetVertices(verts);
            result.SetNormals(normals);
            result.SetUVs(0, uvs);
            result.SetTriangles(outTris, 0);
            result.RecalculateTangents();
            result.RecalculateBounds();
            UnityEngine.Object.DestroyImmediate(mesh);
            return result;
        }

        static void MarkCaps(ProBuilderMesh pb, Vector3 direction, int submesh)
        {
            var positions = pb.positions;
            foreach (var f in pb.faces)
            {
                var idx = f.indexes;
                if (idx.Count < 3) continue;
                Vector3 a = positions[idx[0]], b = positions[idx[1]], c = positions[idx[2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (Mathf.Abs(Vector3.Dot(n, direction)) > 0.9f && Vector3.Dot((a + b + c) / 3f - pb.transform.position, direction) > 0f)
                    f.submeshIndex = submesh;
            }
        }

        // ================================================================ mesh yardımcıları

        static Mesh Combine(List<(Mesh mesh, Matrix4x4 matrix)> parts)
        {
            var combine = new CombineInstance[parts.Count];
            for (int i = 0; i < parts.Count; i++)
                combine[i] = new CombineInstance { mesh = parts[i].mesh, transform = parts[i].matrix };
            var result = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            result.CombineMeshes(combine, true, true);
            foreach (var p in parts) UnityEngine.Object.DestroyImmediate(p.mesh);
            return result;
        }

        delegate Vector2 UvMap(Vector3 position, Vector3 normal, int submesh, float tiling);

        /// <summary>UV açma: her köşe kendi izdüşümüne göre (kutu / silindir), birimler metre × döşeme.</summary>
        static Mesh Finish(Mesh mesh, UvMap map, float tiling, string name)
        {
            if (map == BoxUV) return FinishBox(mesh, tiling, name);
            // Silindirik izdüşüm üçgen başına: atan2 dikişinde u 2→0 atlarsa üçgen tüm dokuyu
            // sıkıştırıp çizgili bir bant çizerdi. Köşeleri ayırıp dikişi kapatıyoruz.
            var srcV = mesh.vertices;
            var srcN = mesh.normals;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subTris = new List<int[]>();
            var tri = new Vector2[3];
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                var tris = mesh.GetTriangles(sm);
                var outTris = new int[tris.Length];
                for (int t = 0; t < tris.Length; t += 3)
                {
                    float min = float.MaxValue, max = float.MinValue;
                    for (int k = 0; k < 3; k++)
                    {
                        int i = tris[t + k];
                        tri[k] = map(srcV[i], srcN.Length > i ? srcN[i] : Vector3.up, sm, tiling);
                        min = Mathf.Min(min, tri[k].x);
                        max = Mathf.Max(max, tri[k].x);
                    }
                    if (max - min > 1f)                        // dikişten geçen üçgen
                        for (int k = 0; k < 3; k++) if (tri[k].x < 1f) tri[k].x += 2f;
                    for (int k = 0; k < 3; k++)
                    {
                        int i = tris[t + k];
                        outTris[t + k] = verts.Count;
                        verts.Add(srcV[i]);
                        normals.Add(srcN.Length > i ? srcN[i] : Vector3.up);
                        uvs.Add(tri[k]);
                    }
                }
                subTris.Add(outTris);
            }
            var result = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            result.SetVertices(verts);
            result.SetNormals(normals);
            result.SetUVs(0, uvs);
            result.subMeshCount = subTris.Count;
            for (int sm = 0; sm < subTris.Count; sm++) result.SetTriangles(subTris[sm], sm);
            result.RecalculateTangents();
            result.RecalculateBounds();
            UnityEngine.Object.DestroyImmediate(mesh);
            return result;
        }

        /// <summary>
        /// Kutu izdüşümü üçgen başına: her üçgen kendi baskın eksenine göre UV alır (köşeleri ayrılır),
        /// böylece eksen geçişlerinde doku uzayıp çizgilenmez. Yumuşak normaller korunur.
        /// </summary>
        static Mesh FinishBox(Mesh mesh, float tiling, string name)
        {
            var srcV = mesh.vertices;
            var srcN = mesh.normals;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subTris = new List<int[]>();
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                var tris = mesh.GetTriangles(sm);
                var outTris = new int[tris.Length];
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = srcV[tris[t]], b = srcV[tris[t + 1]], c = srcV[tris[t + 2]];
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    Vector3 abs = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                    int axis = abs.y >= abs.x && abs.y >= abs.z ? 1 : abs.x >= abs.z ? 0 : 2;
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 p = srcV[tris[t + k]];
                        Vector2 uv = axis == 1 ? new Vector2(p.x, p.z) : axis == 0 ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
                        outTris[t + k] = verts.Count;
                        verts.Add(p);
                        normals.Add(srcN.Length > 0 ? srcN[tris[t + k]] : n.normalized);
                        uvs.Add(uv * tiling);
                    }
                }
                subTris.Add(outTris);
            }

            var result = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            result.SetVertices(verts);
            result.SetNormals(normals);
            result.SetUVs(0, uvs);
            result.subMeshCount = subTris.Count;
            for (int sm = 0; sm < subTris.Count; sm++) result.SetTriangles(subTris[sm], sm);
            result.RecalculateTangents();
            result.RecalculateBounds();
            UnityEngine.Object.DestroyImmediate(mesh);
            return result;
        }

        static Vector2 BoxUV(Vector3 p, Vector3 n, int submesh, float tiling)
        {
            Vector3 a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x, p.z) * tiling;
            if (a.x >= a.z) return new Vector2(p.z, p.y) * tiling;
            return new Vector2(p.x, p.y) * tiling;
        }

        static Vector2 CylinderUV(Vector3 p, Vector3 n, int submesh, float tiling)
        {
            if (submesh == 1 || Mathf.Abs(n.y) > 0.8f) return new Vector2(p.x, p.z) * 0.5f + new Vector2(0.5f, 0.5f);
            float u = (Mathf.Atan2(p.z, p.x) / (Mathf.PI * 2f) + 0.5f) * 2f;
            return new Vector2(u, p.y * tiling);
        }

        static Vector2 LogUV(Vector3 p, Vector3 n, int submesh, float tiling)
        {
            if (submesh == 1) return new Vector2(p.x, p.y) * 2.6f + new Vector2(0.5f, 0.5f);
            float u = (Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) + 0.5f) * 2f;
            return new Vector2(u, p.z * 1.2f);
        }

        /// <summary>Low poly arazi mesh'ini yumuşatır (köşe birleştirme + yumuşak normal) ve düzlemsel UV verir.</summary>
        public static void SoftenTerrain(Mesh mesh, float tiling)
        {
            var src = mesh.vertices;
            var tris = mesh.triangles;
            var map = new Dictionary<Vector3Int, int>();
            var verts = new List<Vector3>();
            var remap = new int[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(src[i].x * 1000f), Mathf.RoundToInt(src[i].y * 1000f), Mathf.RoundToInt(src[i].z * 1000f));
                if (!map.TryGetValue(key, out int idx))
                {
                    idx = verts.Count;
                    verts.Add(src[i]);
                    map[key] = idx;
                }
                remap[i] = idx;
            }
            for (int i = 0; i < tris.Length; i++) tris[i] = remap[tris[i]];

            var uvs = new Vector2[verts.Count];
            for (int i = 0; i < verts.Count; i++) uvs[i] = new Vector2(verts[i].x, verts[i].z) * tiling;

            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        // ================================================================ prosedürel dokular

        static Texture2D Paint(string name, Func<float, float, Color> pattern)
        {
            string path = $"{TextureFolder}/{name}.png";
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            var pixels = new Color[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
                pixels[y * TexSize + x] = pattern((x + 0.5f) / TexSize, (y + 0.5f) / TexSize);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 4;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // --- desenler (hepsi döşenebilir: periyodik gürültü)

        /// <summary>Yumuşak tonlar üzerine fırça darbesi gibi açık "yaprak lekeleri".</summary>
        static Color LeafPattern(float u, float v, Color dark, Color light, int seed)
        {
            float n = Fbm(u, v, 4, 4, seed);
            Color c = Color.Lerp(dark, Color.Lerp(dark, light, 0.55f), Smooth(0.35f, 0.7f, n));
            float dabs = Dabs(u, v, 9, seed, 0.32f);
            c = Color.Lerp(c, light, dabs * 0.85f);
            float dabs2 = Dabs(u, v, 17, seed + 5, 0.25f);
            c = Color.Lerp(c, light * 1.08f, dabs2 * 0.55f);
            float shade = Dabs(u, v, 7, seed + 9, 0.3f);
            c = Color.Lerp(c, dark * 0.85f, shade * 0.25f);
            return c;
        }

        static Color BarkPattern(float u, float v)
        {
            Color baseC = new Color(0.52f, 0.36f, 0.25f);
            Color dark = new Color(0.36f, 0.24f, 0.17f);
            Color light = new Color(0.64f, 0.47f, 0.33f);
            float warp = Fbm(u, v, 3, 2, 3) * 2.2f;
            float stripes = Mathf.Sin((u * 8f + warp) * Mathf.PI * 2f) * 0.5f + 0.5f;
            Color c = Color.Lerp(baseC, light, Fbm(u, v, 6, 3, 8) * 0.6f);
            c = Color.Lerp(c, dark, Smooth(0.72f, 0.95f, stripes) * 0.75f);
            return c;
        }

        static Color RockPattern(float u, float v)
        {
            Color a = new Color(0.62f, 0.62f, 0.64f);
            Color b = new Color(0.74f, 0.73f, 0.72f);
            Color c = Color.Lerp(a, b, Smooth(0.35f, 0.7f, Fbm(u, v, 3, 4, 41)));
            c = Color.Lerp(c, new Color(0.54f, 0.58f, 0.52f), Smooth(0.6f, 0.8f, Fbm(u, v, 2, 3, 77)) * 0.35f); // hafif yosun
            float speck = Dabs(u, v, 24, 13, 0.12f);
            c = Color.Lerp(c, c * 0.82f, speck * 0.6f);
            return c;
        }

        static Color RingsPattern(float u, float v)
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float wobble = Fbm(u, v, 4, 2, 5) * 0.08f;
            float rings = Mathf.Sin((r + wobble) * 34f) * 0.5f + 0.5f;
            Color inner = new Color(0.93f, 0.78f, 0.55f);
            Color ring = new Color(0.80f, 0.60f, 0.40f);
            Color c = Color.Lerp(inner, ring, Smooth(0.6f, 0.9f, rings) * 0.7f);
            if (r > 0.92f) c = Color.Lerp(c, new Color(0.45f, 0.30f, 0.21f), Smooth(0.92f, 0.98f, r));
            return c;
        }

        static Color WoodPattern(float u, float v, Color baseC)
        {
            float warp = Fbm(u, v, 2, 3, 19) * 1.5f;
            float grain = Mathf.Sin((v * 10f + warp) * Mathf.PI * 2f) * 0.5f + 0.5f;
            Color c = Color.Lerp(baseC * 1.06f, baseC * 0.84f, Smooth(0.55f, 0.95f, grain));
            float knot = Dabs(u, v, 3, 21, 0.1f);
            c = Color.Lerp(c, baseC * 0.7f, knot * 0.5f);
            return c;
        }

        /// <summary>Yuvarlak uçlu, şaşırtmalı sıralı kiremit/ahşap pul (satırlar v boyunca).</summary>
        static Color ShinglePattern(float u, float v)
        {
            const int rows = 6, cols = 4;
            float y = v * rows;
            int row = Mathf.FloorToInt(y);
            float fy = y - row;
            float x = u * cols + ((row & 1) == 1 ? 0.5f : 0f);
            int col = Mathf.FloorToInt(x);
            float fx = x - col;
            int wc = ((col % cols) + cols) % cols, wr = ((row % rows) + rows) % rows;
            float h = Hash(wc, wr, 91);
            Color c = Color.Lerp(new Color(0.66f, 0.33f, 0.24f), new Color(0.78f, 0.43f, 0.29f), h);
            c = Color.Lerp(c, c * 0.86f, Fbm(u, v, 4, 3, 93) * 0.5f);
            c *= Mathf.Lerp(1.08f, 0.9f, fy);                                   // alt dudak açık, üstü gölgede
            float rim = 0.08f + 0.22f * Mathf.Pow(2f * fx - 1f, 2f);           // yuvarlak alt kenar
            c = Color.Lerp(c * 0.55f, c, Smooth(rim, rim + 0.05f, fy));
            float gap = Mathf.Min(fx, 1f - fx);
            c = Color.Lerp(c * 0.7f, c, Smooth(0.0f, 0.035f, gap));
            c.a = 1f;
            return c;
        }

        /// <summary>İki tonlu yumuşak çimen; isteğe bağlı minik çiçek noktaları.</summary>
        static Color GroundPattern(float u, float v, Color a, Color b, bool flowers)
        {
            Color c = Color.Lerp(a, b, Smooth(0.3f, 0.7f, Fbm(u, v, 3, 4, 61)));
            c = Color.Lerp(c, a * 0.9f, Dabs(u, v, 14, 63, 0.3f) * 0.25f);
            c = Color.Lerp(c, b * 1.06f, Dabs(u, v, 22, 65, 0.22f) * 0.35f);
            if (flowers)
            {
                float f1 = Dabs(u, v, 12, 81, 0.06f);
                float f2 = Dabs(u, v, 10, 83, 0.05f);
                c = Color.Lerp(c, new Color(1f, 0.96f, 0.86f), Smooth(0.5f, 0.7f, f1));
                c = Color.Lerp(c, new Color(1f, 0.82f, 0.38f), Smooth(0.5f, 0.7f, f2));
            }
            return c;
        }

        // --- gürültü

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Periyodik değer gürültüsü (u,v ∈ [0,1], periyot p hücre).</summary>
        static float ValueNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int Wrap(int i) => ((i % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int p = basePeriod;
            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(u, v, p, seed + i * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / norm;
        }

        /// <summary>Hücre başına rastgele yerleşmiş yumuşak lekeler (fırça darbesi). Döşenebilir.</summary>
        static float Dabs(float u, float v, int cells, int seed, float radius)
        {
            float best = 0f;
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int gx = cx + ox, gy = cy + oy;
                int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                float px = gx + Hash(wx, wy, seed), py = gy + Hash(wx, wy, seed + 1);
                float r = radius * (0.6f + Hash(wx, wy, seed + 2) * 0.8f);
                // hafif eliptik, eğik leke
                float ang = Hash(wx, wy, seed + 3) * Mathf.PI;
                float dx = x - px, dy = y - py;
                float rx = dx * Mathf.Cos(ang) + dy * Mathf.Sin(ang);
                float ry = -dx * Mathf.Sin(ang) + dy * Mathf.Cos(ang);
                float d = Mathf.Sqrt(rx * rx + ry * ry * 2.2f) / r;
                best = Mathf.Max(best, 1f - Smooth(0.55f, 1f, d));
            }
            return best;
        }

        static float Noise3(Vector3 p)
        {
            // basit 3B değer gürültüsü (deformasyon için)
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float H(int x, int y, int z) => Hash(x + z * 157, y - z * 311, 7);
            float a = Mathf.Lerp(Mathf.Lerp(H(x0, y0, z0), H(x0 + 1, y0, z0), fx), Mathf.Lerp(H(x0, y0 + 1, z0), H(x0 + 1, y0 + 1, z0), fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(H(x0, y0, z0 + 1), H(x0 + 1, y0, z0 + 1), fx), Mathf.Lerp(H(x0, y0 + 1, z0 + 1), H(x0 + 1, y0 + 1, z0 + 1), fx), fy);
            return Mathf.Lerp(a, b, fz);
        }
    }
}
