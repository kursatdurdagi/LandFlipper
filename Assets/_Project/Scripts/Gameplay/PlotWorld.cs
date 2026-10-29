using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace LandFlipper.Gameplay
{
    /// <summary>
    /// Arazinin ağ üzerindeki tek otoritesi. Düğüm durumları ObjectID ile SyncDictionary'de
    /// tutulur; geç katılan oyuncular güncel arazi durumunu buradan alır (GDD 19.2).
    /// Tüm durum değişiklikleri ve kaynak spawn'ları sadece host'ta yapılır.
    /// </summary>
    public class PlotWorld : NetworkBehaviour
    {
        public static PlotWorld Instance { get; private set; }

        [Header("Plot")]
        [SerializeField] float halfSize = 18f;
        [Tooltip("Kütük/parçaların çite olan en yakın mesafesi.")]
        [SerializeField] float boundaryMargin = 0.9f;
        [SerializeField] LayerMask groundMask = 1;

        [Header("Drops")]
        [SerializeField] ResourceDefinition logDefinition;
        [SerializeField] ResourceDefinition rockChunkDefinition;
        [SerializeField] ResourceDefinition grassDefinition;
        [SerializeField] ResourceDefinition plankDefinition;
        [Tooltip("Kaç çim öbeği biçilince bir balya oluşur.")]
        [SerializeField] int grassCutsPerBale = 3;

        [Header("XP (GDD 13: XP ilerleme sağlayan etkinliklerden kazanılır)")]
        [SerializeField] int grassXp = 2;
        [SerializeField] int bushXp = 4;
        [SerializeField] int rockXp = 8;
        [SerializeField] int treeXp = 12;
        [Tooltip("Kulübeden sökülen her parça için.")]
        [SerializeField] int shackPartXp = 3;

        readonly SyncDictionary<int, NodeState> states = new SyncDictionary<int, NodeState>();
        readonly Dictionary<int, ResourceNode> nodes = new Dictionary<int, ResourceNode>();
        int grassCutCounter;

        public float HalfSize => halfSize;

        void Awake()
        {
            Instance = this;
            foreach (var node in FindObjectsByType<ResourceNode>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!nodes.TryAdd(node.ObjectId, node))
                    Debug.LogError($"[LandFlipper] Tekrarlanan ObjectID {node.ObjectId}: {node.name}", node);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnStartClient()
        {
            states.OnAdd += OnStateAdded;
            states.OnSet += OnStateSet;

            // Geç katılım: mevcut değişiklikleri animasyonsuz uygula.
            if (!isServer)
                foreach (var pair in states)
                    ApplyLocal(pair.Key, pair.Value, false);
        }

        public override void OnStopClient()
        {
            states.OnAdd -= OnStateAdded;
            states.OnSet -= OnStateSet;
        }

        void OnStateAdded(int id)
        {
            if (!isServer) ApplyLocal(id, states[id], true);
        }

        void OnStateSet(int id, NodeState _)
        {
            if (!isServer) ApplyLocal(id, states[id], true);
        }

        void ApplyLocal(int id, NodeState state, bool animate)
        {
            if (nodes.TryGetValue(id, out var node)) node.ApplyState(state, animate);
        }

        public bool TryGetNode(int id, out ResourceNode node) => nodes.TryGetValue(id, out node);

        /// <summary>Kaynak tanımını id ile bulur (istemci tarafı görsel efektler için).</summary>
        public ResourceDefinition FindDefinition(string id)
        {
            foreach (var def in new[] { logDefinition, rockChunkDefinition, grassDefinition, plankDefinition })
                if (def != null && def.id == id) return def;
            return null;
        }

        public NodeState GetState(int id) => states.TryGetValue(id, out var s) ? s : default;

        // ------------------------------------------------------------ server

        [Server]
        public bool ServerApplyHit(int id, Vector3 hitterPosition)
        {
            if (!nodes.TryGetValue(id, out var node)) return false;

            var state = GetState(id);
            if (state.depleted) return false;

            int hitsBefore = state.hitsTaken;
            state.hitsTaken++;
            if (state.hitsTaken >= node.MaxHits)
            {
                state.depleted = true;
                if (node is TreeNode tree)
                    state.fallAngle = (short)Mathf.RoundToInt(ChooseFallAngle(tree, hitterPosition));
            }

            states[id] = state;
            ApplyLocal(id, state, true);
            RpcHitFeedback(id);

            int xpReward = 0;
            if (node is ShackNode shack)
            {
                int from = shack.RemovedPartsFor(hitsBefore);
                int to = state.depleted ? shack.PartCount : shack.RemovedPartsFor(state.hitsTaken);
                SpawnShackPlanks(shack, from, to);
                xpReward += (to - from) * shackPartXp;
            }

            if (state.depleted)
            {
                OnNodeDepleted(node, state);
                xpReward += node.Kind switch
                {
                    NodeKind.Grass => grassXp,
                    NodeKind.Bush => bushXp,
                    NodeKind.Rock => rockXp,
                    NodeKind.Tree => treeXp,
                    _ => 0
                };
            }

            if (xpReward > 0 && CareerState.Instance != null)
                CareerState.Instance.ServerAddXp(xpReward);
            return true;
        }

        [ClientRpc]
        void RpcHitFeedback(int id)
        {
            if (nodes.TryGetValue(id, out var node)) node.PlayHitFeedback();
        }

        [Server]
        void OnNodeDepleted(ResourceNode node, NodeState state)
        {
            switch (node)
            {
                case GrassNode grass:
                    if (++grassCutCounter >= grassCutsPerBale)
                    {
                        grassCutCounter = 0;
                        Vector3 p = ClampInside(grass.transform.position);
                        p.y = GroundHeight(p) + 0.3f;
                        ServerSpawn(grassDefinition, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.up * 1.5f);
                    }
                    break;

                case RockNode rock:
                    for (int i = 0; i < rock.ChunkCount; i++)
                    {
                        Vector2 offset = Random.insideUnitCircle * rock.Radius * 0.7f;
                        Vector3 p = ClampInside(rock.transform.position + new Vector3(offset.x, 0f, offset.y));
                        p.y = GroundHeight(p) + 0.35f + i * 0.1f;
                        Vector3 impulse = new Vector3(offset.x, 1.2f, offset.y) * 1.5f;
                        ServerSpawn(rockChunkDefinition, p, Random.rotation, impulse);
                    }
                    break;

                case TreeNode tree:
                    StartCoroutine(SpawnLogsAfterFall(tree, state.fallAngle));
                    break;

                case BushNode bush:
                    for (int i = 0; i < bush.BaleCount; i++)
                    {
                        Vector3 p = ClampInside(bush.transform.position);
                        p.y = GroundHeight(p) + 0.4f + i * 0.35f;
                        ServerSpawn(grassDefinition, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.up * 1.5f);
                    }
                    break;
            }
        }

        [Server]
        void SpawnShackPlanks(ShackNode shack, int fromPart, int toPart)
        {
            for (int part = fromPart; part < toPart; part++)
            for (int i = 0; i < shack.PlanksPerPart; i++)
            {
                Vector3 p = ClampInside(shack.PartPosition(part) + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f)));
                p.y = Mathf.Max(GroundHeight(p) + 0.4f, shack.PartPosition(part).y);
                Vector3 impulse = new Vector3(Random.Range(-1f, 1f), 1.5f, Random.Range(-1f, 1f));
                ServerSpawn(plankDefinition, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), impulse);
            }
        }

        IEnumerator SpawnLogsAfterFall(TreeNode tree, float angle)
        {
            yield return new WaitForSeconds(TreeNode.FallDuration + 0.15f);

            Vector3 direction = TreeNode.AngleToDirection(angle);
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            int count = tree.LogCount;
            float spacing = tree.LogLength + 0.08f;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = tree.transform.position + direction * (0.5f + tree.LogLength * 0.5f + i * spacing);
                p = ClampInside(p);
                p.y = GroundHeight(p) + 0.3f;
                ServerSpawn(logDefinition, p, rotation, Vector3.zero);
            }
        }

        /// <summary>
        /// Oyuncudan uzağa düşmeyi tercih eder; ağacın ucu çit dışına taşacaksa
        /// 30° adımlarla döner, olmazsa arazi merkezine doğru düşürür.
        /// </summary>
        float ChooseFallAngle(TreeNode tree, Vector3 hitterPosition)
        {
            Vector3 basePos = tree.transform.position;
            Vector3 away = basePos - hitterPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            float preferred = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;

            for (int step = 0; step <= 12; step++)
            {
                float offset = (step % 2 == 1 ? 1f : -1f) * ((step + 1) / 2) * 30f;
                float angle = preferred + offset;
                Vector3 tip = basePos + TreeNode.AngleToDirection(angle) * tree.FallReach;
                if (IsInside(tip, boundaryMargin)) return Mathf.DeltaAngle(0f, angle);
            }

            Vector3 toCenter = transform.position - basePos;
            return Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
        }

        public bool IsInside(Vector3 point, float margin)
        {
            Vector3 local = point - transform.position;
            float limit = halfSize - margin;
            return Mathf.Abs(local.x) <= limit && Mathf.Abs(local.z) <= limit;
        }

        public Vector3 ClampInside(Vector3 point)
        {
            Vector3 c = transform.position;
            float limit = halfSize - boundaryMargin;
            point.x = Mathf.Clamp(point.x, c.x - limit, c.x + limit);
            point.z = Mathf.Clamp(point.z, c.z - limit, c.z + limit);
            return point;
        }

        public float GroundHeight(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * 10f, Vector3.down, out var hit, 30f, groundMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : transform.position.y;
        }

        [Server]
        public Pickup ServerSpawn(ResourceDefinition definition, Vector3 position, Quaternion rotation, Vector3 impulse)
        {
            if (definition == null || definition.prefab == null)
            {
                Debug.LogError("[LandFlipper] Kaynak tanımı ya da prefab eksik.");
                return null;
            }
            var pickup = Instantiate(definition.prefab, position, rotation);
            NetworkServer.Spawn(pickup.gameObject);
            pickup.ServerLaunch(impulse);
            return pickup;
        }

#if UNITY_EDITOR
        public void EditorSetup(float plotHalfSize, LayerMask ground, ResourceDefinition log, ResourceDefinition chunk,
            ResourceDefinition grass, ResourceDefinition plank)
        {
            plankDefinition = plank;
            halfSize = plotHalfSize;
            groundMask = ground;
            logDefinition = log;
            rockChunkDefinition = chunk;
            grassDefinition = grass;
        }
#endif
    }
}
