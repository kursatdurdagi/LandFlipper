using UnityEngine;

namespace LandFlipper.Player.Animation
{
    /// <summary>
    /// Analitik iki kemik IK (bacak: kalça-diz-ayak, kol: omuz-dirsek-el).
    /// Hint noktası diz/dirseğin hangi yöne büküleceğini belirler.
    /// </summary>
    public static class TwoBoneIK
    {
        public static void Solve(Transform upper, Transform middle, Transform end, Vector3 target, Vector3 hint)
        {
            Vector3 a = upper.position;
            Vector3 b = middle.position;
            Vector3 c = end.position;

            float upperLength = Vector3.Distance(a, b);
            float lowerLength = Vector3.Distance(b, c);
            if (upperLength < 1e-4f || lowerLength < 1e-4f) return;

            Vector3 toTarget = target - a;
            float targetDistance = Mathf.Clamp(toTarget.magnitude, 1e-3f, upperLength + lowerLength - 1e-3f);
            Vector3 direction = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : (c - a).normalized;

            Vector3 bendDirection = Vector3.ProjectOnPlane(hint - a, direction);
            if (bendDirection.sqrMagnitude < 1e-6f)
                bendDirection = Vector3.ProjectOnPlane(b - a, direction);
            bendDirection.Normalize();

            // Kosinüs teoremi: kalça/omuz açısı
            float cosAngle = (upperLength * upperLength + targetDistance * targetDistance - lowerLength * lowerLength)
                             / (2f * upperLength * targetDistance);
            float angle = Mathf.Acos(Mathf.Clamp(cosAngle, -1f, 1f));
            Vector3 desiredMiddle = a + (direction * Mathf.Cos(angle) + bendDirection * Mathf.Sin(angle)) * upperLength;

            upper.rotation = Quaternion.FromToRotation(b - a, desiredMiddle - a) * upper.rotation;

            b = middle.position;
            c = end.position;
            Vector3 desiredEnd = a + direction * targetDistance;
            middle.rotation = Quaternion.FromToRotation(c - b, desiredEnd - b) * middle.rotation;
        }
    }
}
