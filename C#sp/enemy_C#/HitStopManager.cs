using System.Collections;
using UnityEngine;

public class HitStopManager : MonoBehaviour
{
    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    // �������ű����ã���������Ч��
    public void TriggerHitStop(float duration, float slowedTimeScale = 0f)
    {
        if (animator != null)
            StartCoroutine(HitStopCoroutine(duration, slowedTimeScale));
    }

    IEnumerator HitStopCoroutine(float duration, float slowedTimeScale)
    {
        // 冻结动画（slowedTimeScale 通常为 0）
        animator.speed = slowedTimeScale;

        // 按真实时间等待（不受 Time.timeScale 影响）
        yield return new WaitForSecondsRealtime(duration);

        // 强制恢复动画速度（不保存恢复原值，防止遗留 speed=0）
        animator.speed = 1f;
    }
}