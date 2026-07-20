using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public bool allowRotation = true;

    [Header("Target")]
    [SerializeField] Transform followTarget;

    [Header("Rotation")]
    [SerializeField] [Range(0.1f, 10f)] float RotationSpeed = 2f;   // ← 镜头灵敏度，拖滑条调
    [SerializeField] float minVerticalAngle = -20f;
    [SerializeField] float maxVerticalAngle = 45f;
    [SerializeField] bool invertX;
    [SerializeField] bool invertY;

    [Header("Distance")]
    [SerializeField] float distance = 5f;
    [SerializeField] float scrollSpeed = 2f;
    [SerializeField] float minDistance = 2f;
    [SerializeField] float maxDistance = 8f;

    [Header("Pivot (镜头圆心)")]
    [SerializeField] float pivotHeight = 1.5f;   // 镜头圆心高度（胸口位置）

    [Header("Damping (阻尼)")]
    [SerializeField] float scrollDampTime = 0.03f;    // 滚轮缩放平滑时间
    [SerializeField] float positionDampTime = 0.04f;  // 镜头位置平滑时间

    [Header("Framing")]
    [SerializeField] Vector2 framingOffset;

    [Header("Occlusion (墙壁透明)")]
    [SerializeField] LayerMask occlusionLayers;
    [SerializeField] float occlusionRadius = 0.2f;
    [SerializeField] Material transparentMaterial;

    [Header("Camera Collision (镜头碰撞)")]
    [SerializeField] LayerMask collisionLayers;
    [SerializeField] float collisionRadius = 0.25f;
    [SerializeField] float collisionOffset = 0.1f;

    [Header("Player Fade (玩家淡入淡出)")]
    [SerializeField] float fadeStartDistance = 1.5f;
    [SerializeField] float fadeEndDistance = 0.5f;
    [SerializeField] float fadeSpeed = 5f;

    [Header("Lock On (锁定系统)")]
    [SerializeField] LayerMask enemyLayer;
    [SerializeField] float lockOnRange = 15f;
    [SerializeField] float lockOnViewAngle = 90f;
    [SerializeField] float lockedCameraSpeed = 5f;
    [SerializeField] Vector2 lockOnOffset = new Vector2(0.5f, 0f);

    // ========== 震动系统 ==========
    [Header("三级蓄力冲击震动")]
    [SerializeField]
    private AnimationCurve heavySlashShakeCurve = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, -3f),
        new Keyframe(0.08f, 0.4f, -2f, 0f),
        new Keyframe(0.3f, 0f, 0f, 0f)
    );
    [SerializeField] private float heavySlashMagnitude = 0.06f;

    [Header("二级蓄力冲击震动")]
    [SerializeField]
    private AnimationCurve tier2ChargeShakeCurve = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, -3f),
        new Keyframe(0.06f, 0.25f, -2f, 0f),
        new Keyframe(0.15f, 0f, 0f, 0f)
    );
    [SerializeField] private float tier2ChargeMagnitude = 0.04f;

    [Header("敌人居合冲击震动")]
    [SerializeField]
    private AnimationCurve iaiSlashShakeCurve = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, -3.5f),
        new Keyframe(0.05f, 0.5f, -3f, 0f),
        new Keyframe(0.2f, 0f, 0f, 0f)
    );
    [SerializeField] private float iaiShakeMagnitude = 0.07f;

    // 内部状态
    private float shakeElapsed = 0f;
    private float shakeDuration = 0f;
    private AnimationCurve currentShakeCurve;
    private float currentShakeMagnitude = 0f;
    private Vector3 appliedShakeOffset = Vector3.zero;

    private float rotationX;
    private float rotationY;
    private float invertXval;
    private float invertYval;

    // 墙壁透明管理
    private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private List<Renderer> currentlyOccluding = new List<Renderer>();

    // 玩家淡入淡出
    private List<Material> playerMaterials = new List<Material>();
    private float currentAlpha = 1f;

    // 锁定系统
    private Transform lockedTarget;
    private bool isLockedOn = false;

    // ═══════════════════════════════════════
    //  阻尼平滑用
    // ═══════════════════════════════════════
    private float targetDistance;
    private float smoothDistanceVelocity;
    private Vector3 smoothPositionVelocity;

    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        targetDistance = distance;

        // 获取玩家所有材质并实例化
        if (followTarget != null)
        {
            Renderer[] renderers = followTarget.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                if (rend.gameObject.layer == LayerMask.NameToLayer("UI")) continue;

                Material[] originalMats = rend.materials;
                Material[] newMats = new Material[originalMats.Length];
                for (int i = 0; i < originalMats.Length; i++)
                {
                    Material instance = new Material(originalMats[i]);
                    newMats[i] = instance;
                    playerMaterials.Add(instance);
                }
                rend.materials = newMats;
            }
        }
    }

    void OnDestroy()
    {
        foreach (Material mat in playerMaterials)
            Destroy(mat);
        playerMaterials.Clear();
    }

    // ★ 唯一改动：Update → LateUpdate
    void LateUpdate()
    {
        // --- 锁定输入处理 ---
        if (Input.GetMouseButtonDown(2))
        {
            if (isLockedOn) UnlockTarget();
            else TryLockOn();
        }

        if (isLockedOn)
        {
            if (!IsTargetValid(lockedTarget)) UnlockTarget();
        }

        // --- 旋转输入 ---
        invertXval = invertX ? -1 : 1;
        invertYval = invertY ? -1 : 1;

        if (isLockedOn && lockedTarget != null)
        {
            UpdateLockedOnRotation();
        }
        else
        {
            rotationY += Input.GetAxis("Mouse X") * RotationSpeed * invertXval;
        }

        rotationX += Input.GetAxis("Mouse Y") * RotationSpeed * invertYval;
        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);

        // ── 滚轮调距离（带阻尼） ──
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        targetDistance -= scroll * scrollSpeed;
        targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
        distance = Mathf.SmoothDamp(distance, targetDistance, ref smoothDistanceVelocity, scrollDampTime, maxDistance);

        Quaternion targetRotation = Quaternion.Euler(rotationX, rotationY, 0);

        Vector3 focusPosition = followTarget.position
                                + followTarget.up * pivotHeight
                                + followTarget.right * framingOffset.x
                                + followTarget.forward * framingOffset.y;

        Vector3 desiredPosition;
        if (isLockedOn && lockedTarget != null)
        {
            desiredPosition = CalculateLockedOnCameraPosition(focusPosition, targetRotation);
        }
        else
        {
            desiredPosition = focusPosition - targetRotation * new Vector3(0, 0, distance);
        }

        // ── 碰撞检测：地面/墙壁平滑缩回 ──
        Vector3 collisionPos = ApplyCameraCollision(desiredPosition, focusPosition);
        float collisionDist = Vector3.Distance(focusPosition, collisionPos);

        // 如果碰撞把镜头推近了，让 targetDistance 逐步缩小（平滑收拢）
        if (collisionDist < distance - 0.01f)
        {
            targetDistance = Mathf.Min(targetDistance, collisionDist);
        }
        // 当碰撞解除时，逐渐恢复到用户设定的 targetDistance（但用 distance 平滑过渡）

        // ── 阻尼：平滑移动镜头位置 ──
        transform.position = Vector3.SmoothDamp(transform.position, collisionPos, ref smoothPositionVelocity, positionDampTime);
        transform.rotation = targetRotation;

        HandleOcclusion(focusPosition, collisionPos);
        HandlePlayerFade(collisionPos);

        ApplyShakeOffset();

        // 锁定标记跟随敌人屏幕位置
        UpdateLockOnMarkerPosition();
    }

    private void TryLockOn()
    {
        Collider[] colliders = Physics.OverlapSphere(followTarget.position, lockOnRange, enemyLayer);
        if (colliders.Length == 0) return;

        Transform bestTarget = null;
        float bestScore = float.MaxValue;
        Vector3 cameraForward = transform.forward;
        Vector3 playerPos = followTarget.position;

        foreach (Collider col in colliders)
        {
            Transform enemy = col.transform;
            Vector3 dirToEnemy = (enemy.position - playerPos).normalized;

            float angle = Vector3.Angle(cameraForward, dirToEnemy);
            if (angle > lockOnViewAngle) continue;

            float dist = Vector3.Distance(playerPos, enemy.position);
            if (Physics.Raycast(playerPos, dirToEnemy, out RaycastHit hit, dist, collisionLayers | occlusionLayers))
            {
                if (hit.collider.transform.root != enemy.root) continue;
            }

            float score = angle * 1.5f + dist * 0.3f;
            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = enemy;
            }
        }

        if (bestTarget != null)
        {
            lockedTarget = bestTarget;
            isLockedOn = true;
            CreateLockOnMarker();
        }
    }

    private void UnlockTarget()
    {
        lockedTarget = null;
        isLockedOn = false;
        DestroyLockOnMarker();
    }

    private bool IsTargetValid(Transform target)
    {
        if (target == null) return false;
        float dist = Vector3.Distance(followTarget.position, target.position);
        return dist <= lockOnRange * 1.5f;
    }

    private void UpdateLockedOnRotation()
    {
        Vector3 playerPos = followTarget.position;
        Vector3 targetPos = lockedTarget.position;
        Vector3 dirToTarget = (targetPos - playerPos).normalized;
        dirToTarget.y = 0;

        if (dirToTarget != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(dirToTarget);
            float targetY = lookRotation.eulerAngles.y;
            rotationY = Mathf.LerpAngle(rotationY, targetY, lockedCameraSpeed * Time.deltaTime);
        }
    }

    private Vector3 CalculateLockedOnCameraPosition(Vector3 focusPosition, Quaternion targetRotation)
    {
        Vector3 basePos = focusPosition - targetRotation * new Vector3(0, 0, distance);
        Vector3 playerToTarget = lockedTarget.position - focusPosition;
        Vector3 rightOffset = Vector3.Cross(playerToTarget.normalized, Vector3.up) * lockOnOffset.x;
        Vector3 upOffset = Vector3.up * lockOnOffset.y;
        return basePos + rightOffset + upOffset;
    }

    private Vector3 ApplyCameraCollision(Vector3 targetPos, Vector3 pivot)
    {
        Vector3 direction = (targetPos - pivot).normalized;
        float distanceToTarget = Vector3.Distance(pivot, targetPos);

        if (Physics.SphereCast(pivot, collisionRadius, direction, out RaycastHit hit, distanceToTarget, collisionLayers))
        {
            float safeDistance = hit.distance - collisionRadius - collisionOffset;
            if (safeDistance < 0) safeDistance = 0;
            return pivot + direction * safeDistance;
        }
        return targetPos;
    }

    private void HandleOcclusion(Vector3 from, Vector3 to)
    {
        Vector3 direction = (to - from).normalized;
        float maxDistance = Vector3.Distance(from, to);

        int maxHits = 20;
        RaycastHit[] hits = new RaycastHit[maxHits];
        int numHits = Physics.SphereCastNonAlloc(from, occlusionRadius, direction, hits, maxDistance, occlusionLayers);

        HashSet<Renderer> currentOccluders = new HashSet<Renderer>();
        for (int i = 0; i < numHits; i++)
        {
            Renderer renderer = hits[i].collider.GetComponent<Renderer>();
            if (renderer != null)
                currentOccluders.Add(renderer);
        }

        foreach (Renderer r in currentOccluders)
        {
            if (!currentlyOccluding.Contains(r))
            {
                if (!originalMaterials.ContainsKey(r))
                    originalMaterials[r] = r.sharedMaterials;
                Material[] transMats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < transMats.Length; i++)
                    transMats[i] = transparentMaterial;
                r.sharedMaterials = transMats;
                currentlyOccluding.Add(r);
            }
        }

        List<Renderer> toRemove = new List<Renderer>();
        foreach (Renderer r in currentlyOccluding)
        {
            if (!currentOccluders.Contains(r))
            {
                if (originalMaterials.ContainsKey(r))
                {
                    r.sharedMaterials = originalMaterials[r];
                    originalMaterials.Remove(r);
                }
                toRemove.Add(r);
            }
        }
        foreach (Renderer r in toRemove)
            currentlyOccluding.Remove(r);
    }

    private void HandlePlayerFade(Vector3 rawCameraPos)
    {
        if (playerMaterials.Count == 0) return;

        float distanceToPlayer = Vector3.Distance(rawCameraPos, followTarget.position);
        float targetAlpha = 1f;

        if (distanceToPlayer < fadeStartDistance)
        {
            targetAlpha = Mathf.InverseLerp(fadeEndDistance, fadeStartDistance, distanceToPlayer);
            targetAlpha = Mathf.Clamp01(targetAlpha);
        }

        currentAlpha = Mathf.Lerp(currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);

        foreach (Material mat in playerMaterials)
        {
            if (mat.HasProperty("_Color"))
            {
                Color color = mat.color;
                color.a = currentAlpha;
                mat.color = color;
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                Color color = mat.GetColor("_BaseColor");
                color.a = currentAlpha;
                mat.SetColor("_BaseColor", color);
            }
        }
    }

    public void TriggerTier2ChargeShake()
    {
        currentShakeCurve = tier2ChargeShakeCurve;
        currentShakeMagnitude = tier2ChargeMagnitude;
        shakeElapsed = 0f;
        shakeDuration = tier2ChargeShakeCurve.keys[tier2ChargeShakeCurve.length - 1].time;
    }

    public void TriggerHeavySlashImpact(Vector3 enemyPosition)
    {
        currentShakeCurve = heavySlashShakeCurve;
        currentShakeMagnitude = heavySlashMagnitude;
        shakeElapsed = 0f;
        shakeDuration = heavySlashShakeCurve.keys[heavySlashShakeCurve.length - 1].time;
    }

    public void TriggerIaiShake(Vector3 enemyPosition)
    {
        currentShakeCurve = iaiSlashShakeCurve;
        currentShakeMagnitude = iaiShakeMagnitude;
        shakeElapsed = 0f;
        shakeDuration = iaiSlashShakeCurve.keys[iaiSlashShakeCurve.length - 1].time;
    }

    private void ApplyShakeOffset()
    {
        if (shakeElapsed < shakeDuration && currentShakeCurve != null)
        {
            float curveValue = currentShakeCurve.Evaluate(shakeElapsed);
            float x = (Mathf.PerlinNoise(0, shakeElapsed * 20f) - 0.5f) * 2f * currentShakeMagnitude * curveValue;
            float y = (Mathf.PerlinNoise(100, shakeElapsed * 20f) - 0.5f) * 2f * currentShakeMagnitude * curveValue;

            Vector3 localShake = new Vector3(x, y, 0);
            appliedShakeOffset = transform.right * localShake.x + transform.up * localShake.y;

            shakeElapsed += Time.unscaledDeltaTime;
        }
        else if (shakeElapsed >= shakeDuration)
        {
            appliedShakeOffset = Vector3.Lerp(appliedShakeOffset, Vector3.zero, Time.deltaTime * 10f);
            if (appliedShakeOffset.sqrMagnitude < 0.00001f)
            {
                appliedShakeOffset = Vector3.zero;
                currentShakeCurve = null;
            }
        }

        transform.position += appliedShakeOffset;
    }

    // ═══════════════════════════════════════
    //  锁定标记（屏幕UI图层，悬浮在敌人位置）
    // ═══════════════════════════════════════

    private static Texture2D markerTexture;
    private Canvas markerCanvas;
    private UnityEngine.UI.RawImage markerImage;

    private void CreateLockOnMarker()
    {
        if (lockedTarget == null) return;

        // 生成圆环纹理（仅一次）
        if (markerTexture == null)
            markerTexture = GenerateRingTexture();

        // 创建 Canvas（覆盖在UI最上层）
        GameObject canvasGO = new GameObject("LockOnCanvas");
        markerCanvas = canvasGO.AddComponent<Canvas>();
        markerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        markerCanvas.sortingOrder = 9999;

        // 创建 RawImage
        GameObject imgGO = new GameObject("LockOnImage");
        imgGO.transform.SetParent(markerCanvas.transform, false);
        markerImage = imgGO.AddComponent<UnityEngine.UI.RawImage>();
        markerImage.texture = markerTexture;
        markerImage.rectTransform.sizeDelta = new Vector2(48, 48);
        markerImage.color = Color.white;

        // 设为中心锚点
        markerImage.rectTransform.anchorMin = Vector2.zero;
        markerImage.rectTransform.anchorMax = Vector2.zero;
    }

    private void DestroyLockOnMarker()
    {
        if (markerImage != null)
        {
            Destroy(markerImage.gameObject);
            markerImage = null;
        }
        if (markerCanvas != null)
        {
            Destroy(markerCanvas.gameObject);
            markerCanvas = null;
        }
    }

    /// <summary>
    /// 把锁定标记移到敌人在屏幕上的位置
    /// </summary>
    private void UpdateLockOnMarkerPosition()
    {
        if (markerImage == null || lockedTarget == null) return;

        // 取敌人胸口位置的屏幕坐标
        Vector3 worldPos = lockedTarget.position + Vector3.up * 1.2f;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

        // 在屏幕内才显示
        if (screenPos.z > 0)
        {
            markerImage.rectTransform.anchoredPosition = new Vector2(screenPos.x, screenPos.y);
            markerImage.enabled = true;
        }
        else
        {
            markerImage.enabled = false;
        }
    }

    /// <summary>
    /// 生成锁定标记纹理：实心圆 + 外圈环，白色
    /// </summary>
    private static Texture2D GenerateRingTexture()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(size / 2f, size / 2f);
        Color fill = new Color(1f, 1f, 1f, 0.6f);        // 白色实心圆（半透）
        Color ring = new Color(1f, 1f, 1f, 0.8f);        // 白色外环（更亮）
        Color clear = Color.clear;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                if (d <= 16f)                                  // 实心圆
                    tex.SetPixel(x, y, fill);
                else if (d >= 24f && d <= 30f)                 // 外环（紧贴圆边）
                    tex.SetPixel(x, y, ring);
                else
                    tex.SetPixel(x, y, clear);
            }
        }
        tex.Apply();
        return tex;
    }

    public Quaternion PlanarRotation => Quaternion.Euler(0, rotationY, 0);
}
