using UnityEngine;
using System.Collections.Generic;

public class PoseSnapshot : MonoBehaviour
{
    private Animator anim;
    private Dictionary<HumanBodyBones, PoseData> idlePose;

    [System.Serializable]
    public struct PoseData
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
    }

    void Awake()
    {
        anim = GetComponent<Animator>();
        // 默认自动录制，但你也可以注释掉，改用右键手动录制
        StartCoroutine(CaptureIdlePose());
    }

    System.Collections.IEnumerator CaptureIdlePose()
    {
        yield return null;
        yield return null;
        RecordCurrentPose();
        Debug.Log("Idle 姿势已录制（自动）");
    }

    [ContextMenu("Record Current Pose")]
    public void RecordCurrentPose()
    {
        if (anim == null) anim = GetComponent<Animator>();
        idlePose = new Dictionary<HumanBodyBones, PoseData>();

        foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;
            Transform boneTransform = anim.GetBoneTransform(bone);
            if (boneTransform != null)
            {
                idlePose[bone] = new PoseData
                {
                    localPosition = boneTransform.localPosition,
                    localRotation = boneTransform.localRotation
                };
            }
        }
    }

    public void RestoreIdlePose()
    {
        if (idlePose == null || anim == null) return;
        foreach (var kvp in idlePose)
        {
            Transform bone = anim.GetBoneTransform(kvp.Key);
            if (bone != null)
            {
                bone.localPosition = kvp.Value.localPosition;
                bone.localRotation = kvp.Value.localRotation;
            }
        }
    }
}