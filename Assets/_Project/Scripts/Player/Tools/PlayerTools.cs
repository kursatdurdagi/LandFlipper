using System;
using LandFlipper.Gameplay;
using LandFlipper.Player.Animation;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace LandFlipper.Player.Tools
{
    /// <summary>
    /// Elde görünen aletler (makas / balta / çekiç) ve kullanma animasyonları.
    /// Alet, oyuncu bir işe başlayınca ele gelir, bir süre kullanılmazsa iner.
    /// Animasyon prosedüreldir: aletin tutma noktası (sap) bir poz eğrisinde hareket eder,
    /// sağ kol IK ile sapa uzanır. Aynı poz 1. şahıs kolunda ve gövdede (diğer oyuncular / 3. şahıs) kullanılır.
    /// Ağ: "elde hangi alet" ve "çalışıyor mu" SyncVar; savurmalar Rpc ile diğerlerine gider.
    /// </summary>
    [DefaultExecutionOrder(1200)] // gövde animatörü (1000) ve 1. şahıs kolu (1100) sonrasında
    public class PlayerTools : NetworkBehaviour
    {
        public static PlayerTools Local { get; private set; }

        /// <summary>Local oyuncuya gösterilecek hata/bilgi mesajı.</summary>
        public static event Action<string> LocalNotification;

        /// <summary>Savurmanın hangi anında alet hedefe değer (0-1).</summary>
        public const float ImpactAt = 0.46f;

        [SerializeField] ProceduralCharacterAnimator bodyAnimator;
        [SerializeField] FirstPersonArms firstPersonArms;
        [SerializeField] Transform cameraPivot;

        [Header("Davranış")]
        [Tooltip("Alet kullanılmadan bu kadar saniye geçince indirilir.")]
        [SerializeField] float idleHideDelay = 1.8f;
        [Tooltip("Ele alma / indirme hızı (1/sn).")]
        [SerializeField] float equipSpeed = 6f;
        [Tooltip("Makas aç-kapa süresi (sn). PlayerInteractor'daki kesme ritmiyle aynı.")]
        [SerializeField] float snipInterval = 0.28f;

        [Header("Kavrama")]
        [Tooltip("Parmak eklemlerinin kıvrılma ekseni (kemik yerel uzayı) ve açısı.")]
        [SerializeField] Vector3 fingerCurlAxis = Vector3.right;
        [SerializeField] float fingerCurl = 62f;
        [SerializeField] Vector3 thumbCurlAxis = Vector3.forward;
        [SerializeField] float thumbCurl = 25f;

        [Header("1. Şahıs")]
        [Header("Ses")]
        [SerializeField] SfxLibrary sfx;

        [Tooltip("Makası kendi ekseni etrafında çevirir (ağız düzlemi kameraya dönsün).")]
        [SerializeField] float shearsRoll = 0f;
        [Tooltip("Aletin omza göre ek kaydırması (kamera uzayı, m).")]
        [SerializeField] Vector3 firstPersonOffset = new Vector3(-0.04f, 0.24f, 0.06f);

        [SyncVar] sbyte syncedKind = -1;
        [SyncVar] bool syncedWorking;

        FirstPersonController controller;
        PlayerCarry carry;

        // owner girdisi
        int localKind = -1;
        bool localWorking;
        float lastUseTime;
        sbyte sentKind = -1;
        bool sentWorking;

        // görsel durum (herkeste)
        int shownKind = -1;
        float equipBlend;
        float workBlend;
        float swingStart = -10f;
        SfxKind swingSfx;
        bool impactPlayed = true;
        int lastSnip;
        float swingDuration = 0.45f;
        float snipPhase;

        class Slot
        {
            public ToolDefinition Definition;
            public GameObject Instance;
            public Renderer[] Renderers;
            public ToolVisual Visual;
        }

        readonly Slot[] bodySlots = { new Slot(), new Slot(), new Slot() };
        readonly Slot[] viewSlots = { new Slot(), new Slot(), new Slot() };

        /// <summary>Elde olan (ya da gelmekte olan) alet; yoksa -1.</summary>
        public int ActiveKind => isLocalPlayer ? localKind : syncedKind;
        bool Working => isLocalPlayer ? localWorking : syncedWorking;
        public bool IsSwinging => Time.time < swingStart + swingDuration;

        void Awake()
        {
            controller = GetComponent<FirstPersonController>();
            carry = GetComponent<PlayerCarry>();
        }

        public override void OnStartLocalPlayer() => Local = this;

        public override void OnStopLocalPlayer()
        {
            if (Local == this) Local = null;
        }

        // ------------------------------------------------------------ owner API

        /// <summary>Balta/çekiç savurması ya da makasla tek kesiş başlatır. Darbe anına kalan süreyi döner.</summary>
        public float BeginSwing(ToolKind kind, float duration, SfxKind sound = SfxKind.None)
        {
            Equip(kind);
            swingStart = Time.time;
            swingDuration = Mathf.Max(0.15f, duration);
            lastUseTime = Time.time;
            swingSfx = sound != SfxKind.None ? sound : DefaultSfx(kind);
            impactPlayed = false;
            if (isLocalPlayer) CmdSwing((byte)kind, swingDuration, (byte)swingSfx);
            return swingDuration * ImpactAt;
        }

        /// <summary>Basılı tutarak çalışma (makasla çim biçme).</summary>
        public void SetWorking(ToolKind kind, bool working)
        {
            if (working)
            {
                Equip(kind);
                localWorking = true;
                lastUseTime = Time.time;
            }
            else if (localWorking)
            {
                localWorking = false;
                lastUseTime = Time.time;
            }
            PushState();
        }

        void Equip(ToolKind kind)
        {
            localKind = (int)kind;
            PushState();
        }

        void PushState()
        {
            if (!isLocalPlayer) return;
            if ((sbyte)localKind == sentKind && localWorking == sentWorking) return;
            sentKind = (sbyte)localKind;
            sentWorking = localWorking;
            CmdSetState(sentKind, sentWorking);
        }

        public void RequestUpgrade(ToolKind kind)
        {
            var career = CareerState.Instance;
            if (career == null) return;
            if (!career.CanUpgradeTool(kind, out string reason))
            {
                LocalNotification?.Invoke(reason);
                return;
            }
            CmdUpgrade((byte)kind);
        }

        // ------------------------------------------------------------ ağ

        [Command]
        void CmdSetState(sbyte kind, bool working)
        {
            syncedKind = kind;
            syncedWorking = working;
        }

        [Command]
        void CmdSwing(byte kind, float duration, byte sound)
        {
            syncedKind = (sbyte)kind;
            RpcSwing(kind, Mathf.Clamp(duration, 0.15f, 2f), sound);
        }

        [ClientRpc(includeOwner = false)]
        void RpcSwing(byte kind, float duration, byte sound)
        {
            swingStart = Time.time;
            swingDuration = duration;
            swingSfx = (SfxKind)sound;
            impactPlayed = false;
        }

        static SfxKind DefaultSfx(ToolKind kind) => kind switch
        {
            ToolKind.Axe => SfxKind.Wood,
            ToolKind.Hammer => SfxKind.Rock,
            _ => SfxKind.Snip
        };

        // ------------------------------------------------------------ ses (animasyonla aynı karede)

        void UpdateSfx()
        {
            // basılı tutarak biçme: ağızlar her kapandığında (faz x.5) bir "şık"
            if (Working && shownKind == (int)ToolKind.Shears && equipBlend > 0.6f)
            {
                int snip = Mathf.FloorToInt(snipPhase - SnipSoundPhase);
                if (snip != lastSnip)
                {
                    lastSnip = snip;
                    PlaySfx(SfxKind.Snip, 0.85f);
                }
            }
            else lastSnip = Mathf.FloorToInt(snipPhase - SnipSoundPhase);

            // savurma: alet hedefe değdiği an (pozun darbe karesi)
            if (!impactPlayed && swingDuration > 0f)
            {
                float t = (Time.time - swingStart) / swingDuration;
                float at = shownKind == (int)ToolKind.Shears ? ImpactAt * 0.8f : ImpactAt + HeavyContactLag;
                if (t >= at)
                {
                    impactPlayed = true;
                    if (t < 1f) PlaySfx(swingSfx, 1f);
                }
            }
        }

        // ses dosyalarının tepe noktası ~8-25 ms sonra: kapanmadan hemen önce başlat
        const float SnipSoundPhase = 0.45f;
        // ağır alet pozu darbe pozuna ImpactAt + 0.04'te ulaşır
        const float HeavyContactLag = 0.035f;

        void PlaySfx(SfxKind kind, float volume)
        {
            if (sfx == null || kind == SfxKind.None) return;
            bool local = isLocalPlayer;
            Vector3 pos = transform.position + Vector3.up * 1.2f + transform.forward * 0.6f;
            sfx.Play(kind, pos, local ? 0.35f : 1f, volume);
        }

        [Command]
        void CmdUpgrade(byte kind)
        {
            var career = CareerState.Instance;
            if (career == null) return;
            if (Workbench.FindInRange(transform.position) == null)
            {
                TargetNotify(connectionToClient, "Alet tezgâhına yakın olmalısın.");
                return;
            }
            if (!career.ServerTryUpgradeTool((ToolKind)kind, out string message))
                TargetNotify(connectionToClient, message);
        }

        [TargetRpc]
        void TargetNotify(NetworkConnectionToClient target, string message) => LocalNotification?.Invoke(message);

        // ------------------------------------------------------------ durum

        void Update()
        {
            float dt = Time.deltaTime;

            if (isLocalPlayer)
            {
                bool carrying = carry != null && carry.IsCarrying;
                bool idle = !localWorking && !IsSwinging && Time.time - lastUseTime > idleHideDelay;
                if (localKind >= 0 && (carrying || idle))
                {
                    localKind = -1;
                    localWorking = false;
                }
                PushState();
            }

            int target = ActiveKind;
            if (target != shownKind)
            {
                // önce eldekini indir, sonra yenisini kaldır
                equipBlend = Mathf.MoveTowards(equipBlend, 0f, dt * equipSpeed * 1.5f);
                if (equipBlend <= 0f) shownKind = target;
            }
            else if (shownKind >= 0)
            {
                equipBlend = Mathf.MoveTowards(equipBlend, 1f, dt * equipSpeed);
            }

            workBlend = Mathf.MoveTowards(workBlend, Working ? 1f : 0f, dt * 8f);
            if (Working) snipPhase += dt / snipInterval;
            UpdateSfx();

            if (firstPersonArms != null)
                firstPersonArms.ToolMode = isLocalPlayer && shownKind >= 0;
        }

        // ------------------------------------------------------------ görsel

        void LateUpdate()
        {
            if (shownKind < 0)
            {
                HideAll(bodySlots);
                HideAll(viewSlots);
                return;
            }

            var kind = (ToolKind)shownKind;
            var definition = CareerState.Tool(kind);
            EvaluatePose(kind, out Vector3 localPos, out Quaternion localRot, out float open);

            // --- gövde (diğer oyuncular, 3. şahıs, local'de gölge)
            var bodySlot = Prepare(bodySlots, shownKind, definition, false);
            HideOthers(bodySlots, shownKind);
            if (bodySlot != null && bodyAnimator != null && bodyAnimator.isActiveAndEnabled &&
                bodyAnimator.TryGetRightArm(out var bu, out var bm, out var be, out var bBind))
            {
                Quaternion frame = Quaternion.AngleAxis(bodyAnimator.LookPitch * 0.5f, transform.right) * transform.rotation;
                PlaceAndSolve(bodySlot, definition, bu.position, frame, localPos, localRot, open, bu, bm, be, bBind);

                bool localFirstPerson = isLocalPlayer && controller != null && !controller.IsThirdPerson;
                var mode = localFirstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                foreach (var r in bodySlot.Renderers)
                    if (r != null) r.shadowCastingMode = mode;
            }
            else if (bodySlot?.Instance != null) bodySlot.Instance.SetActive(false);

            // --- 1. şahıs kolu (sadece local)
            bool firstPerson = isLocalPlayer && controller != null && !controller.IsThirdPerson &&
                               firstPersonArms != null && firstPersonArms.IsShown && cameraPivot != null;
            if (firstPerson && firstPersonArms.TryGetArm(out var vu, out var vm, out var ve, out var vBind))
            {
                var viewSlot = Prepare(viewSlots, shownKind, definition, true);
                HideOthers(viewSlots, shownKind);
                if (viewSlot != null)
                {
                    Quaternion frame = cameraPivot.rotation;
                    Vector3 origin = vu.position + frame * firstPersonOffset;
                    PlaceAndSolve(viewSlot, definition, origin, frame, localPos, localRot, open, vu, vm, ve, vBind);
                }
            }
            else HideAll(viewSlots);
        }

        void PlaceAndSolve(Slot slot, ToolDefinition definition, Vector3 origin, Quaternion frame,
            Vector3 localPos, Quaternion localRot, float open,
            Transform upper, Transform middle, Transform hand, Quaternion handBind)
        {
            Vector3 gripPos = origin + frame * localPos;
            Quaternion gripRot = frame * localRot;

            // Sağ el sapı kavrar: avuç içi aletin soluna bakar, parmaklar vuran yüze doğru sarılır.
            Vector3 palm = gripRot * Vector3.left;
            Vector3 fingers = gripRot * Vector3.forward;
            Vector3 wrist = gripPos - fingers * GripFingerOffset - palm * GripPalmOffset;

            // Kol boyundan uzağa uzanma: kol gerilip el aletten kopmasın
            float reach = (Vector3.Distance(upper.position, middle.position) + Vector3.Distance(middle.position, hand.position)) * 0.97f;
            Vector3 toWrist = wrist - upper.position;
            if (toWrist.magnitude > reach) wrist = upper.position + toWrist.normalized * reach;

            Vector3 elbowHint = upper.position + frame * new Vector3(0.45f, -0.55f, -0.2f);
            HandPose.Solve(upper, middle, hand, handBind, wrist, elbowHint, palm, fingers);
            CurlFingers(hand);

            // Alet, hesaplanan noktaya değil elin GERÇEK pozuna bağlanır: el ile alet arasında kayma olmaz.
            Quaternion actualGrip = hand.rotation * HandToGrip;
            Vector3 actualGripPos = hand.position + actualGrip * (Vector3.forward * GripFingerOffset + Vector3.left * GripPalmOffset);

            var t = slot.Instance.transform;
            Vector3 offset = definition != null ? definition.gripPositionOffset : Vector3.zero;
            Quaternion euler = definition != null ? Quaternion.Euler(definition.gripEulerOffset) : Quaternion.identity;
            t.SetPositionAndRotation(actualGripPos + actualGrip * offset, actualGrip * euler);
            if (!slot.Instance.activeSelf) slot.Instance.SetActive(true);
            if (slot.Visual != null) slot.Visual.SetOpen(open);
        }

        const float GripFingerOffset = 0.07f;
        const float GripPalmOffset = 0.03f;
        // el dönüşü = LookRotation(avuç, parmak); tutma dönüşü = el * bunun tersi
        static readonly Quaternion HandToGrip = Quaternion.Inverse(Quaternion.LookRotation(Vector3.left, Vector3.forward));

        // Parmaklar sapı sarar (animatörler her karede bind pozuna döndürdüğü için üstüne eklenir).
        readonly System.Collections.Generic.Dictionary<Transform, Transform[]> fingerCache =
            new System.Collections.Generic.Dictionary<Transform, Transform[]>();

        void CurlFingers(Transform hand)
        {
            if (!fingerCache.TryGetValue(hand, out var joints))
            {
                var list = new System.Collections.Generic.List<Transform>();
                foreach (var t in hand.GetComponentsInChildren<Transform>(true))
                {
                    if (t == hand || t.name.EndsWith("4")) continue; // uç noktalar
                    if (t.name.Contains("Thumb") || t.name.Contains("Index") || t.name.Contains("Middle") ||
                        t.name.Contains("Ring") || t.name.Contains("Pinky"))
                        list.Add(t);
                }
                joints = list.ToArray();
                fingerCache[hand] = joints;
            }

            foreach (var joint in joints)
            {
                bool thumb = joint.name.Contains("Thumb");
                joint.localRotation *= Quaternion.AngleAxis(thumb ? thumbCurl : fingerCurl, thumb ? thumbCurlAxis : fingerCurlAxis);
            }
        }

        // ------------------------------------------------------------ poz eğrisi

        // Omuz uzayında (x sağ, y yukarı, z ileri) aletin tutma noktası ve yönü.
        // Alet: +Y başı/ucu, +Z vuran yüz. Pozitif X açısı aletin başını öne yatırır.
        static readonly Vector3 LoweredPos = new Vector3(0.06f, -0.42f, 0.12f);
        static readonly Vector3 LoweredEuler = new Vector3(35f, 0f, -10f);

        static readonly Vector3 HeavyIdlePos = new Vector3(0.02f, -0.22f, 0.3f);
        static readonly Vector3 HeavyIdleEuler = new Vector3(18f, -6f, -14f);
        static readonly Vector3 HeavyWindupPos = new Vector3(0.06f, 0.02f, 0.18f);
        static readonly Vector3 HeavyWindupEuler = new Vector3(-30f, -4f, -10f);
        static readonly Vector3 HeavyImpactPos = new Vector3(-0.04f, -0.18f, 0.36f);
        static readonly Vector3 HeavyImpactEuler = new Vector3(80f, -4f, -6f);

        static readonly Vector3 ShearsIdlePos = new Vector3(-0.02f, -0.15f, 0.3f);
        static readonly Vector3 ShearsIdleEuler = new Vector3(25f, -10f, 15f);
        static readonly Vector3 ShearsWorkPos = new Vector3(-0.06f, -0.17f, 0.33f);
        static readonly Vector3 ShearsWorkEuler = new Vector3(40f, -15f, 20f);

        void EvaluatePose(ToolKind kind, out Vector3 pos, out Quaternion rot, out float open)
        {
            float swingT = IsSwinging ? (Time.time - swingStart) / swingDuration : -1f;
            Vector3 p;
            Vector3 e;
            open = 0.15f;

            if (kind == ToolKind.Shears)
            {
                p = Vector3.Lerp(ShearsIdlePos, ShearsWorkPos, Smooth(workBlend));
                e = Vector3.Lerp(ShearsIdleEuler, ShearsWorkEuler, Smooth(workBlend));

                if (workBlend > 0f)
                {
                    // makas ritmi: aç-kapa ve her kesişte hafif ileri itme
                    float c = Mathf.Cos(snipPhase * Mathf.PI * 2f);
                    open = Mathf.Lerp(open, 0.5f + 0.5f * c, workBlend);
                    p.z += (0.5f - 0.5f * c) * 0.012f * workBlend;
                    e.x += (0.5f - 0.5f * c) * 3f * workBlend;
                }

                if (swingT >= 0f)
                {
                    // tek kesiş (çalı): öne uzan, açık ağızla kapat, geri çek
                    float reach = swingT < ImpactAt ? Smooth(swingT / ImpactAt) : 1f - Smooth((swingT - ImpactAt) / (1f - ImpactAt));
                    p = Vector3.Lerp(p, ShearsWorkPos + new Vector3(0f, 0.05f, 0.04f), reach);
                    e = Vector3.Lerp(e, ShearsWorkEuler - new Vector3(15f, 0f, 0f), reach);
                    open = swingT < ImpactAt * 0.8f ? 1f : 0f;
                }
            }
            else
            {
                p = HeavyIdlePos;
                e = HeavyIdleEuler;
                if (swingT >= 0f)
                {
                    const float windupEnd = 0.36f;
                    float impactEnd = ImpactAt + 0.04f;
                    const float holdEnd = 0.62f;

                    if (swingT < windupEnd)
                    {
                        float k = EaseOut(swingT / windupEnd);
                        p = Vector3.Lerp(HeavyIdlePos, HeavyWindupPos, k);
                        e = Vector3.Lerp(HeavyIdleEuler, HeavyWindupEuler, k);
                    }
                    else if (swingT < impactEnd)
                    {
                        float k = EaseIn((swingT - windupEnd) / (impactEnd - windupEnd));
                        p = Vector3.Lerp(HeavyWindupPos, HeavyImpactPos, k);
                        e = Vector3.Lerp(HeavyWindupEuler, HeavyImpactEuler, k);
                    }
                    else if (swingT < holdEnd)
                    {
                        // darbe: hafif geri tepme
                        float k = (swingT - impactEnd) / (holdEnd - impactEnd);
                        p = HeavyImpactPos + new Vector3(0f, 0.02f, -0.015f) * Mathf.Sin(k * Mathf.PI);
                        e = HeavyImpactEuler - new Vector3(6f, 0f, 0f) * Mathf.Sin(k * Mathf.PI);
                    }
                    else
                    {
                        float k = Smooth((swingT - holdEnd) / (1f - holdEnd));
                        p = Vector3.Lerp(HeavyImpactPos, HeavyIdlePos, k);
                        e = Vector3.Lerp(HeavyImpactEuler, HeavyIdleEuler, k);
                    }
                }
            }

            // ele alma / indirme
            float b = Smooth(equipBlend);
            pos = Vector3.Lerp(LoweredPos, p, b);
            rot = Quaternion.Slerp(Quaternion.Euler(LoweredEuler), Quaternion.Euler(e), b);
            // makas düzlemi kameraya dönük dursun (ağız açılıp kapanırken görünsün)
            if (kind == ToolKind.Shears) rot *= Quaternion.Euler(0f, shearsRoll, 0f);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
        }

        static float EaseIn(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        // ------------------------------------------------------------ örnekler

        Slot Prepare(Slot[] slots, int kind, ToolDefinition definition, bool view)
        {
            var slot = slots[kind];
            if (slot.Definition == definition && slot.Instance != null) return slot;

            if (slot.Instance != null) Destroy(slot.Instance);
            slot.Instance = null;
            slot.Definition = definition;
            if (definition == null || definition.visualPrefab == null) return null;

            var go = Instantiate(definition.visualPrefab, transform);
            go.name = (view ? "ViewTool_" : "BodyTool_") + definition.id;
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Destroy(col);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            slot.Renderers = go.GetComponentsInChildren<Renderer>(true);
            if (view)
                foreach (var r in slot.Renderers) r.shadowCastingMode = ShadowCastingMode.Off;
            slot.Visual = go.GetComponent<ToolVisual>();
            slot.Instance = go;
            go.SetActive(false);
            return slot;
        }

        static void HideAll(Slot[] slots)
        {
            foreach (var s in slots)
                if (s.Instance != null && s.Instance.activeSelf) s.Instance.SetActive(false);
        }

        static void HideOthers(Slot[] slots, int keep)
        {
            for (int i = 0; i < slots.Length; i++)
                if (i != keep && slots[i].Instance != null && slots[i].Instance.activeSelf) slots[i].Instance.SetActive(false);
        }

#if UNITY_EDITOR
        public void EditorSetup(ProceduralCharacterAnimator body, FirstPersonArms arms, Transform pivot)
        {
            bodyAnimator = body;
            firstPersonArms = arms;
            cameraPivot = pivot;
        }

        public void EditorSetSfx(SfxLibrary library) => sfx = library;
#endif
    }
}
