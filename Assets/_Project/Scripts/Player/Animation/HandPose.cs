using UnityEngine;

namespace LandFlipper.Player.Animation
{
    /// <summary>
    /// Kolu IK ile hedefe uzatıp eli verilen yöne (avuç içi / parmak) çevirir.
    /// Bilek bükülmesini önlemek için elin ihtiyaç duyduğu dönüşün "büküm" (twist) kısmı
    /// ön kola aktarılır; bilek sadece küçük bir düzeltme yapar.
    /// Mixamo eli: yerel +Z = avuç içi normali, yerel +Y = parmak yönü.
    /// </summary>
    public static class HandPose
    {
        public static void Solve(Transform upper, Transform forearm, Transform hand, Quaternion handBindLocal,
            Vector3 wristTarget, Vector3 elbowHint, Vector3 palmNormal, Vector3 fingerDirection)
        {
            TwoBoneIK.Solve(upper, forearm, hand, wristTarget, elbowHint);

            Quaternion desiredHand = Quaternion.LookRotation(palmNormal, fingerDirection);

            // Ön kol, elin bind pozuna göre hangi dönüşte olmalıydı?
            Quaternion desiredForearm = desiredHand * Quaternion.Inverse(handBindLocal);
            Quaternion delta = desiredForearm * Quaternion.Inverse(forearm.rotation);

            // Sadece ön kol ekseni etrafındaki dönüşü uygula (dirsek-bilek hattı ve el konumu değişmez).
            Vector3 axis = (hand.position - forearm.position).normalized;
            forearm.rotation = TwistAround(delta, axis) * forearm.rotation;

            hand.rotation = desiredHand;
        }

        /// <summary>Swing-twist ayrıştırması: q'nun 'axis' etrafındaki büküm bileşeni.</summary>
        static Quaternion TwistAround(Quaternion q, Vector3 axis)
        {
            Vector3 r = new Vector3(q.x, q.y, q.z);
            Vector3 p = Vector3.Project(r, axis);
            float x = p.x, y = p.y, z = p.z, w = q.w;
            float mag = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (mag < 1e-6f) return Quaternion.identity;
            return new Quaternion(x / mag, y / mag, z / mag, w / mag);
        }
    }
}
