using UnityEditor;
using UnityEngine;

namespace CursedWilds.Editor
{
    public static class AnimationInventory
    {
        public static void Report()
        {
            foreach (string path in new[]
            {
                "Assets/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx",
                "Assets/Universal Animation Library 2[Standard]/Female Mannequin/Unity/Mannequin_F.fbx",
                "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Models/Armature.fbx"
            })
            {
                Debug.Log("ANIMATION ASSET " + path);
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (clip is AnimationClip animationClip) Debug.Log("ANIMATION CLIP " + animationClip.name + " length=" + animationClip.length);
            }
        }
    }
}
