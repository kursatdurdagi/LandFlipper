using LandFlipper.Core;
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
    /// Ekonomi/dünya değişiklikleri bu sınıfta değil, host doğrulamalı sistemlerde yapılacak.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] InputActionAsset inputActions;
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
        [SerializeField] float mouseSensitivity = 0.12f;
        [SerializeField] float gamepadSensitivity = 180f;
        [SerializeField] float minPitch = -85f;
        [SerializeField] float maxPitch = 85f;

        [Header("Debug View")]
        [Tooltip("V tuşu ile geçilen test amaçlı üçüncü şahıs kamera ofseti.")]
        [SerializeField] Vector3 thirdPersonOffset = new Vector3(0.5f, 0.2f, -2.8f);

        [SyncVar] float syncedPitch;

        CharacterController characterController;
        InputActionAsset runtimeActions;
        InputAction moveAction, lookAction, jumpAction, sprintAction, toggleViewAction;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float pitch;
        bool thirdPerson;

        public Camera PlayerCamera => playerCamera;
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

            runtimeActions = Instantiate(inputActions);
            var map = runtimeActions.FindActionMap("Player", true);
            moveAction = map.FindAction("Move", true);
            lookAction = map.FindAction("Look", true);
            jumpAction = map.FindAction("Jump", true);
            sprintAction = map.FindAction("Sprint", true);
            map.Enable();

            toggleViewAction = new InputAction("ToggleView", InputActionType.Button, "<Keyboard>/v");
            toggleViewAction.AddBinding("<Gamepad>/select");
            toggleViewAction.Enable();

            if (!InputGate.IsBlocked)
                CursorUtility.SetLocked(true);
        }

        public override void OnStopLocalPlayer()
        {
            if (runtimeActions != null)
            {
                runtimeActions.Disable();
                Destroy(runtimeActions);
                runtimeActions = null;
            }
            toggleViewAction?.Dispose();
            toggleViewAction = null;
            CursorUtility.SetLocked(false);
        }

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
            if (isLocalPlayer && runtimeActions != null)
            {
                bool blocked = InputGate.IsBlocked;
                Vector2 moveInput = blocked ? Vector2.zero : moveAction.ReadValue<Vector2>();
                bool sprint = !blocked && sprintAction.IsPressed();
                bool jump = !blocked && jumpAction.WasPressedThisFrame();

                if (!blocked)
                {
                    HandleLook();
                    if (toggleViewAction.WasPressedThisFrame())
                        SetThirdPerson(!thirdPerson);
                }

                HandleMovement(moveInput, sprint, jump);

                if (Mathf.Abs(syncedPitch - pitch) > 0.5f)
                    syncedPitch = pitch;
            }

            if (characterAnimator != null)
                characterAnimator.LookPitch = isLocalPlayer ? pitch : syncedPitch;
        }

        void HandleLook()
        {
            Vector2 raw = lookAction.ReadValue<Vector2>();
            if (raw == Vector2.zero) return;

            bool isPointer = lookAction.activeControl?.device is Pointer;
            Vector2 delta = isPointer ? raw * mouseSensitivity : raw * (gamepadSensitivity * Time.deltaTime);

            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void HandleMovement(Vector2 input, bool sprint, bool jump)
        {
            float speed = sprint ? sprintSpeed : walkSpeed;
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
