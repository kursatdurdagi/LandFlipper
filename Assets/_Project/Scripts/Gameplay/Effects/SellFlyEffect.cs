using System;
using UnityEngine;

namespace LandFlipper.Gameplay.Effects
{
    /// <summary>
    /// Satılan kaynağın görsel kopyası: elden tezgâha kavisle uçar, döner, küçülür ve kaybolur.
    /// Asıl ağ nesnesi host'ta hemen yok edilir; bu kopya her istemcide yerel olarak oynar.
    /// </summary>
    public class SellFlyEffect : MonoBehaviour
    {
        Vector3 from, to;
        Quaternion startRotation;
        Vector3 startScale;
        Vector3 spinAxis;
        float duration, arcHeight, age;
        Action onArrive;

        public static void Play(ResourceDefinition definition, Vector3 position, Quaternion rotation, float scale,
            Vector3 target, Action onArrive)
        {
            if (definition == null || definition.prefab == null) return;
            var visual = definition.prefab.transform.Find("Visual");
            if (visual == null) return;

            var clone = Instantiate(visual.gameObject, position, rotation);
            clone.name = "SellFx_" + definition.id;
            var baseScale = visual.localScale;
            clone.transform.localScale = baseScale * scale;

            var fx = clone.AddComponent<SellFlyEffect>();
            fx.from = position;
            fx.to = target;
            fx.startRotation = rotation;
            fx.startScale = clone.transform.localScale;
            fx.duration = Mathf.Clamp(Vector3.Distance(position, target) * 0.12f, 0.3f, 0.55f);
            fx.arcHeight = 0.5f + Vector3.Distance(position, target) * 0.08f;
            fx.spinAxis = UnityEngine.Random.onUnitSphere;
            fx.onArrive = onArrive;
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / duration);

            Vector3 p = Vector3.Lerp(from, to, t);
            p.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
            transform.position = p;
            transform.rotation = Quaternion.AngleAxis(t * 420f, spinAxis) * startRotation;

            // son kısımda tezgâha "yutulur"
            float shrink = t < 0.7f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.7f) / 0.3f);
            transform.localScale = startScale * shrink;

            if (t >= 1f)
            {
                onArrive?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
