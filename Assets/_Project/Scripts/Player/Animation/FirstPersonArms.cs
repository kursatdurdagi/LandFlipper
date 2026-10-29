using System.Collections.Generic;
using UnityEngine;

namespace LandFlipper.Player.Animation
{
    /// <summary>
    /// Birinci şahıs kol görünümü (sadece local oyuncu). Karakter modelinin kamera pivotuna bağlı
    /// ikinci bir kopyasıdır: baş, bacaklar ve sol kol gizlenir, gövde kameranın arkasında kalır.
    /// Yük taşınırken sağ el IK ile yığının altına uzanır, avuç içi yukarı bakar.
    /// </summary>
    [DefaultExecutionOrder(1100)] // PlayerCarry yığını yerleştirdikten sonra
    public class FirstPersonArms : MonoBehaviour
    {
        [SerializeField] FirstPersonController owner;
        [SerializeField] PlayerCarry carry;
        [Tooltip("Yığının dayandığı nokta (PlayerCarry'nin 1. şahıs çapası).")]
        [SerializeField] Transform gripAnchor;
        [Tooltip("Bilekten avuç ortasına mesafe (yük avucun ortasına otursun).")]
        [SerializeField] float palmOffset = 0.07f;
        [SerializeField] float palmThickness = 0.025f;
        [Tooltip("Parmakların sola (içe) dönme miktarı.")]
        [SerializeField] float fingerInward = 0.35f;

        Renderer[] renderers;
        Transform[] rigBones;
        Quaternion[] bindRotations;
        Transform upperArm, forearm, hand;
        Quaternion handBindLocal;
        bool initialized;
        bool visible;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            initialized = TryInitialize();
            visible = true;   // prefab'ta renderer'lar açık: zorla kapat
            SetVisible(false);
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

            var hips = Get("Hips");
            upperArm = Get("RightArm");
            forearm = Get("RightForeArm");
            hand = Get("RightHand");
            if (hips == null || upperArm == null || forearm == null || hand == null) return false;

            // Kamerada görünmemesi gereken parçaları çökert (mesh bu kemiklerde bir noktaya toplanır).
            foreach (var name in new[] { "Neck", "LeftUpLeg", "RightUpLeg", "LeftShoulder" })
            {
                var bone = Get(name);
                if (bone != null) bone.localScale = Vector3.one * 0.0001f;
            }

            rigBones = hips.GetComponentsInChildren<Transform>(true);
            bindRotations = new Quaternion[rigBones.Length];
            for (int i = 0; i < rigBones.Length; i++)
                bindRotations[i] = rigBones[i].localRotation;
            handBindLocal = hand.localRotation;
            return true;
        }

        void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            foreach (var r in renderers)
                if (r != null) r.enabled = value;
        }

        void LateUpdate()
        {
            bool show = initialized
                        && owner != null && owner.isLocalPlayer && !owner.IsThirdPerson
                        && carry != null && carry.IsCarrying
                        && gripAnchor != null;
            SetVisible(show);
            if (!show) return;

            for (int i = 0; i < rigBones.Length; i++)
                rigBones[i].localRotation = bindRotations[i];

            Vector3 up = gripAnchor.up;
            Vector3 right = gripAnchor.right;
            Vector3 forward = gripAnchor.forward;
            Vector3 fingers = (forward - right * fingerInward).normalized;

            // Yığının tabanı avucun ortasına otursun: bilek, avuç ortasının biraz gerisinde ve altında.
            // (Sallanma PlayerCarry'de çapanın kendisine uygulanır; el ve nesneler birlikte oynar.)
            Vector3 wrist = gripAnchor.position - fingers * palmOffset - up * palmThickness;
            Vector3 elbowHint = upperArm.position + right * 0.35f - up * 0.5f - forward * 0.1f;

            HandPose.Solve(upperArm, forearm, hand, handBindLocal, wrist, elbowHint, up, fingers);
        }

#if UNITY_EDITOR
        public void EditorSetup(FirstPersonController controller, PlayerCarry playerCarry, Transform anchor)
        {
            owner = controller;
            carry = playerCarry;
            gripAnchor = anchor;
        }
#endif
    }
}
