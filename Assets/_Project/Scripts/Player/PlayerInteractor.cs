using System;
using LandFlipper.Core;
using LandFlipper.Gameplay;
using LandFlipper.Player.Tools;
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
        PlayerTools tools;

        // savurma: vuruş, alet hedefe değdiği anda gönderilir
        int pendingHitId = -1;
        float pendingHitTime;
        NodeKind pendingHitKind;
        InputAction primaryAction, interactAction, dropAction;
        float nextHitTime;
        float dropPressedAt = -1f;
        bool dropAllTriggered;
        double serverNextHitTime;

        // basılı tutarak biçme (çim vb.)
        ResourceNode holdNode;
        float holdTime;
        float nextHoldFeedback;
        int serverHoldId = -1;
        double serverHoldStart;

        // satış: E tek basış = 1 kaynak, basılı tutma = hızlanan tempoyla tek tek
        [Header("Selling")]
        [SerializeField] float sellFirstInterval = 0.38f;
        [SerializeField] float sellMinInterval = 0.12f;
        [SerializeField] float sellAcceleration = 0.78f;
        [Tooltip("Para, kaynak tezgâha ulaştığında eklenir (uçuş süresiyle uyumlu).")]
        [SerializeField] float sellMoneyDelay = 0.4f;
        bool sellHolding;
        float nextSellTime;
        float sellInterval;

        CharacterController characterController;
        bool serverDropAllRunning;

        /// <summary>0-1 arası basılı tutma ilerlemesi; basılı tutulmuyorsa 0. HUD halkası bunu çizer.</summary>
        public float HoldProgress { get; private set; }

        /// <summary>"Toplama" yetenekleriyle kısalan vuruş bekleme süresi.</summary>
        float EffectiveHitCooldown => hitCooldown / (1f + CareerState.Stat(SkillStat.HitSpeed));

        public ResourceNode TargetNode { get; private set; }
        public Pickup TargetPickup { get; private set; }
        public SellStation TargetStation { get; private set; }
        public Workbench TargetWorkbench { get; private set; }

        /// <summary>Local oyuncu Alet Tezgâhı'nda etkileşime girdi (UI açılır).</summary>
        public static event Action WorkbenchRequested;
        public string Prompt { get; private set; } = string.Empty;

        /// <summary>Local oyuncuya gösterilecek kısa bildirim.</summary>
        public event Action<string> Notification;

        void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            carry = GetComponent<PlayerCarry>();
            tools = GetComponent<PlayerTools>();
            characterController = GetComponent<CharacterController>();
        }

        public override void OnStartLocalPlayer()
        {
            // Değiştirilebilir tuşlar merkezi LFInput'tan gelir (Ayarlar → Tuşlar).
            LFInput.EnsureCreated();
            primaryAction = LFInput.Primary;
            interactAction = LFInput.Interact;
            dropAction = LFInput.Drop;
        }

        public override void OnStopLocalPlayer()
        {
            primaryAction = null;
            interactAction = null;
            dropAction = null;
        }

        void Update()
        {
            if (!isLocalPlayer || primaryAction == null) return;

            if (InputGate.IsBlocked)
            {
                ClearTarget();
                ResetHold();
                pendingHitId = -1;
                sellHolding = false;
                dropPressedAt = -1f;
                return;
            }

            FindTarget();
            Prompt = BuildPrompt();

            UpdatePendingHit();
            if (primaryAction.WasPressedThisFrame()) OnPrimary();
            UpdateHold();
            if (interactAction.WasPressedThisFrame()) OnInteract();
            UpdateSellHold();
            HandleDrop();
        }

        // ------------------------------------------------------------ targeting

        void ClearTarget()
        {
            TargetNode = null;
            TargetPickup = null;
            TargetStation = null;
            TargetWorkbench = null;
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
            Workbench benchHit = null;
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
                var bench = station == null ? col.GetComponentInParent<Workbench>() : null;
                var node = station == null && bench == null ? col.GetComponentInParent<ResourceNode>() : null;
                if (station != null) stationHit = station;
                else if (bench != null) benchHit = bench;
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
                TargetWorkbench = benchHit;
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
            // Tuş adları oyuncunun atamalarından gelir (Ayarlar → Tuşlar)
            string interactKey = LFInput.KeyLabel(LFInput.Interact);
            string dropKey = LFInput.KeyLabel(LFInput.Drop);
            string primaryKey = LFInput.KeyLabel(LFInput.Primary);

            if (TargetPickup != null)
            {
                var def = TargetPickup.Definition;
                return carry.HasRoomFor(TargetPickup)
                    ? $"[{interactKey}] Al · {def.displayName} ({Currency.Format(CareerState.SellPrice(def))})"
                    : $"Yük dolu ({carry.UsedSlots}/{carry.CapacitySlots}) · [{dropKey}] Bırak";
            }

            if (TargetStation != null)
            {
                if (!carry.IsCarrying) return "Satış Tezgâhı · Satmak için kaynak getir";
                int total = 0;
                foreach (var item in carry.Items) total += CareerState.SellPrice(item.Definition);
                return $"[{interactKey}] Sat · basılı tut: hepsi ({carry.Count} kaynak · {Currency.Format(total)})";
            }

            if (TargetWorkbench != null)
                return $"[{interactKey}] Alet Tezgâhı · Aletlerini geliştir";

            if (TargetNode != null)
            {
                if (carry.IsCarrying) return $"Elin dolu · [{dropKey}] Bırak";
                var tool = CareerState.Tool(ToolDefinition.KindFor(TargetNode.Kind));
                string toolName = tool != null ? $" · {tool.displayName}" : string.Empty;
                if (TargetNode.HoldDuration > 0f)
                    return $"[{primaryKey} basılı tut] {TargetNode.ActionVerb} · {TargetNode.DisplayName}{toolName}";
                string progress = TargetNode.MaxHits > 1 ? $" ({TargetNode.HitsTaken}/{TargetNode.MaxHits})" : string.Empty;
                return $"[{primaryKey}] {TargetNode.ActionVerb} · {TargetNode.DisplayName}{progress}{toolName}";
            }

            return carry.IsCarrying ? $"[{dropKey}] Bırak · Basılı tut: hepsini bırak" : string.Empty;
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
            if (TargetNode.HoldDuration > 0f) return; // basılı tutma UpdateHold'da işlenir
            if (Time.time < nextHitTime) return;

            float duration = EffectiveHitCooldown;
            nextHitTime = Time.time + duration;

            // Alet ele gelir ve savrulur; vuruş, alet hedefe değdiği anda gönderilir.
            float impactDelay = tools != null ? tools.BeginSwing(ToolDefinition.KindFor(TargetNode.Kind), duration) : 0f;
            pendingHitId = TargetNode.ObjectId;
            pendingHitKind = TargetNode.Kind;
            pendingHitTime = Time.time + impactDelay;
            UpdatePendingHit();
        }

        void UpdatePendingHit()
        {
            if (pendingHitId < 0 || Time.time < pendingHitTime) return;
            controller.Kick(pendingHitKind == NodeKind.Grass || pendingHitKind == NodeKind.Bush ? 1.2f : 2.5f);
            CmdHit(pendingHitId);
            pendingHitId = -1;
        }

        /// <summary>
        /// Basılı tutma süresi: "Seri Eller" yeteneği ve makasın kademesi (Terzi / Bahçe Makası) kısaltır.
        /// </summary>
        static float EffectiveHoldDuration(ResourceNode node)
        {
            var tool = CareerState.Tool(ToolDefinition.KindFor(node.Kind));
            float toolSpeed = tool != null && tool.kind == ToolKind.Shears ? tool.SpeedMultiplier : 1f;
            return node.HoldDuration / ((1f + CareerState.Stat(SkillStat.HitSpeed)) * toolSpeed);
        }

        void UpdateHold()
        {
            bool holding = primaryAction.IsPressed()
                           && TargetNode != null
                           && TargetNode.HoldDuration > 0f
                           && !carry.IsCarrying;

            if (!holding)
            {
                ResetHold();
                return;
            }

            if (holdNode != TargetNode)
            {
                // yeni öbeğe geçildi: sıfırdan başla (basılı tutmaya devam ederek peş peşe biçilebilir)
                holdNode = TargetNode;
                holdTime = 0f;
                nextHoldFeedback = 0f;
                CmdBeginHold(holdNode.ObjectId);
            }

            float duration = EffectiveHoldDuration(holdNode);
            tools?.SetWorking(ToolDefinition.KindFor(holdNode.Kind), true);
            holdTime += Time.deltaTime;
            HoldProgress = Mathf.Clamp01(holdTime / duration);

            // makas hissi: düzenli küçük sarsıntı ve çimen kıpırtısı
            if (holdTime >= nextHoldFeedback)
            {
                nextHoldFeedback = holdTime + 0.28f;
                controller.Kick(0.5f);
                holdNode.PlayHitFeedback();
            }

            if (holdTime >= duration)
            {
                controller.Kick(1.4f);
                CmdHit(holdNode.ObjectId);
                ResetHold();
            }
        }

        void ResetHold()
        {
            if (holdNode != null) tools?.SetWorking(ToolKind.Shears, false);
            holdNode = null;
            holdTime = 0f;
            HoldProgress = 0f;
        }

        void OnInteract()
        {
            if (TargetWorkbench != null)
            {
                WorkbenchRequested?.Invoke();
                return;
            }

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
                if (carry.IsCarrying) StartSellSession();
                else Notification?.Invoke("Satacak bir şey taşımıyorsun.");
            }
        }

        void StartSellSession()
        {
            sellHolding = true;
            sellInterval = sellFirstInterval;
            nextSellTime = Time.time + sellInterval;
            CmdSellOne();
        }

        void UpdateSellHold()
        {
            if (!sellHolding) return;
            if (!interactAction.IsPressed() || TargetStation == null || !carry.IsCarrying)
            {
                sellHolding = false;
                return;
            }

            if (Time.time >= nextSellTime)
            {
                CmdSellOne();
                sellInterval = Mathf.Max(sellMinInterval, sellInterval * sellAcceleration); // tık... tık.. tık.tık
                nextSellTime = Time.time + sellInterval;
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
        void CmdBeginHold(int objectId)
        {
            serverHoldId = objectId;
            serverHoldStart = NetworkTime.time;
        }

        [Command]
        void CmdHit(int objectId)
        {
            if (carry.IsCarrying) return;
            var world = PlotWorld.Instance;
            if (world == null || !world.TryGetNode(objectId, out var node)) return;
            if (!ServerInRange(node.transform.position)) return;

            if (node.HoldDuration > 0f)
            {
                // Basılı tutma süresi host'ta doğrulanır (gecikme payı ile).
                bool heldLongEnough = serverHoldId == objectId &&
                                      NetworkTime.time - serverHoldStart >= EffectiveHoldDuration(node) * 0.7f;
                serverHoldId = -1;
                if (!heldLongEnough) return;
            }
            else
            {
                if (NetworkTime.time < serverNextHitTime) return;
                serverNextHitTime = NetworkTime.time + EffectiveHitCooldown * 0.8f;
            }

            // Alet kademesi vuruş gücünü belirler (biçmede tek vuruş yeter).
            var tool = CareerState.Tool(ToolDefinition.KindFor(node.Kind));
            int damage = node.HoldDuration > 0f || tool == null ? 1 : tool.Damage;
            world.ServerApplyHit(objectId, transform.position, damage);
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

        // ------------------------------------------------------------ bırakma (fırlatma)

        [Command]
        void CmdDropTop()
        {
            var pickup = carry.ServerRemoveTop();
            if (pickup != null) ServerToss(pickup);
        }

        [Command]
        void CmdDropAll()
        {
            if (!serverDropAllRunning) StartCoroutine(ServerDropAllRoutine());
        }

        System.Collections.IEnumerator ServerDropAllRoutine()
        {
            // üstten başlayarak tek tek, kısa aralıklarla fırlat
            serverDropAllRunning = true;
            while (true)
            {
                var pickup = carry.ServerRemoveTop();
                if (pickup == null) break;
                ServerToss(pickup);
                yield return new WaitForSeconds(0.09f);
            }
            serverDropAllRunning = false;
        }

        [Server]
        void ServerToss(Pickup pickup)
        {
            Vector3 forward = transform.forward;
            Vector3 start = pickup.transform.position; // şu an elde durduğu yer
            if ((start - transform.position).sqrMagnitude > 9f)
                start = transform.position + forward * 0.8f + Vector3.up * 1.1f;

            Vector3 impulse = forward * UnityEngine.Random.Range(2.0f, 2.8f)
                              + Vector3.up * UnityEngine.Random.Range(1.6f, 2.3f)
                              + transform.right * UnityEngine.Random.Range(-0.5f, 0.5f);
            Vector3 spin = UnityEngine.Random.insideUnitSphere * 6f;
            pickup.ServerDrop(start, pickup.transform.rotation, impulse, spin, characterController);
        }

        // ------------------------------------------------------------ satış (tek tek)

        [Command]
        void CmdSellOne()
        {
            var station = SellStation.FindInRange(transform.position);
            var career = CareerState.Instance;
            if (station == null || career == null)
            {
                TargetNotify(connectionToClient, "Satış tezgâhına daha yakın olmalısın.");
                return;
            }

            var item = carry.ServerRemoveTop();
            if (item == null) return;

            int price = CareerState.SellPrice(item.Definition); // "Verim" satış bonusu dahil
            var t = item.transform;
            RpcSellFx(item.netId, item.Definition.id, t.position, t.rotation, t.localScale.x, station.IntakePosition, price);
            NetworkServer.Destroy(item.gameObject);
            StartCoroutine(AddMoneyDelayed(price));
        }

        System.Collections.IEnumerator AddMoneyDelayed(int amount)
        {
            yield return new WaitForSeconds(sellMoneyDelay); // kaynak tezgâha ulaştığında "tık"
            if (CareerState.Instance != null) CareerState.Instance.ServerAddMoney(amount);
        }

        [ClientRpc]
        void RpcSellFx(uint itemNetId, string definitionId, Vector3 position, Quaternion rotation, float scale, Vector3 target, int price)
        {
            // İstemcide nesne hâlâ duruyorsa kendi gördüğü konumdan uçsun (1. şahıs görünümü dahil).
            if (NetworkClient.spawned.TryGetValue(itemNetId, out var identity) && identity != null)
            {
                var t = identity.transform;
                position = t.position;
                rotation = t.rotation;
                scale = t.localScale.x;
                identity.gameObject.SetActive(false);
            }

            var definition = PlotWorld.Instance != null ? PlotWorld.Instance.FindDefinition(definitionId) : null;
            Gameplay.Effects.SellFlyEffect.Play(definition, position, rotation, scale, target, () =>
                Gameplay.Effects.FloatingText.Spawn("+" + Currency.Format(price), target + Vector3.up * 0.35f,
                    new Color(1f, 0.84f, 0.3f)));
        }

        [TargetRpc]
        void TargetNotify(NetworkConnectionToClient target, string message) => Notification?.Invoke(message);

#if UNITY_EDITOR
        public void EditorSetMask(LayerMask mask) => interactMask = mask;
#endif
    }
}
