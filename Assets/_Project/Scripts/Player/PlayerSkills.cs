using System;
using LandFlipper.Gameplay;
using Mirror;

namespace LandFlipper.Player
{
    /// <summary>
    /// Yetenek yükseltme isteğini host'a ileten köprü. Puan ve yetenekler host kariyerine aittir;
    /// herhangi bir oyuncu ortak puanı harcayabilir (GDD 6.3: ilk sürümde XP host kariyerinde).
    /// </summary>
    public class PlayerSkills : NetworkBehaviour
    {
        /// <summary>Local oyuncuya gösterilecek hata/bilgi mesajı.</summary>
        public static event Action<string> LocalNotification;

        public static PlayerSkills Local { get; private set; }

        public override void OnStartLocalPlayer() => Local = this;

        public override void OnStopLocalPlayer()
        {
            if (Local == this) Local = null;
        }

        public void RequestUpgrade(SkillDefinition skill)
        {
            if (skill == null || CareerState.Instance == null) return;
            if (!CareerState.Instance.CanUpgrade(skill, out string reason))
            {
                LocalNotification?.Invoke(reason);
                return;
            }
            CmdUpgrade(skill.id);
        }

        [Command]
        void CmdUpgrade(string skillId)
        {
            var career = CareerState.Instance;
            if (career == null) return;
            if (!career.ServerTryUpgrade(skillId, out string message))
                TargetNotify(connectionToClient, message);
        }

        [TargetRpc]
        void TargetNotify(NetworkConnectionToClient target, string message) => LocalNotification?.Invoke(message);
    }
}
