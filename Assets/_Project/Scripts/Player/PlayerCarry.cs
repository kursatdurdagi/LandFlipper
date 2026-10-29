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
        [SerializeField] float viewScale = 0.38f;
        [Tooltip("1. şahısta yığın yukarı değil, hafif ileri doğru büyür (görüşü kapatmasın).")]
        [SerializeField] float viewStackLean = 0.3f;
        [SerializeField] ProceduralCharacterAnimator characterAnimator;
        [SerializeField] float followSharpness = 30f;

        [Header("First Person Bob")]
        [Tooltip("Dururken el ve yükün hafif inip kalkması (m).")]
        [SerializeField] float idleBob = 0.006f;
        [Tooltip("Yürürken adım ritminde sallanma (m).")]
        [SerializeField] float walkBob = 0.02f;

        FirstPersonController controller;
        CharacterController characterController;
        Vector3 viewAnchorBase;
        float bobPhase;

        void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            characterController = GetComponent<CharacterController>();
            if (viewAnchor != null) viewAnchorBase = viewAnchor.localPosition;
        }

        /// <summary>
        /// 1. şahıs çapasını sallar. El (FirstPersonArms) ve yük aynı çapayı kullandığı için birlikte oynarlar.
        /// </summary>
        void ApplyViewBob()
        {
            Vector3 v = characterController != null ? characterController.velocity : Vector3.zero;
            float moving = Mathf.Clamp01(new Vector3(v.x, 0f, v.z).magnitude / 4f);
            bobPhase += Time.deltaTime * Mathf.Lerp(2.2f, 9f, moving);
            float amplitude = Mathf.Lerp(idleBob, walkBob, moving);
            viewAnchor.localPosition = viewAnchorBase + new Vector3(
                Mathf.Cos(bobPhase * 0.5f) * amplitude * 0.5f,
                Mathf.Sin(bobPhase) * amplitude,
                0f);
        }

        readonly SyncList<uint> carried = new SyncList<uint>();
        readonly List<Pickup> buffer = new List<Pickup>();

        /// <summary>Temel kapasite + "Taşıma" yetenekleri (host kariyerinden, herkeste aynı).</summary>
        public int CapacitySlots => capacitySlots + Mathf.RoundToInt(CareerState.Stat(SkillStat.CarryCapacity));
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

        public bool HasRoomFor(Pickup pickup) => UsedSlots + pickup.Definition.slotCost <= CapacitySlots;

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
            if (pickup == null || pickup.IsCarried || !HasRoomFor(pickup)) return false; // kapasite host'ta yeniden doğrulanır
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
            if (firstPersonView) ApplyViewBob();
            Transform anchor = firstPersonView ? viewAnchor : carryAnchor;
            float scale = firstPersonView ? viewScale : 1f;

            Vector3 stackDirection = firstPersonView
                ? (anchor.up + anchor.forward * viewStackLean).normalized
                : anchor.up;

            // Ele yeni gelen nesne yumuşakça uçar; avuca oturunca ele kilitlenir.
            // (Sürekli takip etmek, hareket ederken el ile nesne arasında titreme yapıyordu.)
            settled.RemoveWhere(id => !carried.Contains(id));

            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            float height = 0f;
            foreach (var item in items)
            {
                var def = item.Definition;
                float h = def.stackHeight * scale;
                Vector3 targetPos = anchor.position + stackDirection * (height + h * 0.5f);
                Quaternion targetRot = anchor.rotation * Quaternion.Euler(def.carryEuler);
                var t = item.transform;

                if (settled.Contains(item.netId))
                {
                    t.SetPositionAndRotation(targetPos, targetRot);
                }
                else
                {
                    t.SetPositionAndRotation(Vector3.Lerp(t.position, targetPos, k), Quaternion.Slerp(t.rotation, targetRot, k));
                    if ((t.position - targetPos).sqrMagnitude < 0.0009f && Quaternion.Angle(t.rotation, targetRot) < 4f)
                        settled.Add(item.netId);
                }

                t.localScale = Vector3.one * scale;
                height += h;
            }
        }

        readonly HashSet<uint> settled = new HashSet<uint>();

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
