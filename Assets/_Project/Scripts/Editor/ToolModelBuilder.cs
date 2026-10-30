using System.Collections.Generic;
using LandFlipper.Player.Tools;
using UnityEditor;
using UnityEngine;

namespace LandFlipper.EditorTools
{
    /// <summary>
    /// Sanatçı modellerini (FBX) alet prefab'ına çevirir: ölçek ve eksen normalize edilir
    /// (+Y ağız ucu, +Z vuran yüz / vida ekseni, kök = sapın tutulduğu nokta), makasların iki yarısı
    /// vida noktasında dönen ayrı pivotlara alınır (ToolVisual aç-kapa yapabilsin).
    /// </summary>
    public static class ToolModelBuilder
    {
        public const string MeshFolder = "Assets/_Project/Art/Generated/ToolMeshes";

        class Part
        {
            public string Name;
            public List<Vector3> Vertices = new List<Vector3>();
            public List<Vector3> Normals = new List<Vector3>();
            public List<Vector2> Uvs = new List<Vector2>();
            public List<List<int>> SubTris = new List<List<int>>();
            public List<Material> Materials = new List<Material>();
            public bool IsHalf;      // ağız + sap (dönen yarı)
            public Vector3 BladeCentroid;
        }

        /// <summary>Makas modelinden prefab üretir. length: ağız ucu → sap sonu toplam uzunluk (m).</summary>
        public static GameObject BuildShears(string modelPath, string prefabPath, float length, float openAngle)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) return null;
            LowPolyWorldBuilder.EnsureFolder(MeshFolder);

            // --- model uzayında parçaları topla (prefab dönüşümleriyle, kök orijinde)
            var inst = (GameObject)Object.Instantiate(model);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var parts = new List<Part>();
            Vector3? pin = null;
            var bladePoints = new List<Vector3>();
            var handlePoints = new List<Vector3>();
            var all = new List<Vector3>();

            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null) continue;
                var m = mf.transform.localToWorldMatrix;
                var part = new Part { Name = mf.name.Replace("(Clone)", "") };
                var v = mesh.vertices;
                var n = mesh.normals;
                var uv = mesh.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    part.Vertices.Add(m.MultiplyPoint3x4(v[i]));
                    part.Normals.Add(n.Length > i ? m.MultiplyVector(n[i]).normalized : Vector3.up);
                    part.Uvs.Add(uv.Length > i ? uv[i] : Vector2.zero);
                }
                bool hasBlade = false, hasHandle = false;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var tris = new List<int>(mesh.GetTriangles(s));
                    part.SubTris.Add(tris);
                    var mat = s < mr.sharedMaterials.Length ? mr.sharedMaterials[s] : null;
                    part.Materials.Add(mat);
                    bool blade = mat != null && mat.name == "blade";
                    foreach (int i in tris)
                    {
                        var p = part.Vertices[i];
                        if (blade) { bladePoints.Add(p); part.BladeCentroid += p; } else handlePoints.Add(p);
                    }
                    if (blade) hasBlade = true; else hasHandle = true;
                }
                part.IsHalf = hasBlade && hasHandle;
                if (part.IsHalf)
                {
                    int count = 0;
                    for (int s = 0; s < part.Materials.Count; s++)
                        if (part.Materials[s] != null && part.Materials[s].name == "blade") count += part.SubTris[s].Count;
                    part.BladeCentroid /= Mathf.Max(1, count);
                }
                else
                {
                    // ağız/sap olmayan küçük parça: vida
                    var b = Bounds(part.Vertices);
                    if (!pin.HasValue || b.size.sqrMagnitude < 1e-3f || part.Name.ToLower().Contains("pin") || part.Name.Contains("Cylinder"))
                        pin = b.center;
                }
                all.AddRange(part.Vertices);
                parts.Add(part);
            }
            Object.DestroyImmediate(inst);

            // --- eksenler: uzun eksen → +Y (ağız yönü), ince eksen → Z (vida), kalan → X
            var bounds = Bounds(all);
            Vector3 size = bounds.size;
            int longAxis = MaxAxis(size);
            int thinAxis = MinAxis(size);
            int widthAxis = 3 - longAxis - thinAxis;

            Vector3 bladeC = Centroid(bladePoints), handleC = Centroid(handlePoints);
            Vector3 yDir = Axis(longAxis) * Mathf.Sign(bladeC[longAxis] - handleC[longAxis]);
            Vector3 zDir = Axis(thinAxis);
            Vector3 xDir = Vector3.Cross(yDir, zDir); // sağ el / sol el uyumu: det > 0
            var rot = Quaternion.Inverse(Quaternion.LookRotation(zDir, yDir));

            float scale = length / Mathf.Max(1e-5f, size[longAxis]);
            // tutma noktası: sapların (halkaların) ortası
            Vector3 grip = handleC;
            Vector3 pinPoint = pin ?? Vector3.Lerp(handleC, bladeC, 0.45f);
            Vector3 Map(Vector3 p) => rot * (p - grip) * scale;
            Vector3 pinTool = Map(pinPoint);

            // --- prefab
            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(prefabPath));
            Transform halfA = null, halfB = null;
            var halves = new List<(Transform t, float bladeX)>();
            foreach (var part in parts)
            {
                var verts = new Vector3[part.Vertices.Count];
                var norms = new Vector3[part.Normals.Count];
                Vector3 origin = part.IsHalf ? pinTool : Vector3.zero;
                for (int i = 0; i < verts.Length; i++)
                {
                    verts[i] = Map(part.Vertices[i]) - origin;
                    norms[i] = (rot * part.Normals[i]).normalized;
                }
                var mesh = new Mesh { name = $"{root.name}_{part.Name}" };
                mesh.vertices = verts;
                mesh.normals = norms;
                mesh.uv = part.Uvs.ToArray();
                mesh.subMeshCount = part.SubTris.Count;
                for (int s = 0; s < part.SubTris.Count; s++) mesh.SetTriangles(part.SubTris[s], s);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                mesh = LowPolyWorldBuilder.SaveMeshAt(mesh, $"{MeshFolder}/{mesh.name}.asset");

                var go = new GameObject(part.Name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = origin;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = part.Materials.ToArray();

                if (part.IsHalf) halves.Add((go.transform, Map(part.BladeCentroid).x));
            }

            // ağzı daha +X tarafında olan yarı açılırken -açı döner (ucu +X'e gider: ToolVisual B), diğeri +açı (A)
            halves.Sort((l, r) => l.bladeX.CompareTo(r.bladeX));
            if (halves.Count > 0) halfA = halves[0].t;
            if (halves.Count > 1) halfB = halves[halves.Count - 1].t;

            root.AddComponent<ToolVisual>().EditorSetup(halfA, halfB, Vector3.forward, openAngle);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Bounds Bounds(List<Vector3> points)
        {
            var b = new Bounds(points.Count > 0 ? points[0] : Vector3.zero, Vector3.zero);
            foreach (var p in points) b.Encapsulate(p);
            return b;
        }

        static Vector3 Centroid(List<Vector3> points)
        {
            Vector3 sum = Vector3.zero;
            foreach (var p in points) sum += p;
            return points.Count > 0 ? sum / points.Count : Vector3.zero;
        }

        static int MaxAxis(Vector3 v) => v.x >= v.y && v.x >= v.z ? 0 : v.y >= v.z ? 1 : 2;
        static int MinAxis(Vector3 v) => v.x <= v.y && v.x <= v.z ? 0 : v.y <= v.z ? 1 : 2;
        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
    }
}
