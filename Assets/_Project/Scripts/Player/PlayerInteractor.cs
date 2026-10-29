using System;
using LandFlipper.Core;
using LandFlipper.Gameplay;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LandFlipper.Player
{
    /// <summary>
    /// Nişangâhla hedef seçme ve etkileşim (GDD 9.1). Local oyuncu hedefi bulur ve istek gönderir;
    /// host mesafe, kapasite ve "elin dolu" kurallarını yeniden doğrular.
    ///  Sol tık: biç / kes / kır     E: al / sat     R: üstteki nesneyi bırak (basılı tut: hepsini)
    /// </summary>
    [RequireComponent(typeof(PlayerCarry))]
    public class PlayerInteractor : NetworkBehaviour
    {
        [SerializeField] float interactRange = 3.2f;
        [SerializeField] float serverRange = 4.8f;
        [SerializeField] float hitCooldown = 0.45f;
        [SerializeField] float dropAllHoldTime = 0.45f;
        [SerializeField] LayerMask interactMask = ~0;

        static readonly RaycastHit[] hitBuffer = new RaycastHit[24];

        FirstPersonController controller;
        PlayerCarry carry;
        InputAction primaryAction, interactAction, dropAction;
        float nextHitTime;
        float dropPressedAt = -1f;
        bool dropAllTriggered;
        double serverNextHitTime;

        public ResourceNode TargetNode { get; private set; }
        public Pickup TargetPickup { get; private set; }
        public SellStation TargetStation { get; private set; }
        public string Prompt { get; private set; } = string.Empty;

        /// <summary>Local oyuncuya gösterilecek kısa bildirim.</summary>
        public event Action<string> Notification;

        void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            carry = GetComponent<PlayerCarry>();
        }

        public override void OnStartLocalPlayer()
        {
            primaryAction = new InputAction("Primary", InputActionType.Button, "<Mouse>/leftButton");
            primaryAction.AddBinding("<Gamepad>/rightTrigger");
            interactAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            interactAction.AddBinding("<Gamepad>/buttonWest");
            dropAction = new InputAction("Drop", InputActionType.Button, "<Keyboard>/r");
            dropAction.AddBinding("<Gamepad>/buttonEast");
            primaryAction.Enable();
            interactAction.Enable();
            dropAction.Enable();
        }

        public override void OnStopLocalPlayer()
        {
            primaryAction?.Dispose();
            interactAction?.Dispose();
            dropAction?.Dispose();
        }

        void Update()
        {
            if (!isLocalPlayer || primaryAction == null) return;

            if (InputGate.IsBlocked)
            {
                ClearTarget();
                dropPressedAt = -1f;
                return;
            }

            FindTarget();
            Prompt = BuildPrompt();

            if (primaryAction.WasPressedThisFrame()) OnPrimary();
            if (interactAction.WasPressedThisFrame()) OnInteract();
            HandleDrop();
        }

        // ------------------------------------------------------------ targeting

        void ClearTarget()
        {
            TargetNode = null;
            TargetPickup = null;
            TargetStation = null;
            Prompt = string.Empty;
        }

        void FindTarget()
        {
            ClearTarget();
            Transform eye = controller.CameraPivot;
            int count = Physics.RaycastNonAlloc(eye.position, eye.forward, hitBuffer, interactRange, interactMask,
                QueryTriggerInteraction.Collide);

            Array.Sort(hitBuffer, 0, count, HitDistanceComparer.Instance);

            const float pickupBias = 0.6f; // çimenin içinde/arkasında duran nesne yine seçilebilsin
            Pickup pickupHit = null;
            float pickupDistance = float.MaxValue;
            ResourceNode nodeHit = null;
            SellStation stationHit = null;
            float otherDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                var col = hit.collider;
                if (col.transform.IsChildOf(transform)) continue;
                if (hit.distance > otherDistance + pickupBias) break;

                var pickup = col.GetComponentInParent<Pickup>();
                if (pickup != null)
                {
                    if (!pickup.IsCarried && pickupHit == null)
                    {
                        pickupHit = pickup;
                        pickupDistance = hit.distance;
                    }
                    continue;
                }

                if (otherDistance < float.MaxValue)
                {
                    if (!col.isTrigger) break; // ilk hedefin arkasındaki katı nesne: dur
                    continue;
                }

                var station = col.GetComponentInParent<SellStation>();
                var node = station == null ? col.GetComponentInParent<ResourceNode>() : null;
                if (station != null) stationHit = station;
                else if (node != null && !node.IsDepleted) nodeHit = node;
                else if (col.isTrigger) continue;

                otherDistance = hit.distance; // hedef ya da engel (zemin, duvar, kütük dibi)
                if (!col.isTrigger && pickupHit != null) break;
            }

            if (pickupHit != null && pickupDistance <= otherDistance + pickupBias)
                TargetPickup = pickupHit;
            else
            {
                TargetStation = stationHit;
                TargetNode = nodeHit;
            }
        }

        class HitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        string BuildPrompt()
        {
            if (TargetPickup != null)
            {
                var def = TargetPickup.Definition;
                return carry.HasRoomFor(TargetPickup)
                    ? $"[E] Al · {def.displayName} ({Currency.Format(def.sellPrice)})"
                    : $"Yük dolu ({carry.UsedSlots}/{carry.CapacitySlots}) · [R] Bırak";
            }

            if (TargetStation != null)
            {
                if (!carry.IsCarrying) return "Satış Tezgâhı · Satmak için kaynak getir";
                int total = 0;
                foreach (var item in carry.Items) total += item.Definition.sellPrice;
                return $"[E] Sat · {carry.Count} kaynak · {Currency.Format(total)}";
            }

            if (TargetNode != null)
            {
                if (carry.IsCarrying) return "Elin dolu · [R] Bırak";
                string progress = TargetNode.MaxHits > 1 ? $" ({TargetNode.HitsTaken}/{TargetNode.MaxHits})" : string.Empty;
                return $"[Sol Tık] {TargetNode.ActionVerb} · {TargetNode.DisplayName}{progress}";
            }

            return carry.IsCarrying ? "[R] Bırak · Basılı tut: hepsini bırak" : string.Empty;
        }

        // ------------------------------------------------------------ actions

        void OnPrimary()
        {
            if (TargetNode == null) return;
            if (carry.IsCarrying)
            {
                Notification?.Invoke("Elinde yük varken kesemez/kıramazsın. Önce R ile bırak.");
                return;
            }
            if (Time.time < nextHitTime) return;

            nextHitTime = Time.time + hitCooldown;
            controller.Kick(TargetNode.Kind == NodeKind.Grass ? 1.2f : 2.5f);
            CmdHit(TargetNode.ObjectId);
        }

        void OnInteract()
        {
            if (TargetPickup != null)
            {
                if (!carry.HasRoomFor(TargetPickup))
                {
                    Notification?.Invoke($"Taşıma kapasiten dolu ({carry.UsedSlots}/{carry.CapacitySlots}).");
                    return;
                }
                CmdPickUp(TargetPickup.netIdentity);
            }
            else if (TargetStation != null)
            {
                if (carry.IsCarrying) CmdSell();
                else Notification?.Invoke("Satacak bir şey taşımıyorsun.");
            }
        }

        void HandleDrop()
        {
            if (dropAction.WasPressedThisFrame())
            {
                dropPressedAt = Time.time;
                dropAllTriggered = false;
            }

            if (dropPressedAt >= 0f && !dropAllTriggered && Time.time - dropPressedAt >= dropAllHoldTime)
            {
                dropAllTriggered = true;
                if (carry.IsCarrying) CmdDropAll();
            }

            if (dropAction.WasReleasedThisFrame())
            {
                if (!dropAllTriggered && carry.IsCarrying) CmdDropTop();
                dropPressedAt = -1f;
            }
        }

        // ------------------------------------------------------------ server

        bool ServerInRange(Vector3 point)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            return d.magnitude <= serverRange;
        }

        [Command]
        void CmdHit(int objectId)
        {
            if (carry.IsCarrying) return;
            if (NetworkTime.time < serverNextHitTime) return;
            var world = PlotWorld.Instance;
            if (world == null || !world.TryGetNode(objectId, out var node)) return;
            if (!ServerInRange(node.transform.position)) return;

            serverNextHitTime = NetworkTime.time + hitCooldown * 0.8f;
            world.ServerApplyHit(objectId, transform.position);
        }

        [Command]
        void CmdPickUp(NetworkIdentity item)
        {
            if (item == null) return;
            var pickup = item.GetComponent<Pickup>();
            if (pickup == null || pickup.IsCarried || !ServerInRange(pickup.transform.position)) return;
            if (!carry.ServerTryAdd(pickup))
                TargetNotify(connectionToClient, "Taşıma kapasiten dolu.");
        }

        [Command]
        void CmdDropTop()
        {
            var pickup = carry.ServerRemoveTop();
            if (pickup != null) ServerPlace(pickup, 0);
        }

        [Command]
        void CmdDropAll()
        {
            var items = carry.ServerTakeAll();
            for (int i = 0; i < items.Count; i++) ServerPlace(items[i], i);
        }

        [Server]
        void ServerPlace(Pickup pickup, int index)
        {
            Vector3 forward = transform.forward;
            Vector3 position = transform.position + forward * 1.0f + Vector3.up * (0.7f + index * 0.35f)
                               + transform.right * UnityEngine.Random.Range(-0.15f, 0.15f);
            Quaternion rotation = Quaternion.Euler(0f, transform.eulerAngles.y + pickup.Definition.carryEuler.y, 0f);
            pickup.ServerDrop(position, rotation, forward * 0.8f);
        }

        [Command]
        void CmdSell()
        {
            var station = SellStation.FindInRange(transform.position);
            if (station == null || CareerState.Instance == null)
            {
                TargetNotify(connectionToClient, "Satış tezgâhına daha yakın olmalısın.");
                return;
            }

            var items = carry.ServerTakeAll();
            int total = 0;
            foreach (var item in items)
            {
                total += item.Definition.sellPrice;
                NetworkServer.Destroy(item.gameObject);
            }

            if (items.Count == 0) return;
            CareerState.Instance.ServerAddMoney(total);
            TargetNotify(connectionToClient, $"{items.Count} kaynak satıldı · +{Currency.Format(total)}");
        }

        [TargetRpc]
        void TargetNotify(NetworkConnectionToClient target, string message) => Notification?.Invoke(message);

#if UNITY_EDITOR
        public void EditorSetMask(LayerMask mask) => interactMask = mask;
#endif
    }
}
