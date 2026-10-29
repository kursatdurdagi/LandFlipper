using System.Collections.Generic;
using LandFlipper.Gameplay;
using LandFlipper.Player.Animation;
using Mirror;
using UnityEngine;

namespace LandFlipper.Player
{
    /// <summary>
    /// Oyuncunun elinde üst üste taşıdığı kaynaklar (GDD 10). Liste host'ta değişir,
    /// SyncList ile herkese gider; dizilim her istemcide yerel olarak hesaplanır.
    /// </summary>
    public class PlayerCarry : NetworkBehaviour
    {
        [Tooltip("GDD 10: ilk sürümde dört taşıma yuvası. Skill tree ile artacak.")]
        [SerializeField] int capacitySlots = 4;
        [Tooltip("Gövde önündeki tutma noktası (diğer oyuncular ve 3. şahıs görünümü).")]
        [SerializeField] Transform carryAnchor;
        [Tooltip("Birinci şahısta yükün görüşü kapatmaması için ekranın altında, küçültülmüş gösterim noktası.")]
        [SerializeField] Transform viewAnchor;
        [SerializeField] float viewScale = 0.6f;
        [SerializeField] ProceduralCharacterAnimator characterAnimator;
        [SerializeField] float followSharpness = 30f;

        FirstPersonController controller;

        void Awake() => controller = GetComponent<FirstPersonController>();

        readonly SyncList<uint> carried = new SyncList<uint>();
        readonly List<Pickup> buffer = new List<Pickup>();

        public int CapacitySlots => capacitySlots;
        public int Count => carried.Count;
        public bool IsCarrying => carried.Count > 0;

        public int UsedSlots
        {
            get
            {
                int used = 0;
                foreach (var id in carried)
                    if (TryResolve(id, out var p)) used += p.Definition.slotCost;
                return used;
            }
        }

        public bool HasRoomFor(Pickup pickup) => UsedSlots + pickup.Definition.slotCost <= capacitySlots;

        /// <summary>Alttan üste doğru taşınan nesneler.</summary>
        public IReadOnlyList<Pickup> Items
        {
            get
            {
                buffer.Clear();
                foreach (var id in carried)
                    if (TryResolve(id, out var p)) buffer.Add(p);
                return buffer;
            }
        }

        static bool TryResolve(uint netId, out Pickup pickup)
        {
            pickup = null;
            NetworkIdentity identity;
            bool found = NetworkServer.active
                ? NetworkServer.spawned.TryGetValue(netId, out identity)
                : NetworkClient.spawned.TryGetValue(netId, out identity);
            if (found && identity != null) pickup = identity.GetComponent<Pickup>();
            return pickup != null;
        }

        // ------------------------------------------------------------ server

        [Server]
        public bool ServerTryAdd(Pickup pickup)
        {
            if (pickup == null || pickup.IsCarried || !HasRoomFor(pickup)) return false;
            carried.Add(pickup.netId);
            pickup.ServerSetCarrier(netId);
            return true;
        }

        [Server]
        public Pickup ServerRemoveTop()
        {
            while (carried.Count > 0)
            {
                uint id = carried[carried.Count - 1];
                carried.RemoveAt(carried.Count - 1);
                if (TryResolve(id, out var p)) return p;
            }
            return null;
        }

        [Server]
        public List<Pickup> ServerTakeAll()
        {
            var result = new List<Pickup>();
            foreach (var id in carried)
                if (TryResolve(id, out var p)) result.Add(p);
            carried.Clear();
            return result;
        }

        public override void OnStopServer()
        {
            // Oyuncu ayrılırsa taşıdıkları olduğu yere düşer.
            var items = ServerTakeAll();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                Vector3 p = transform.position + Vector3.up * (0.8f + i * 0.35f);
                items[i].ServerDrop(p, transform.rotation, Vector3.zero);
            }
        }

        // ------------------------------------------------------------ visuals

        void LateUpdate()
        {
            var items = Items;
            if (characterAnimator != null)
            {
                characterAnimator.IsHolding = items.Count > 0;
                characterAnimator.HoldTarget = carryAnchor;
            }
            if (items.Count == 0 || carryAnchor == null) return;

            bool firstPersonView = isLocalPlayer && viewAnchor != null && controller != null && !controller.IsThirdPerson;
            Transform anchor = firstPersonView ? viewAnchor : carryAnchor;
            float scale = firstPersonView ? viewScale : 1f;

            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            float height = 0f;
            foreach (var item in items)
            {
                var def = item.Definition;
                float h = def.stackHeight * scale;
                Vector3 targetPos = anchor.position + anchor.up * (height + h * 0.5f);
                Quaternion targetRot = anchor.rotation * Quaternion.Euler(def.carryEuler);
                item.transform.SetPositionAndRotation(
                    Vector3.Lerp(item.transform.position, targetPos, k),
                    Quaternion.Slerp(item.transform.rotation, targetRot, k));
                item.transform.localScale = Vector3.one * scale;
                height += h;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform anchor, Transform firstPersonAnchor, ProceduralCharacterAnimator animator)
        {
            carryAnchor = anchor;
            viewAnchor = firstPersonAnchor;
            characterAnimator = animator;
        }
#endif
    }
}
