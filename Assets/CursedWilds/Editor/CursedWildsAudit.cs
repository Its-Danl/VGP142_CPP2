using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CursedWilds.Editor
{
    /// <summary>Read-only project report used before recovery changes.</summary>
    public static class CursedWildsAudit
    {
        [MenuItem("Cursed Wilds/Audit Existing Project")]
        public static void AuditProject()
        {
            Debug.Log("=== CURSED WILDS RECOVERY AUDIT BEGIN ===");
            ReportPrefab("Assets/Prefabs/PlayerPrefab.prefab");
            ReportPrefab("Assets/NatureStarterKit/Models/Tree.prefab");
            ReportPrefab("Assets/CursedWilds/Prefabs/MeleeEnemy.prefab");
            ReportPrefab("Assets/CursedWilds/Prefabs/TurretEnemy.prefab");
            ReportPrefab("Assets/CursedWilds/Prefabs/ChargerEnemy.prefab");
            ReportMissingPrefabScripts();
            ReportScene("Assets/CursedWilds/Scenes/MainMenu.unity");
            ReportScene("Assets/CursedWilds/Scenes/CursedWilds.unity");
            Debug.Log("=== CURSED WILDS RECOVERY AUDIT END ===");
        }

        private static void ReportPrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogWarning($"AUDIT prefab missing: {path}"); return; }
            try
            {
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                var animators = root.GetComponentsInChildren<Animator>(true);
                int missing = CountMissing(root);
                Debug.Log($"AUDIT PREFAB {path}: root={root.name}, children={root.GetComponentsInChildren<Transform>(true).Length}, components={root.GetComponentsInChildren<Component>(true).Length}, renderers={renderers.Length}, animators={animators.Length}, missingScripts={missing}");
                foreach (var animator in animators)
                    Debug.Log($"AUDIT ANIMATOR {path}: {animator.transform.GetHierarchyPath()} controller={(animator.runtimeAnimatorController == null ? "<none>" : animator.runtimeAnimatorController.name)}, avatar={(animator.avatar == null ? "<none>" : animator.avatar.name)}");
                foreach (var renderer in renderers)
                    Debug.Log($"AUDIT RENDERER {path}: {renderer.transform.GetHierarchyPath()} enabled={renderer.enabled}, materials={string.Join(",", renderer.sharedMaterials.Select(m => m == null ? "<null>" : m.shader == null ? m.name + ":<no shader>" : m.name + ":" + m.shader.name))}");
                foreach (var component in root.GetComponents<Component>())
                    Debug.Log($"AUDIT ROOT COMPONENT {path}: {(component == null ? "<missing>" : component.GetType().FullName)}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ReportScene(string path)
        {
            if (!System.IO.File.Exists(path)) { Debug.LogWarning($"AUDIT scene missing: {path}"); return; }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int roots = scene.rootCount;
            int objects = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Transform>(true).Length);
            int missing = scene.GetRootGameObjects().Sum(CountMissing);
            var terrain = UnityEngine.Object.FindAnyObjectByType<Terrain>();
            var allObjects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(transform => transform.gameObject);
            int enemies = allObjects.Count(gameObject => gameObject.GetComponent<EnemyController>() != null);
            int hazards = allObjects.Count(gameObject => gameObject.GetComponent<HazardVolume>() != null);
            int collectibles = allObjects.Count(gameObject => gameObject.GetComponent<CollectibleController>() != null);
            Debug.Log($"AUDIT SCENE {path}: roots={roots}, objects={objects}, missingScripts={missing}, enemies={enemies}, hazards={hazards}, collectibles={collectibles}, terrain={(terrain == null ? "<none>" : terrain.terrainData.size.ToString())}");
        }

        private static void ReportMissingPrefabScripts()
        {
            int affected = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int missing = CountMissing(root);
                    if (missing > 0) { affected++; Debug.LogWarning($"AUDIT MISSING SCRIPT PREFAB {path}: {missing}"); }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log($"AUDIT PREFAB MISSING-SCRIPT SUMMARY: affected={affected}");
        }

        private static int CountMissing(GameObject gameObject) =>
            GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) + gameObject.GetComponentsInChildren<Transform>(true).Skip(1).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));

        private static string GetHierarchyPath(this Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }
    }
}
