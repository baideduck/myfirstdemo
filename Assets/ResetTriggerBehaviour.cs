using UnityEngine;

public class ResetBoolOnEnter : StateMachineBehaviour
{
    public string boolName = "isSlashing";

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.SetBool(boolName, false);
    }
}