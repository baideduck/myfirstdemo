using UnityEngine;

public class PlayerUI : MonoBehaviour
{
    private PlayerHealth _hp;
    private PlayerStamina _st;
    private PlayerSharpness _sh;

    const float BAR_W = 260f;
    const float BAR_H = 24f;
    const float BAR_GAP = 4f;
    const float BAR_X = 16f;
    const float BAR_Y = 16f;
    const float BS_W = 420f;
    const float BS_H = 24f;
    const float BS_GAP = 6f;
    const float BS_Y = 12f;

    void Start()
    {
        _hp = GetComponent<PlayerHealth>();
        _st = GetComponent<PlayerStamina>();
        _sh = GetComponent<PlayerSharpness>();
    }

    void OnGUI()
    {
        // 每帧直接找，不缓存，不用管引用失效
        var bossHp = FindObjectOfType<EnemyHealth>();
        var bossPt = FindObjectOfType<BossPosture>();

        DrawBoss(bossHp, bossPt);
        DrawPlayer();
    }

    void DrawBoss(EnemyHealth bossHp, BossPosture bossPt)
    {
        float bx = (Screen.width - BS_W) * 0.5f;
        float cy = BS_Y;

        if (bossHp != null && bossHp.maxHealth > 0f)
        {
            DrawOneBar(bx, cy, BS_W, BS_H,
                bossHp.currentHealth, bossHp.maxHealth,
                new Color(0.6f, 0.15f, 0.15f), "BOSS");
            cy += BS_H + BS_GAP;
        }

        if (bossPt != null && bossPt.MaxPosture > 0f)
        {
            DrawOneBar(bx, cy, BS_W, BS_H,
                bossPt.CurrentPosture, bossPt.MaxPosture,
                new Color(0.15f, 0.25f, 0.6f), "架势");
        }
    }

    void DrawPlayer()
    {
        float cx = BAR_X;
        float cy = BAR_Y;

        if (_hp != null && _hp.MaxHealth > 0f)
        {
            DrawOneBar(cx, cy, BAR_W, BAR_H,
                _hp.CurrentHealth, _hp.MaxHealth,
                new Color(0.7f, 0.2f, 0.2f), "HP");
            cy += BAR_H + BAR_GAP;
        }

        if (_st != null && _st.MaxStamina > 0f)
        {
            DrawOneBar(cx, cy, BAR_W, BAR_H,
                _st.CurrentStamina, _st.MaxStamina,
                new Color(0.25f, 0.6f, 0.2f), "ST");
            cy += BAR_H + BAR_GAP;
        }

        if (_sh != null && _sh.MaxSharpness > 0)
        {
            Color c = _sh.CurrentLevel switch
            {
                PlayerSharpness.SharpnessLevel.Blue => new Color(0.3f, 0.5f, 1f),
                PlayerSharpness.SharpnessLevel.Green => Color.green,
                PlayerSharpness.SharpnessLevel.White => Color.white,
                _ => Color.gray
            };
            DrawOneBar(cx, cy, BAR_W, BAR_H,
                _sh.CurrentSharpnessPoints, _sh.MaxSharpness, c, "斩味");
        }
    }

    void DrawOneBar(float x, float y, float w, float h,
        float cur, float max, Color color, string label)
    {
        if (max <= 0f) return;
        float ratio = Mathf.Clamp01(cur / max);

        GUI.Box(new Rect(x, y, w, h), "");
        GUI.color = color;
        GUI.DrawTexture(new Rect(x + 2, y + 2, (w - 4) * ratio, h - 4), Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(6, 0, 0, 0)
        };
        GUI.Label(new Rect(x + 2, y, w - 6, h), label, style);

        GUIStyle numStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleRight,
            padding = new RectOffset(0, 6, 0, 0)
        };
        GUI.Label(new Rect(x + 2, y, w - 6, h), $"{cur:F0}/{max}", numStyle);
    }
}
