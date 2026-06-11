using UnityEngine;

public class RestoreAttackLayerOnExit : StateMachineBehaviour
{
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        EnemyController enemy = animator.GetComponent<EnemyController>();
        if (enemy != null)
        {
            enemy.EnableAttackLayer();
        }
    }
}