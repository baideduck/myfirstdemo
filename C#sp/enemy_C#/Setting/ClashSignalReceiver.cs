using UnityEngine;
using UnityEngine.Playables;

public class ClashSignalReceiver : MonoBehaviour
{
    [Header("引用")]
    public BossPhaseManager phaseMgr;
    public GameObject player;
    public float knockbackDistance = 8f;

    private Animator bossAnim;
    private Animator playerAnim;
    private EnemyController enemyCtrl;
    private Transform bossTransform;

    // 角力期间持续面向玩家（每帧强制修正旋转，不影响 Root Motion 位移）
    private bool forceFacePlayer = false;

    private void LateUpdate()
    {
        if (forceFacePlayer && bossTransform != null && player != null)
        {
            Vector3 dir = (player.transform.position - bossTransform.position).normalized;
            dir.y = 0;
            if (dir.magnitude > 0.01f)
                bossTransform.rotation = Quaternion.LookRotation(dir);
        }
    }

    /// <summary>
    /// 让 Boss 面向玩家（单次调用）
    /// </summary>
    private void FacePlayer()
    {
        if (bossTransform != null && player != null)
        {
            Vector3 dir = (player.transform.position - bossTransform.position).normalized;
            dir.y = 0;
            if (dir.magnitude > 0.01f)
                bossTransform.rotation = Quaternion.LookRotation(dir);
        }
    }

    private void Awake()
    {
        if (phaseMgr != null)
        {
            bossTransform = phaseMgr.transform;
            enemyCtrl = phaseMgr.GetComponent<EnemyController>();
            bossAnim = phaseMgr.GetComponent<Animator>();
            if (bossAnim == null && enemyCtrl != null)
                bossAnim = enemyCtrl.anim;
        }

        if (player != null)
            playerAnim = player.GetComponent<Animator>();
    }

    /// <summary>
    /// Signal: 角力开始，Boss后撤拉开距离
    /// </summary>
    public void OnStart()
    {
        forceFacePlayer = true;

        FacePlayer();

        if (bossAnim != null)
        {
            bossAnim.Play("Dodge_B", 0, 0f);
            bossAnim.Update(0f);
        }
    }

    /// <summary>
    /// Signal: Boss居合突进到玩家面前
    /// </summary>
    public void OnIaiArrive()
    {
        FacePlayer();
    }

    /// <summary>
    /// Signal: 玩家 Block_Fail + 被击退
    /// </summary>
    public void OnPlayerBlockFail()
    {
        if (player == null) player = GameObject.FindGameObjectWithTag("Player");
        if (playerAnim == null) playerAnim = player.GetComponent<Animator>();

        FacePlayer();

        if (playerAnim != null)
        {
            playerAnim.Play("Block_Fail", 0, 0f);
            playerAnim.Update(0f);
        }

        // Boss 的方向击退玩家
        if (bossTransform != null)
        {
            Vector3 dir = (player.transform.position - bossTransform.position).normalized;
            dir.y = 0;
            player.transform.position += dir * 2.5f;
        }
    }

    /// <summary>
    /// Signal: Boss被玩家顶飞
    /// </summary>
    public void OnBossKnockback()
    {
        FacePlayer();

        if (bossAnim != null)
            bossAnim.Play("Hit_Air", 0, 0f);
    }

    /// <summary>
    /// Signal: Boss翻滚落地
    /// </summary>
    public void OnBossRoll()
    {
        FacePlayer();

        if (bossAnim != null)
            bossAnim.Play("Roll", 0, 0f);
    }

    /// <summary>
    /// Signal: 进入宗师状态，结束演出
    /// </summary>
    public void OnEnterMasterPhase()
    {
        forceFacePlayer = false;

        if (bossAnim != null)
            bossAnim.Play("Idle", 0, 0f);

        if (enemyCtrl != null)
        {
            enemyCtrl.HasSuperArmor = false;
            enemyCtrl.isExecutionFrozen = false;
        }

        if (phaseMgr != null)
            phaseMgr.OnStruggleFinished();

        // 解锁玩家
        if (player == null) player = GameObject.FindGameObjectWithTag("Player");
        MeeleFighter mf = player.GetComponent<MeeleFighter>();
        PlayerController pc = player.GetComponent<PlayerController>();
        if (mf != null) { mf.InAction = false; mf.IsHyperArmor = false; }
        if (pc != null) pc.LockMovement(0f);
    }
}
