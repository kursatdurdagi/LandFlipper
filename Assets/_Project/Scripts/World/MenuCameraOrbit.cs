using UnityEngine;
using UnityEngine.InputSystem;

namespace LandFlipper.World
{
    /// <summary>
    /// Ana menü arka planı: kamera dioramanın önünde yavaşça salınır.
    /// Odak noktası sola kaydırılır ki karakter, sol taraftaki menü kartının sağında kalsın.
    /// </summary>
    public class MenuCameraOrbit : MonoBehaviour
    {
        [SerializeField] Vector3 focus = new Vector3(0f, 1.1f, 0f);
        [SerializeField] float radius = 5.2f;
        [SerializeField] float height = 1.4f;
        [SerializeField] float baseAngle = 20f;
        [SerializeField] float swayAngle = 25f;
        [SerializeField] float swaySpeed = 0.12f;
        [SerializeField] float screenOffset = 1.5f;
        [Tooltip("Fare paralaksı: kamera imleci hafifçe takip eder (m).")]
        [SerializeField] Vector2 parallax = new Vector2(0.45f, 0.22f);
        [SerializeField] float parallaxSmoothing = 3f;

        Vector2 smoothedMouse;

        void LateUpdate()
        {
            float angle = baseAngle + Mathf.Sin(Time.unscaledTime * swaySpeed) * swayAngle;
            Quaternion orbit = Quaternion.Euler(0f, angle, 0f);

            Vector2 mouse = Vector2.zero;
            if (Mouse.current != null && Screen.width > 0 && Screen.height > 0)
            {
                Vector2 p = Mouse.current.position.ReadValue();
                mouse = new Vector2(p.x / Screen.width * 2f - 1f, p.y / Screen.height * 2f - 1f);
                mouse = Vector2.ClampMagnitude(mouse, 1.4f);
            }
            smoothedMouse = Vector2.Lerp(smoothedMouse, mouse, 1f - Mathf.Exp(-parallaxSmoothing * Time.unscaledDeltaTime));

            Vector3 offset = orbit * new Vector3(smoothedMouse.x * parallax.x, smoothedMouse.y * parallax.y, 0f);
            transform.position = focus + orbit * new Vector3(0f, height, radius) + offset;

            Vector3 right = orbit * Vector3.left; // kamera +Z tarafından -Z'ye bakıyor
            transform.LookAt(focus - right * screenOffset);
        }
    }
}
