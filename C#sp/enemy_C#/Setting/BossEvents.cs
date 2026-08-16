using System;
using UnityEngine;

/// <summary>
/// Boss 端事件中心 —— 所有跨模块通信的唯一通道
/// </summary>
public class BossEvents : MonoBehaviour
{
    // ── 攻击生命周期 ──
    public event Action<EnemyStates> OnAttackStarted;
    public event Action OnAttackFinished;
    public event Action OnAttackInterrupted;

    // ── 受击 ──
    public event Action OnHitTaken;
    public event Action OnRecoveredFromHit;

    // ── 死亡 ──
    public event Action OnDied;

    // ── 调用入口 ──
    public void FireAttackStarted(EnemyStates state) => OnAttackStarted?.Invoke(state);
    public void FireAttackFinished() => OnAttackFinished?.Invoke();
    public void FireAttackInterrupted() => OnAttackInterrupted?.Invoke();
    public void FireHitTaken() => OnHitTaken?.Invoke();
    public void FireRecoveredFromHit() => OnRecoveredFromHit?.Invoke();
    public void FireDied() => OnDied?.Invoke();
}
