using UnityEngine;
using UnityEditor;

public class ExtractFBXAnimation : EditorWindow
{
    [MenuItem("Assets/Extract Selected Animation from FBX", false, 30)]
    static void Extract()
    {
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!path.EndsWith(".fbx")) continue;

            // 获取 FBX 里的所有动画片段
            var clips = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var asset in clips)
            {
                if (asset is AnimationClip clip)
                {
                    // 创建新的独立动画文件
                    AnimationClip newClip = new AnimationClip();
                    EditorUtility.CopySerialized(clip, newClip);
                    string newPath = $"Assets/{clip.name}.anim";
                    AssetDatabase.CreateAsset(newClip, newPath);
                    Debug.Log($"Extracted {clip.name} to {newPath}");
                }
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}