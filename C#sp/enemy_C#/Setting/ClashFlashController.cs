using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 控制角力演出中的全屏白闪（Timeline Signal 调用）
/// </summary>
public class ClashFlashController : MonoBehaviour
{
    public Image flashImage;

    private void Awake()
    {
        if (flashImage == null)
            flashImage = GetComponent<Image>();
        if (flashImage != null)
            flashImage.color = new Color(1, 1, 1, 0);
    }

    /// <summary>
    /// Signal 调用：白闪一次（alpha 0→1→0）
    /// </summary>
    public void Flash()
    {
        if (flashImage != null)
            StartCoroutine(FlashRoutine());
    }

    private System.Collections.IEnumerator FlashRoutine()
    {
        float duration = 0.1f;
        float half = duration * 0.5f;

        // 0→1
        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Clamp01(t / half);
            flashImage.color = new Color(1, 1, 1, alpha);
            yield return null;
        }
        flashImage.color = new Color(1, 1, 1, 1);

        // 1→0
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Clamp01(1f - t / half);
            flashImage.color = new Color(1, 1, 1, alpha);
            yield return null;
        }
        flashImage.color = new Color(1, 1, 1, 0);
    }
}
