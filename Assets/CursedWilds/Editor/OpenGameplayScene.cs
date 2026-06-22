using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace CursedWilds.Editor
{
    internal static class OpenGameplayScene
    {
        private const string GameplayScene = "Assets/CursedWilds/Scenes/CursedWilds.unity";

        [MenuItem("Cursed Wilds/Open Gameplay %#g")]
        private static void Open()
        {
            EditorSceneManager.OpenScene(GameplayScene, OpenSceneMode.Single);
        }
    }
}
