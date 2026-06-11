using UnityEngine;
using UnityEditor;

public class CalibrateWeaponMount : EditorWindow
{
    public Transform rightHandMount;
    public Transform leftHandMount;
    public GameObject weapon;

    [MenuItem("Tools/校准武器挂点")]
    public static void ShowWindow()
    {
        GetWindow<CalibrateWeaponMount>("校准武器挂点");
    }

    void OnGUI()
    {
        GUILayout.Label("将武器分别在右手和左手挂点下对齐", EditorStyles.boldLabel);
        rightHandMount = (Transform)EditorGUILayout.ObjectField("右手挂点 (handMount)", rightHandMount, typeof(Transform), true);
        leftHandMount = (Transform)EditorGUILayout.ObjectField("左手挂点 (leftHandMount)", leftHandMount, typeof(Transform), true);
        weapon = (GameObject)EditorGUILayout.ObjectField("武器物体", weapon, typeof(GameObject), true);

        if (GUILayout.Button("步骤1: 记录右手姿态"))
        {
            RecordRightHandPose();
        }

        if (GUILayout.Button("步骤2: 对齐左手姿态"))
        {
            AlignLeftHandPose();
        }
    }

    private Quaternion targetWorldRot;

    void RecordRightHandPose()
    {
        if (rightHandMount == null || weapon == null)
        {
            Debug.LogError("请分配右手挂点和武器物体");
            return;
        }

        // 临时将武器设为右手挂点的子物体，并重置局部变换
        Transform originalParent = weapon.transform.parent;
        weapon.transform.SetParent(rightHandMount);
        weapon.transform.localPosition = Vector3.zero;
        weapon.transform.localRotation = Quaternion.identity;
        weapon.transform.localScale = Vector3.one;

        // 记录武器的世界旋转
        targetWorldRot = weapon.transform.rotation;
        Debug.Log($"已记录右手姿态下的武器世界旋转: {targetWorldRot.eulerAngles}");

        // 恢复原父级（可选：恢复前先断开，避免影响场景）
        weapon.transform.SetParent(originalParent);
        Selection.activeObject = leftHandMount;
        EditorUtility.DisplayDialog("校准", "右手姿态已记录。现在请点击「步骤2: 对齐左手姿态」", "OK");
    }

    void AlignLeftHandPose()
    {
        if (leftHandMount == null || weapon == null || targetWorldRot == Quaternion.identity)
        {
            Debug.LogError("请先执行步骤1，或分配左手挂点/武器");
            return;
        }

        // 临时将武器设为左手挂点的子物体，重置局部变换
        Transform originalParent = weapon.transform.parent;
        weapon.transform.SetParent(leftHandMount);
        weapon.transform.localPosition = Vector3.zero;
        weapon.transform.localRotation = Quaternion.identity;
        weapon.transform.localScale = Vector3.one;

        // 计算：左手挂点应该具有的世界旋转 = 当前武器世界旋转的逆 * targetWorldRot ?
        // 更简单：直接让左手挂点的父物体旋转不变，调整左手挂点本身的局部旋转，使得武器世界旋转 = targetWorldRot
        // 设 LeftHandMount 的父物体世界旋转为 parentWorldRot
        // 则武器世界旋转 = parentWorldRot * leftHandMount.localRotation * weapon.localRotation(单位)
        // 要求武器世界旋转 = targetWorldRot，所以 leftHandMount.localRotation = parentWorldRot^{-1} * targetWorldRot
        // 但 parentWorldRot 未知，我们可以通过当前武器世界旋转来反推：
        Quaternion currentWeaponWorldRot = weapon.transform.rotation;
        // leftHandMount 当前世界旋转 = currentWeaponWorldRot (因为 weapon.localRotation 为单位)
        // 我们希望 leftHandMount 的世界旋转变成 targetWorldRot
        // 所以需要将 leftHandMount 的局部旋转调整为：newLocalRot = leftHandMount.parent.rotation^{-1} * targetWorldRot
        Transform parent = leftHandMount.parent;
        Quaternion newLocalRot = Quaternion.Inverse(parent.rotation) * targetWorldRot;

        // 应用新的局部旋转
        Undo.RecordObject(leftHandMount, "Align Left Hand Mount");
        leftHandMount.localRotation = newLocalRot;

        // 同时可能也需要微调位置，但这里只处理旋转
        Debug.Log($"已设置左手挂点局部旋转为: {newLocalRot.eulerAngles}");

        // 恢复武器原父级
        weapon.transform.SetParent(originalParent);

        EditorUtility.DisplayDialog("校准完成", "左手挂点已对齐。现在运行游戏，切换格挡后武器应该不会再扭动。", "OK");
    }
}