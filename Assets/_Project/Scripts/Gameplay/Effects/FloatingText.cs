using UnityEngine;

namespace LandFlipper.Gameplay.Effects
{
    /// <summary>
    /// Dünyada yükselip kaybolan kısa metin (ör. satışta "+15 TL"). Sadece görsel; ağ durumu taşımaz.
    /// </summary>
    public class FloatingText : MonoBehaviour
    {
        const float Duration = 1.1f;
        const float Rise = 0.9f;

        TextMesh main, shadow;
        Color mainColor, shadowColor;
        Vector3 start;
        float age;

        public static void Spawn(string text, Vector3 position, Color color, float size = 0.012f)
        {
            var go = new GameObject("FloatingText");
            go.transform.position = position;
            var ft = go.AddComponent<FloatingText>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            ft.shadow = CreateLabel(go.transform, font, text, size, new Color(0.15f, 0.1f, 0.05f, 0.85f), new Vector3(0.012f, -0.012f, 0.01f));
            ft.main = CreateLabel(go.transform, font, text, size, color, Vector3.zero);
            ft.mainColor = color;
            ft.shadowColor = ft.shadow.color;
            ft.start = position;
        }

        static TextMesh CreateLabel(Transform parent, Font font, string text, float size, Color color, Vector3 offset)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = text;
            tm.fontSize = 96;
            tm.characterSize = size;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return tm;
        }

        void LateUpdate()
        {
            age += Time.deltaTime;
            float t = age / Duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // hızlı yüksel, yavaşla; son üçte birde sön
            float eased = 1f - (1f - t) * (1f - t);
            transform.position = start + Vector3.up * (Rise * eased);
            float pop = t < 0.12f ? Mathf.Lerp(0.6f, 1.15f, t / 0.12f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.12f) / 0.15f));
            transform.localScale = Vector3.one * pop;

            float alpha = t < 0.66f ? 1f : 1f - (t - 0.66f) / 0.34f;
            main.color = new Color(mainColor.r, mainColor.g, mainColor.b, mainColor.a * alpha);
            shadow.color = new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowColor.a * alpha);

            var cam = Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
