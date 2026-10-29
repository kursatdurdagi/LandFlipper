using UnityEngine;

namespace LandFlipper.Core
{
    /// <summary>
    /// Menüler açıkken oyuncu girdisini (hareket/bakış/etkileşim) kapatan tek nokta.
    /// </summary>
    public static class InputGate
    {
        public static bool IsBlocked { get; private set; }

        public static void SetBlocked(bool blocked)
        {
            IsBlocked = blocked;
        }
    }

    public static class CursorUtility
    {
        public static void SetLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
