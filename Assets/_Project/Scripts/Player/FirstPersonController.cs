using LandFlipper.Core;
using LandFlipper.Gameplay;
using LandFlipper.Player.Animation;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace LandFlipper.Player
{
    /// <summary>
    /// Ağ uyumlu birinci şahıs kontrolcü. Hareket local oyuncuda hesaplanır ve
    /// NetworkTransform (ClientToServer) ile diğerlerine gönderilir. Bakış eğimi
    /// SyncVar ile paylaşılır; diğer oyuncular karakterin nereye baktığını görür.
    /// Girdi LFInput'tan (değiştirilebilir tuşlar), hassasiyet/FOV SettingsService'ten gelir.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] Transform cameraPivot;
        [SerializeField] Camera playerCamera;
        [SerializeField] ProceduralCharacterAnimator characterAnimator;
        [Tooltip("Local oyuncunun kendi gövdesi: birinci şahısta sadece gölge olarak çizilir.")]
        [SerializeField] Renderer[] bodyRenderers;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 4.5f;
        [SerializeField] float sprintSpeed = 7f;
        [SerializeField] float acceleration = 12f;
        [SerializeField] float jumpHeight = 1.1f;
        [SerializeField] float gravity = -20f;

        [Header("Look")]
        [SerializeField] float minPitch = -85f;
        [SerializeField] float maxPitch = 85f;

        [Header("Debug View")]
        [Tooltip("V tuşu ile geçilen üçüncü şahıs kamera ofseti.")]
        [SerializeField] Vector3 thirdPersonOffset = new Vector3(0.5f, 0.2f, -2.8f);

        [SyncVar] float syncedPitch;

        CharacterController characterController;
        bool inputReady;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float pitch;
        float kick;
        bool thirdPerson;

        public Camera PlayerCamera => playerCamera;
        public Transform CameraPivot => cameraPivot;

        /// <summary>Vuruş anında kameraya küçük bir sarsıntı verir (sadece local).</summary>
        public void Kick(float degrees) => kick = Mathf.Min(kick + degrees, 6f);
        public bool IsThirdPerson => thirdPerson;

        void Awake()
        {
            characterController = GetComponent<CharacterController>();
            SetCameraActive(false);
        }

        public override void OnStartLocalPlayer()
        {
            SetCameraActive(true);
            SetThirdPerson(false);

            LFInput.EnsureCreated();
            inputReady = true;

            SettingsService.Changed += ApplySettings;
            ApplySettings();

            if (!InputGate.IsBlocked)
                CursorUtility.SetLocked(true);
        }

        public override void OnStopLocalPlayer()
        {
            inputReady = false;
            SettingsService.Changed -= ApplySettings;
            CursorUtility.SetLocked(false);
        }

        void ApplySettings() => SettingsService.ApplyToCamera(playerCamera, true);

        void SetCameraActive(bool active)
        {
            if (playerCamera == null) return;
            playerCamera.enabled = active;
            var listener = playerCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;
        }

        void SetThirdPerson(bool enabled)
        {
            thirdPerson = enabled;
            if (playerCamera != null)
                playerCamera.transform.localPosition = enabled ? thirdPersonOffset : Vector3.zero;

            var mode = enabled ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (var r in bodyRenderers)
                if (r != null) r.shadowCastingMode = mode;
        }

        void Update()
        {
            if (isLocalPlayer && inputReady)
            {
                bool blocked = InputGate.IsBlocked;
                Vector2 moveInput = blocked ? Vector2.zero : LFInput.Move.ReadValue<Vector2>();
                bool sprint = !blocked && LFInput.Sprint.IsPressed();
                bool jump = !blocked && LFInput.Jump.WasPressedThisFrame();

                if (!blocked)
                {
                    HandleLook();
                    if (LFInput.ToggleView.WasPressedThisFrame())
                        SetThirdPerson(!thirdPerson);
                }

                HandleMovement(moveInput, sprint, jump);

                if (Mathf.Abs(syncedPitch - pitch) > 0.5f)
                    syncedPitch = pitch;
            }

            // Pivot: local'de bakış + vuruş sarsıntısı, diğer oyuncularda ağdan gelen eğim
            // (taşınan yük de bu pivota bağlı olduğu için herkeste doğru yönde görünür).
            kick = Mathf.Lerp(kick, 0f, 1f - Mathf.Exp(-14f * Time.deltaTime));
            float viewPitch = isLocalPlayer ? pitch - kick : syncedPitch;
            cameraPivot.localRotation = Quaternion.Euler(viewPitch, 0f, 0f);

            if (characterAnimator != null)
                characterAnimator.LookPitch = isLocalPlayer ? pitch : syncedPitch;
        }

        void HandleLook()
        {
            Vector2 raw = LFInput.Look.ReadValue<Vector2>();
            if (raw == Vector2.zero) return;

            var settings = SettingsService.Current;
            bool isPointer = LFInput.Look.activeControl?.device is Pointer;
            Vector2 delta = isPointer
                ? raw * settings.mouseSensitivity
                : raw * (settings.gamepadSensitivity * Time.deltaTime);
            if (settings.invertY) delta.y = -delta.y;

            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
        }

        void HandleMovement(Vector2 input, bool sprint, bool jump)
        {
            // "Hareket" yetenekleri: genel hız ve koşma hızı çarpanları
            float speed = sprint ? sprintSpeed * (1f + CareerState.Stat(SkillStat.SprintSpeed)) : walkSpeed;
            speed *= 1f + CareerState.Stat(SkillStat.MoveSpeed);
            Vector3 target = (transform.right * input.x + transform.forward * input.y) * speed;
            horizontalVelocity = Vector3.Lerp(horizontalVelocity, target, 1f - Mathf.Exp(-acceleration * Time.deltaTime));

            if (characterController.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;

            if (jump && characterController.isGrounded)
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            verticalVelocity += gravity * Time.deltaTime;
            characterController.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
        }
    }
}
