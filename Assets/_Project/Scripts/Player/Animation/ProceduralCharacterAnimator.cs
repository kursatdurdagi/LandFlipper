using System.Collections.Generic;
using UnityEngine;

namespace LandFlipper.Player.Animation
{
    /// <summary>
    /// Animasyon klibi olmayan Mixamo rig'i için tamamen prosedürel animasyon:
    /// bacaklar ve kollar iki kemik IK ile, kalça/omurga/baş ise doğrudan döndürmeyle sürülür.
    /// Hız, objenin kendi hareketinden okunur; bu yüzden local ve ağdan gelen
    /// (NetworkTransform) oyuncularda aynı şekilde çalışır.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class ProceduralCharacterAnimator : MonoBehaviour
    {
        [Header("Gait")]
        [SerializeField] float strideLength = 0.6f;
        [SerializeField] float stepHeight = 0.13f;
        [SerializeField] float hipBob = 0.035f;
        [SerializeField] float fullBlendSpeed = 1.2f;
        [SerializeField] float sprintSpeed = 7f;
        [SerializeField] float velocitySmoothing = 10f;
        [SerializeField] float kneeBendAtRest = 0.015f;

        [Header("Upper Body")]
        [SerializeField] float armSwing = 0.22f;
        [SerializeField] float armOutward = 0.07f;
        [SerializeField] float armReach = 0.9f;
        [SerializeField] float runLean = 6f;
        [SerializeField] float breathingAmount = 1.2f;
        [SerializeField] float maxLookPitch = 70f;

        /// <summary>Kamera eğimi (derece, + aşağı). Omurga ve baş bu yöne bakar.</summary>
        public float LookPitch { get; set; }

        /// <summary>Elde yük taşınıyorsa eller bu noktadaki yığının iki yanına uzanır.</summary>
        public bool IsHolding { get; set; }
        public Transform HoldTarget { get; set; }

        /// <summary>Animasyonun gözlemlenen yatay hızı (debug/test).</summary>
        public float CurrentSpeed { get; private set; }

        Transform hips, spine, spine1, spine2, neck, head;
        Limb leftLeg, rightLeg, leftArm, rightArm;

        Transform[] rigBones;
        Quaternion[] bindRotations;
        Vector3 hipsBindLocalPosition;

        Vector3 lastPosition;
        Vector3 smoothedVelocity;
        float phase;
        bool initialized;

        class Limb
        {
            public Transform Upper, Middle, End;
            public Vector3 EndRestLocal;        // kök uzayında ayak/el dinlenme konumu
            public Quaternion EndRestRelative;  // kök dönüşüne göre ayak dönüşü
            public float Length;
        }

        void Awake()
        {
            initialized = TryInitialize();
            if (!initialized)
            {
                Debug.LogWarning($"[LandFlipper] {name}: Mixamo kemikleri bulunamadı, prosedürel animasyon kapalı.", this);
                enabled = false;
                return;
            }
            lastPosition = transform.position;
        }

        bool TryInitialize()
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string key = t.name;
                int colon = key.LastIndexOf(':');
                if (colon >= 0) key = key.Substring(colon + 1);
                bones.TryAdd(key, t);
            }

            Transform Get(string n) => bones.TryGetValue(n, out var t) ? t : null;

            hips = Get("Hips");
            spine = Get("Spine");
            spine1 = Get("Spine1");
            spine2 = Get("Spine2");
            neck = Get("Neck");
            head = Get("Head");
            leftLeg = MakeLimb(Get("LeftUpLeg"), Get("LeftLeg"), Get("LeftFoot"));
            rightLeg = MakeLimb(Get("RightUpLeg"), Get("RightLeg"), Get("RightFoot"));
            leftArm = MakeLimb(Get("LeftArm"), Get("LeftForeArm"), Get("LeftHand"));
            rightArm = MakeLimb(Get("RightArm"), Get("RightForeArm"), Get("RightHand"));

            if (hips == null || spine == null || spine2 == null || head == null ||
                leftLeg == null || rightLeg == null || leftArm == null || rightArm == null)
                return false;

            rigBones = hips.GetComponentsInChildren<Transform>(true);
            bindRotations = new Quaternion[rigBones.Length];
            for (int i = 0; i < rigBones.Length; i++)
                bindRotations[i] = rigBones[i].localRotation;
            hipsBindLocalPosition = hips.localPosition;
            return true;
        }

        Limb MakeLimb(Transform upper, Transform middle, Transform end)
        {
            if (upper == null || middle == null || end == null) return null;
            return new Limb
            {
                Upper = upper,
                Middle = middle,
                End = end,
                EndRestLocal = transform.InverseTransformPoint(end.position),
                EndRestRelative = Quaternion.Inverse(transform.rotation) * end.rotation,
                Length = Vector3.Distance(upper.position, middle.position) + Vector3.Distance(middle.position, end.position)
            };
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return; // oyun duraklatılmışsa son pozu koru

            UpdateLocomotionState(dt);
            RestoreBindPose();

            float blend = Mathf.Clamp01(CurrentSpeed / fullBlendSpeed);
            Vector3 moveDirection = CurrentSpeed > 0.05f ? smoothedVelocity / CurrentSpeed : transform.forward;
            float stride = strideLength * Mathf.Lerp(0.8f, 1.6f, Mathf.Clamp01(CurrentSpeed / sprintSpeed));

            ApplyHips(blend);
            ApplySpineAndLook(blend);
            SolveLeg(leftLeg, phase, moveDirection, stride, blend);
            SolveLeg(rightLeg, phase + Mathf.PI, moveDirection, stride, blend);
            SolveArm(leftArm, -1f, phase + Mathf.PI, blend);   // sol kol sağ bacakla birlikte ileri
            SolveArm(rightArm, 1f, phase, blend);
        }

        void UpdateLocomotionState(float dt)
        {
            Vector3 velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            velocity.y = 0f;
            if (velocity.sqrMagnitude > 400f) velocity = Vector3.zero; // ışınlanma/spawn

            smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, 1f - Mathf.Exp(-velocitySmoothing * dt));
            CurrentSpeed = smoothedVelocity.magnitude;

            float stride = strideLength * Mathf.Lerp(0.8f, 1.6f, Mathf.Clamp01(CurrentSpeed / sprintSpeed));
            if (CurrentSpeed > 0.05f)
            {
                phase += CurrentSpeed * Mathf.PI / stride * dt;
            }
            else
            {
                // Durunca adımı en yakın "iki ayak yerde" fazına yumuşakça getir.
                float rest = Mathf.Round(phase / Mathf.PI) * Mathf.PI;
                phase = Mathf.MoveTowards(phase, rest, dt * 4f);
            }
            if (phase > Mathf.PI * 200f) phase -= Mathf.PI * 200f;
        }

        void RestoreBindPose()
        {
            for (int i = 0; i < rigBones.Length; i++)
                rigBones[i].localRotation = bindRotations[i];
            hips.localPosition = hipsBindLocalPosition;
        }

        void ApplyHips(float blend)
        {
            float bob = (1f - Mathf.Cos(2f * phase)) * 0.5f * hipBob * blend;
            hips.position -= transform.up * (bob + kneeBendAtRest);

            // Yürürken kalçanın hafif yana dönmesi
            float twist = Mathf.Sin(phase) * 5f * blend;
            hips.rotation = Quaternion.AngleAxis(twist, transform.up) * hips.rotation;
        }

        void ApplySpineAndLook(float blend)
        {
            float pitch = Mathf.Clamp(LookPitch, -maxLookPitch, maxLookPitch);
            float breathing = Mathf.Sin(Time.time * 1.6f) * breathingAmount * (1f - blend);
            float counterTwist = -Mathf.Sin(phase) * 5f * blend;
            Vector3 right = transform.right;

            Rotate(spine, right, runLean * Mathf.Clamp01(CurrentSpeed / sprintSpeed) + breathing * 0.5f);
            spine.rotation = Quaternion.AngleAxis(counterTwist, transform.up) * spine.rotation;
            Rotate(spine1, right, pitch * 0.2f);
            Rotate(spine2, right, pitch * 0.25f + breathing);
            Rotate(neck, right, pitch * 0.2f);
            Rotate(head, right, pitch * 0.35f);
        }

        static void Rotate(Transform bone, Vector3 axis, float degrees)
        {
            if (bone == null || Mathf.Approximately(degrees, 0f)) return;
            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        void SolveLeg(Limb leg, float legPhase, Vector3 moveDirection, float stride, float blend)
        {
            float swing = Mathf.Sin(legPhase);
            float lift = Mathf.Max(0f, Mathf.Cos(legPhase));

            Vector3 restPosition = transform.TransformPoint(leg.EndRestLocal);
            Vector3 target = restPosition
                             + moveDirection * (swing * stride * 0.5f * blend)
                             + transform.up * (lift * stepHeight * blend);

            Vector3 kneeHint = leg.Upper.position + transform.forward + moveDirection * 0.3f;
            TwoBoneIK.Solve(leg.Upper, leg.Middle, leg.End, target, kneeHint);

            // Ayak tabanı yere paralel, adım atarken parmak ucu hafif yukarı
            float toeLift = lift * 18f * blend;
            leg.End.rotation = Quaternion.AngleAxis(-toeLift, transform.right) * (transform.rotation * leg.EndRestRelative);
        }

        void SolveArm(Limb arm, float side, float armPhase, float blend)
        {
            Vector3 shoulder = arm.Upper.position;

            if (IsHolding && HoldTarget != null)
            {
                // Yığının en altını iki yandan kavra
                Vector3 grip = HoldTarget.position + transform.right * (side * 0.24f) + HoldTarget.up * 0.08f;
                Vector3 hint = shoulder + transform.right * (side * 0.5f) - transform.up * 0.4f;
                TwoBoneIK.Solve(arm.Upper, arm.Middle, arm.End, grip, hint);
                return;
            }

            float swing = Mathf.Sin(armPhase) * armSwing * blend;

            Vector3 target = shoulder
                             - transform.up * (arm.Length * armReach)
                             + transform.forward * (0.04f + swing)
                             + transform.right * (side * armOutward);

            Vector3 elbowHint = shoulder
                                - transform.forward * 0.6f
                                + transform.right * (side * 0.3f)
                                - transform.up * 0.3f;

            TwoBoneIK.Solve(arm.Upper, arm.Middle, arm.End, target, elbowHint);
        }
    }
}
