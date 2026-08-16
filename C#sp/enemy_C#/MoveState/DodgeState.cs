using System.Collections;
using UnityEngine;

public class DodgeState : State<EnemyController>
{
    private EnemyController enemy;
    private bool finished = false;
    private bool previousRootMotion;
    private string dodgeAnimName = "Dodge_B";
    private const float MAX_DODGE_TIME = 2.5f;   // 兜底：动画异常时防止卡死

    [Header("预留特效音效")]
    public GameObject dodgeEffect;
    public AudioClip dodgeSound;

    public override void Enter(EnemyController owner)
    {
        enemy = owner;

        // 最后防线：Dodge 冷却中或已在 Dodge → 直接退回 Idle
        if (Time.time < enemy.noDodgeUntil || enemy.isDodging)
        {
            enemy.ChangeState(EnemyStates.Idle);
            return;
        }

        finished = false;
        enemy.isDodging = true;

        enemy.EnableWeaponHitBox(false, false);
        enemy.DisableAttackLayer();

        DetermineDodgeAnimation();

        // ★ 播放闪避动画 + 开启根运动：
        //   Dodge_B / Dodge_B_L / Dodge_B_R 片段自带根运动（RootT/RootQ），位移与转身由动画本身驱动。
        //   旧实现 applyRootMotion=false + 代码平移 transform = 滑步（动画原地播、身体被代码拖走）。
        enemy.anim.Play(dodgeAnimName, 0, 0f);
        enemy.anim.Update(0f);

        if (dodgeSound != null)
            AudioSource.PlayClipAtPoint(dodgeSound, enemy.transform.position);

        if (dodgeEffect != null)
            Object.Instantiate(dodgeEffect, enemy.transform.position, Quaternion.identity);

        previousRootMotion = enemy.anim.applyRootMotion;
        enemy.anim.applyRootMotion = true;

        enemy.StartCoroutine(WaitDodgeFinish());
    }

    /// <summary>
    /// 随机选闪避动画：直后 / 左后 / 右后（位移方向由动画本身决定）
    /// </summary>
    private void DetermineDodgeAnimation()
    {
        float roll = Random.value;
        if (roll < 0.33f)
            dodgeAnimName = "Dodge_B";
        else if (roll < 0.66f)
            dodgeAnimName = "Dodge_B_L";
        else
            dodgeAnimName = "Dodge_B_R";
    }

    /// <summary>
    /// 等闪避动画播完（根运动全程驱动位移），播完回 Idle。
    /// 时长以动画实际长度为准，避免旧实现 0.4s 硬切把 ~1.3s 的闪避动画掐断成滑步。
    /// </summary>
    IEnumerator WaitDodgeFinish()
    {
        // 关掉 CharacterController，避免它干扰根运动对 Transform 的直接驱动
        CharacterController cc = enemy.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        AnimatorStateInfo st = enemy.anim.GetCurrentAnimatorStateInfo(0);
        float clipLen = st.IsName(dodgeAnimName) ? st.length : 1.2f;
        float waitTime = Mathf.Clamp(clipLen, 0.3f, MAX_DODGE_TIME);

        float elapsed = 0f;
        while (elapsed < waitTime)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // 恢复 CharacterController
        if (cc != null) cc.enabled = true;

        finished = true;
    }

    public override void Execute()
    {
        if (enemy == null) return;
        if (finished)
            enemy.ChangeState(EnemyStates.Idle);
    }

    public override void Exit()
    {
        enemy.isDodging = false;  // 解除锁定

        // 通知 EnemyController 追击惩罚计时 + 近距 Dodge 冷却同步
        enemy.consecutiveHits = 0;  // 成功 Dodge → 重置连续受击
        enemy.OnDodgeEnded();
        enemy.lastDodgeTime = Time.time;
        enemy.lastAttackEndTime = Time.time;  // 防止 Dodge 后立即再次触发近距 Dodge
        enemy.noDodgeUntil = Time.time + 2f;  // 硬性冷却：2秒内任何源不许再出 Dodge

        enemy.anim.applyRootMotion = previousRootMotion;

        int attackLayer = enemy.anim.GetLayerIndex("Attack Layer");
        if (attackLayer != -1)
            enemy.anim.SetLayerWeight(attackLayer, 1f);
        enemy.EnableWeaponHitBox(false, false);
    }
}
