using UnityEngine;

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

        void LateUpdate()
        {
            float angle = baseAngle + Mathf.Sin(Time.unscaledTime * swaySpeed) * swayAngle;
            Quaternion orbit = Quaternion.Euler(0f, angle, 0f);
            transform.position = focus + orbit * new Vector3(0f, height, radius);

            Vector3 right = orbit * Vector3.left; // kamera +Z tarafından -Z'ye bakıyor
            transform.LookAt(focus - right * screenOffset);
        }
    }
}
