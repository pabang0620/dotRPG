using UnityEngine;

namespace DotRPG
{
    public partial class EnemyController
    {
        /// <summary>
        /// [CH3] Leaves the field without being killed (공허 폭발체 after its burst): no loot, no kill report, no quest count,
        /// but <see cref="Died"/> still fires so its spawner schedules the respawn and listeners let go of it.
        /// </summary>
        internal void Expire()
        {
            if (state == State.Dead) return;
            EnterState(State.Dead);
            if (bodyCollider != null) bodyCollider.enabled = false;
            Died?.Invoke(this);
            gameObject.SetActive(false);
            Destroy(gameObject, 0.1f);
        }
    }
}
