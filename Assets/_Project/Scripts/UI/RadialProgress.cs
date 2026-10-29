using UnityEngine;
using UnityEngine.UIElements;

namespace LandFlipper.UI
{
    /// <summary>
    /// UI Toolkit dairesel ilerleme halkası (Painter2D ile çizilir).
    /// Basılı tutarak yapılan işlemlerde (çim biçme vb.) nişangâhın etrafında gösterilir.
    /// </summary>
    public class RadialProgress : VisualElement
    {
        float progress;

        public float LineWidth { get; set; } = 6f;
        public Color TrackColor { get; set; } = new Color(0f, 0f, 0f, 0.35f);
        public Color FillColor { get; set; } = new Color(0.94f, 0.75f, 0.24f, 1f);
        public Color CompleteColor { get; set; } = new Color(0.55f, 0.82f, 0.42f, 1f);

        public float Progress
        {
            get => progress;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(clamped, progress)) return;
                progress = clamped;
                MarkDirtyRepaint();
            }
        }

        public RadialProgress()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            float radius = (Mathf.Min(rect.width, rect.height) - LineWidth) * 0.5f;
            if (radius <= 0f) return;

            var painter = context.painter2D;
            Vector2 center = rect.center;
            painter.lineWidth = LineWidth;
            painter.lineCap = LineCap.Round;

            painter.strokeColor = TrackColor;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();

            if (progress <= 0f) return;

            painter.strokeColor = Color.Lerp(FillColor, CompleteColor, progress * progress);
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * progress));
            painter.Stroke();
        }
    }
}
