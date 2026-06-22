using System.Collections.Generic;
using System.Linq;
using CursedWilds;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CursedWilds.Editor
{
    public static class CursedWildsBuilder
    {
        private const string Root = "Assets/CursedWilds";
        private static Material toxic, fire, bramble, relic, enemyA, enemyB, enemyC;

        [MenuItem("Cursed Wilds/Rebuild Complete Game %&g")]
        public static void BuildGame()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            EnsureFolders(); CleanLegacyPrefabs(); CreateMaterials();
            AnimatorController enemyAnimator = CreateEnemyAnimatorController();
            AnimatorController playerAnimator = CreatePlayerCombatAnimator();
            GameObject projectile = CreateProjectilePrefab();
            GameObject melee = CreateEnemyPrefab("MeleeEnemy", enemyA, 70f, typeof(MeleeEnemy), projectile, enemyAnimator);
            GameObject turret = CreateEnemyPrefab("TurretEnemy", enemyB, 85f, typeof(TurretEnemy), projectile, enemyAnimator);
            GameObject charger = CreateEnemyPrefab("ChargerEnemy", enemyC, 110f, typeof(ChargerEnemy), projectile, enemyAnimator);
            BuildMainMenu(); BuildGameplay(melee, turret, charger, playerAnimator); SetBuildScenes();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("Cursed Wilds scenes and prefabs built successfully.");
        }

        [MenuItem("Cursed Wilds/Build Windows %&b")]
        public static void BuildWindows()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string output = "BuildFinal/CursedWilds.exe";
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Root + "/Scenes/MainMenu.unity", Root + "/Scenes/CursedWilds.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.CleanBuildCache | BuildOptions.CompressWithLz4HC
            });
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.Exception("Windows build failed: " + result.summary.result);
            Debug.Log("Cursed Wilds Windows build completed: " + output);
        }

        public static void BuildGameplaySmoke()
        {
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Root + "/Scenes/CursedWilds.unity" },
                locationPathName = "BuildSmoke/CursedWilds.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.CleanBuildCache | BuildOptions.CompressWithLz4HC
            });
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.Exception("Gameplay smoke build failed: " + result.summary.result);
            Debug.Log("Gameplay smoke build completed.");
        }

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { Root, Root + "/Scenes", Root + "/Prefabs", Root + "/Materials", Root + "/Generated" })
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(folder[..folder.LastIndexOf('/')], folder[(folder.LastIndexOf('/') + 1)..]);
        }
        private static void CleanLegacyPrefabs()
        {
            const string legacyFireball = "Assets/Prefabs/Fireball.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(legacyFireball) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(legacyFireball);
            try
            {
                RemoveMissingScripts(root);
                PrefabUtility.SaveAsPrefabAsset(root, legacyFireball);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void CreateMaterials()
        {
            toxic = MaterialAsset("Toxic", new Color(.12f, .8f, .35f)); fire = MaterialAsset("Fire", new Color(1f, .2f, .02f)); bramble = MaterialAsset("Bramble", new Color(.24f, .06f, .32f)); relic = MaterialAsset("Relic", new Color(1f, .78f, .08f), true);
            enemyA = MaterialAsset("Melee", new Color(.45f, .08f, .12f)); enemyB = MaterialAsset("Turret", new Color(.2f, .1f, .5f)); enemyC = MaterialAsset("Charger", new Color(.5f, .05f, .55f));
        }
        private static Material MaterialAsset(string name, Color color, bool emission = false)
        {
            string path = Root + "/Materials/" + name + ".mat"; var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = color;
            if (emission) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2f); }
            AssetDatabase.CreateAsset(material, path); return material;
        }
        private static GameObject CreateProjectilePrefab()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "CursedBolt"; go.transform.localScale = Vector3.one * .28f; go.GetComponent<Renderer>().sharedMaterial = relic; Object.DestroyImmediate(go.GetComponent<Collider>());
            return SavePrefab(go, "CursedBolt");
        }
        private static AnimatorController CreateEnemyAnimatorController()
        {
            const string modelPath = "Assets/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx";
            string path = Root + "/Generated/EnemyHumanoid.controller";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Where(clip => !clip.name.StartsWith("__preview__")).ToDictionary(clip => clip.name, clip => clip);
            AnimationClip Clip(string key) => clips.First(pair => pair.Key.EndsWith(key)).Value;
            var stateMachine = controller.layers[0].stateMachine;
            var idle = stateMachine.AddState("Idle"); idle.motion = Clip("Zombie_Idle_Loop"); stateMachine.defaultState = idle;
            var walk = stateMachine.AddState("Walk"); walk.motion = Clip("Zombie_Walk_Fwd_Loop");
            var attack = stateMachine.AddState("Attack"); attack.motion = Clip("Melee_Hook");
            var dead = stateMachine.AddState("Dead"); dead.motion = Clip("Hit_Knockback");
            var toWalk = idle.AddTransition(walk); toWalk.AddCondition(AnimatorConditionMode.Greater, .05f, "Speed"); toWalk.hasExitTime = false;
            var toIdle = walk.AddTransition(idle); toIdle.AddCondition(AnimatorConditionMode.Less, .05f, "Speed"); toIdle.hasExitTime = false;
            var attackTransition = stateMachine.AddAnyStateTransition(attack); attackTransition.AddCondition(AnimatorConditionMode.If, 0f, "Attack"); attackTransition.hasExitTime = false; attackTransition.duration = .05f;
            var attackExit = attack.AddTransition(idle); attackExit.hasExitTime = true; attackExit.exitTime = .9f; attackExit.duration = .08f;
            var deadTransition = stateMachine.AddAnyStateTransition(dead); deadTransition.AddCondition(AnimatorConditionMode.If, 0f, "Dead"); deadTransition.hasExitTime = false; deadTransition.duration = .05f;
            AssetDatabase.SaveAssets();
            return controller;
        }
        private static AnimatorController CreatePlayerCombatAnimator()
        {
            const string sourcePath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
            const string modelPath = "Assets/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx";
            string path = Root + "/Generated/PlayerCombat.controller";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            if (!AssetDatabase.CopyAsset(sourcePath, path)) throw new System.InvalidOperationException("Could not create the player combat Animator controller.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!controller.parameters.Any(parameter => parameter.name == "Attack")) controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            AnimationClip attackClip = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().First(clip => clip.name.EndsWith("Melee_Hook"));
            var layerMachine = new AnimatorStateMachine { name = "Combat Layer" };
            AssetDatabase.AddObjectToAsset(layerMachine, controller);
            var layer = new AnimatorControllerLayer { name = "Combat", defaultWeight = 1f, blendingMode = AnimatorLayerBlendingMode.Override, stateMachine = layerMachine };
            controller.AddLayer(layer);
            var empty = layerMachine.AddState("Empty"); layerMachine.defaultState = empty;
            var attack = layerMachine.AddState("Punch"); attack.motion = attackClip;
            var enter = layerMachine.AddAnyStateTransition(attack); enter.AddCondition(AnimatorConditionMode.If, 0f, "Attack"); enter.hasExitTime = false; enter.duration = .05f;
            var exit = attack.AddTransition(empty); exit.hasExitTime = true; exit.exitTime = .92f; exit.duration = .08f;
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); return controller;
        }
        private static GameObject CreateEnemyPrefab(string name, Material material, float healthValue, System.Type controllerType, GameObject projectile, AnimatorController enemyAnimator)
        {
            const string modelPath = "Assets/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null) throw new System.InvalidOperationException("Required UAL enemy model is missing.");
            var root = new GameObject(name);
            // The animation-library FBX carries editor-only helper behaviours.  Fully unpack the
            // instance before saving our gameplay prefab so those unresolved source components
            // cannot be serialized into any enemy variant.
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model"; model.transform.SetParent(root.transform, false); model.transform.localScale = Vector3.one;
            var animator = model.GetComponent<Animator>(); if (animator == null) animator = model.AddComponent<Animator>(); animator.runtimeAnimatorController = enemyAnimator; animator.applyRootMotion = false;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
            var collider = root.AddComponent<CapsuleCollider>(); collider.height = 1.8f; collider.radius = .35f; collider.center = new Vector3(0f, .9f, 0f);
            var health = root.AddComponent<Health>(); health.Configure(healthValue);
            var agent = root.AddComponent<NavMeshAgent>(); agent.radius = .35f; agent.height = 1.8f; agent.speed = controllerType == typeof(ChargerEnemy) ? 4.5f : 3.5f; agent.stoppingDistance = 1.7f;
            if (controllerType == typeof(MeleeEnemy)) root.AddComponent<MeleeEnemy>();
            else if (controllerType == typeof(TurretEnemy)) { agent.enabled = false; root.AddComponent<TurretEnemy>().Configure(projectile); }
            else root.AddComponent<ChargerEnemy>();
            CreateWorldBar(root.transform);
            if (controllerType == typeof(MeleeEnemy)) CreateVisualPart(root.transform, "Claw", new Vector3(.35f, 1.05f, .45f), new Vector3(.14f, .14f, .65f), relic);
            if (controllerType == typeof(TurretEnemy)) { CreateVisualPart(root.transform, "Arcane Staff", new Vector3(0f, 1.05f, .6f), new Vector3(.18f, .18f, 1.25f), relic); CreateVisualPart(root.transform, "Focus", new Vector3(0f, 1.55f, 0f), new Vector3(.55f, .2f, .55f), relic); }
            if (controllerType == typeof(ChargerEnemy)) { CreateVisualPart(root.transform, "Horn L", new Vector3(-.28f, 1.65f, .2f), new Vector3(.12f, .12f, .65f), relic); CreateVisualPart(root.transform, "Horn R", new Vector3(.28f, 1.65f, .2f), new Vector3(.12f, .12f, .65f), relic); }
            RemoveMissingScripts(root);
            return SavePrefab(root, name);
        }
        private static Transform CreateVisualPart(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name; part.transform.SetParent(parent); part.transform.localPosition = position; part.transform.localScale = scale; part.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(part.GetComponent<Collider>()); return part.transform;
        }
        private static void CreateWorldBar(Transform parent)
        {
            var bar = new GameObject("HealthBar"); bar.transform.SetParent(parent); bar.transform.localPosition = new Vector3(0f, 2.75f, 0f); bar.transform.localScale = new Vector3(1.4f, .16f, .1f);
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube); back.transform.SetParent(bar.transform); back.transform.localScale = Vector3.one; back.GetComponent<Renderer>().sharedMaterial = MaterialAsset("HealthBack", Color.black); Object.DestroyImmediate(back.GetComponent<Collider>());
            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube); fill.transform.SetParent(bar.transform); fill.name = "Fill"; fill.transform.localPosition = new Vector3(-.5f, 0f, -.06f); fill.transform.localScale = new Vector3(1f, .65f, .3f); fill.GetComponent<Renderer>().sharedMaterial = MaterialAsset("HealthFill", Color.green); Object.DestroyImmediate(fill.GetComponent<Collider>());
            var script = bar.AddComponent<WorldHealthBar>(); Set(script, "fill", fill.transform);
        }
        private static void BuildMainMenu()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); var camera = new GameObject("Menu Camera").AddComponent<Camera>(); camera.transform.position = new Vector3(0f, 2f, -10f); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .01f, .06f);
            new GameObject("SaveSystem").AddComponent<SaveSystem>(); var menu = new GameObject("MenuController").AddComponent<MenuController>(); var audioObject = new GameObject("AudioManager"); audioObject.AddComponent<AudioSource>(); var audio = audioObject.AddComponent<AudioManager>();
            var canvas = CreateCanvas("Main Menu Canvas"); var panel = CreatePanel(canvas.transform, "Cursed Wilds", new Color(.1f, .025f, .16f, .94f));
            AddText(panel.transform, "Title", "CURSED WILDS", 68, new Vector2(0f, 180f)); AddText(panel.transform, "Subtitle", "Cleanse the woodland. Recover the Heartwood Relic.", 22, new Vector2(0f, 110f));
            AddButton(panel.transform, "Play", new Vector2(0f, 15f), menu.Play); AddButton(panel.transform, "Quit", new Vector2(0f, -65f), menu.Quit);
            SaveScene("MainMenu");
        }
        private static void BuildGameplay(GameObject melee, GameObject turret, GameObject charger, AnimatorController playerAnimator)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); RenderSettings.ambientLight = new Color(.26f, .18f, .32f); RenderSettings.fog = true; RenderSettings.fogColor = new Color(.12f, .07f, .18f); RenderSettings.fogDensity = .008f;
            var light = new GameObject("Moonlight").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.color = new Color(.55f, .45f, 1f); light.transform.rotation = Quaternion.Euler(50f, -25f, 0f);
            var terrain = CreateTerrain(); var player = CreatePlayer(OnTerrain(250f, 55f), playerAnimator); new GameObject("SaveSystem").AddComponent<SaveSystem>(); var game = new GameObject("GameFlow").AddComponent<GameFlowManager>(); Set(game, "playerHealth", player.GetComponent<Health>());
            var audioObject = new GameObject("AudioManager"); audioObject.AddComponent<AudioSource>(); var audio = audioObject.AddComponent<AudioManager>();
            var canvas = CreateCanvas("Gameplay UI"); CreateHud(canvas.transform, player.GetComponent<Health>(), game, audio, out Text objectiveText, out GameObject over, out GameObject win); Set(game, "gameOverPanel", over); Set(game, "victoryPanel", win); CreatePauseMenu(canvas.transform, game, audio);
            var objective = new GameObject("ObjectiveTracker").AddComponent<ObjectiveTracker>(); Set(objective, "label", objectiveText);
            CreateCollectible("Healing Bloom", OnTerrain(260f, 78f), CollectibleKind.Heal, toxic, objective); CreateCollectible("Swift Charm", OnTerrain(345f, 140f), CollectibleKind.Speed, relic, objective); CreateCollectible("Shield Charm", OnTerrain(180f, 360f), CollectibleKind.Shield, enemyB, objective); CreateCollectible("Heartwood Relic", OnTerrain(375f, 365f), CollectibleKind.HeartwoodRelic, relic, objective);
            CreatePathsAndRuins(); CreateHazards(); CreateEnvironmentalDetail(); var director = new GameObject("Enemy Spawn Director").AddComponent<EnemySpawnDirector>(); ConfigureSpawns(director, melee, turret, charger);
            var surface = terrain.gameObject.AddComponent<NavMeshSurface>(); surface.BuildNavMesh();
            SetInactive("Paused"); SetInactive("Game Over"); SetInactive("Victory");
            RemoveMissingScripts(SceneManager.GetActiveScene());
            SaveScene("CursedWilds");
        }
        private static void SetInactive(string name)
        {
            foreach (var transform in Object.FindObjectsByType<Transform>())
                if (transform.name == name) transform.gameObject.SetActive(false);
        }
        private static Terrain CreateTerrain()
        {
            string terrainPath = Root + "/Generated/CursedWildsTerrain.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(terrainPath) != null) AssetDatabase.DeleteAsset(terrainPath);
            var data = new TerrainData { heightmapResolution = 513, alphamapResolution = 512, size = new Vector3(500f, 600f, 500f) }; float[,] heights = new float[513, 513];
            for (int z = 0; z < 513; z++) for (int x = 0; x < 513; x++)
            {
                float nx = x / 512f, nz = z / 512f;
                float ridge = Mathf.PerlinNoise(nx * 2.4f + 9f, nz * 2.4f + 4f) * .065f;
                float detail = Mathf.PerlinNoise(nx * 11f, nz * 11f) * .012f;
                float clearing = Mathf.Exp(-((nx - .5f) * (nx - .5f) + (nz - .14f) * (nz - .14f)) * 24f) * .045f;
                heights[z, x] = .035f + ridge + detail - clearing;
            }
            data.SetHeights(0, 0, heights);
            data.terrainLayers = new[]
            {
                TerrainLayerAsset("GrassLayer", "Assets/NatureStarterKit/Textures/groundgrass01.tga", "Assets/NatureStarterKit/Textures/groundgrass01N.tga", 16f),
                TerrainLayerAsset("EarthLayer", "Assets/NatureStarterKit/Textures/ground01.tga", "Assets/NatureStarterKit/Textures/ground01N.tga", 20f),
                TerrainLayerAsset("BarkLayer", "Assets/NatureStarterKit/Textures/bark01.tga", "Assets/NatureStarterKit/Textures/bark01N.tga", 12f)
            };
            float[,,] splat = new float[512, 512, 3];
            for (int z = 0; z < 512; z++) for (int x = 0; x < 512; x++)
            {
                float nx = x / 511f, nz = z / 511f;
                float pathBlend = Mathf.Clamp01(1f - Mathf.Min(Mathf.Abs(nx - .5f) * 13f, Mathf.Abs(nz - .52f) * 13f));
                float cursed = Mathf.PerlinNoise(nx * 8f, nz * 8f) > .72f ? .22f : 0f;
                float earth = Mathf.Clamp01(pathBlend * .72f + cursed); float bark = cursed * .4f; float grass = Mathf.Max(0f, 1f - earth - bark); float total = grass + earth + bark;
                splat[z, x, 0] = grass / total; splat[z, x, 1] = earth / total; splat[z, x, 2] = bark / total;
            }
            data.SetAlphamaps(0, 0, splat);
            AssetDatabase.CreateAsset(data, terrainPath); var terrainObject = Terrain.CreateTerrainGameObject(data); terrainObject.name = "Cursed Wilds Terrain"; var terrain = terrainObject.GetComponent<Terrain>(); terrain.drawInstanced = true; return terrain;
        }
        private static TerrainLayer TerrainLayerAsset(string name, string diffusePath, string normalPath, float tileSize)
        {
            string path = Root + "/Generated/" + name + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath); layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath); layer.tileSize = Vector2.one * tileSize; EditorUtility.SetDirty(layer); return layer;
        }
        private static void CreatePathsAndRuins()
        {
            Material pathMaterial = MaterialAsset("Path Stone", new Color(.22f, .18f, .14f)); Material ruinMaterial = MaterialAsset("Ruin Stone", new Color(.32f, .3f, .35f));
            for (int i = 0; i < 22; i++)
            {
                // Keep the first tile beyond the third-person camera's starting sightline.
                float z = 92f + i * 15f; var segment = GameObject.CreatePrimitive(PrimitiveType.Cube); segment.name = "Worn Path"; segment.transform.position = OnTerrain(250f + Mathf.Sin(i * .75f) * 8f, z, .08f); segment.transform.localScale = new Vector3(11f, .15f, 13f); segment.GetComponent<Renderer>().sharedMaterial = pathMaterial;
            }
            for (int ruin = 0; ruin < 3; ruin++)
            {
                Vector2 center = ruin == 0 ? new Vector2(120f, 115f) : ruin == 1 ? new Vector2(385f, 235f) : new Vector2(185f, 390f);
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * 60f * Mathf.Deg2Rad; var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube); pillar.name = "Forgotten Ruin Pillar"; pillar.transform.position = OnTerrain(center.x + Mathf.Cos(angle) * 8f, center.y + Mathf.Sin(angle) * 8f, 2.2f); pillar.transform.localScale = new Vector3(1.4f, 4.4f, 1.4f); pillar.transform.rotation = Quaternion.Euler(0f, i * 60f, i % 2 == 0 ? 6f : -5f); pillar.GetComponent<Renderer>().sharedMaterial = ruinMaterial;
                }
            }
        }
        private static GameObject CreatePlayer(Vector3 position, AnimatorController playerAnimator)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerPrefab.prefab");
            if (source == null) throw new System.InvalidOperationException("Required player prefab is missing: Assets/Prefabs/PlayerPrefab.prefab");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(source); player.name = "Player"; player.tag = "Player"; player.transform.position = position;
            // Starter Assets owns movement, gravity, jumping, and Mecanim animation. Cursed Wilds only layers gameplay on it.
            player.GetComponent<Animator>().runtimeAnimatorController = playerAnimator;
            player.AddComponent<Health>(); player.AddComponent<PlayerMovementEffects>(); player.AddComponent<PlayerStatus>(); player.AddComponent<PlayerCombat>();
            foreach (var listener in player.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(listener);
            var cameraGo = new GameObject("Third Person Camera"); cameraGo.tag = "MainCamera"; var camera = cameraGo.AddComponent<Camera>(); cameraGo.AddComponent<AudioListener>(); var follow = cameraGo.AddComponent<ThirdPersonCamera>(); follow.Configure(player.transform); cameraGo.transform.position = position + new Vector3(0f, 3f, -6f); cameraGo.transform.LookAt(player.transform.position + Vector3.up);
            return player;
        }
        private static void CreateCollectible(string name, Vector3 position, CollectibleKind kind, Material material, ObjectiveTracker tracker)
        {
            var go = new GameObject(name); go.transform.position = position; var collider = go.AddComponent<SphereCollider>(); collider.radius = kind == CollectibleKind.HeartwoodRelic ? 1.5f : 1f; collider.isTrigger = true;
            if (kind == CollectibleKind.Heal)
            {
                CreateOrb(go.transform, "Bloom Core", Vector3.up * .8f, Vector3.one * .52f, material);
                for (int i = 0; i < 6; i++) { float angle = i * 60f * Mathf.Deg2Rad; CreateOrb(go.transform, "Bloom Petal", new Vector3(Mathf.Cos(angle) * .55f, .75f, Mathf.Sin(angle) * .55f), new Vector3(.42f,.18f,.42f), relic); }
            }
            else if (kind == CollectibleKind.Speed)
            {
                var crystal = GameObject.CreatePrimitive(PrimitiveType.Cylinder); crystal.name = "Wind Crystal"; crystal.transform.SetParent(go.transform, false); crystal.transform.localPosition = Vector3.up * .9f; crystal.transform.localScale = new Vector3(.45f, 1.35f, .45f); crystal.transform.rotation = Quaternion.Euler(0f, 0f, 45f); crystal.GetComponent<Renderer>().sharedMaterial = material;
            }
            else if (kind == CollectibleKind.Shield)
            {
                CreateOrb(go.transform, "Shield Heart", Vector3.up * .85f, Vector3.one * .45f, material);
                for (int i = 0; i < 3; i++) { var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder); ring.name = "Shield Ring"; ring.transform.SetParent(go.transform, false); ring.transform.localPosition = Vector3.up * .85f; ring.transform.localScale = new Vector3(1.15f, .05f, 1.15f); ring.transform.rotation = Quaternion.Euler(i * 55f, i * 40f, 0f); ring.GetComponent<Renderer>().sharedMaterial = relic; }
            }
            else
            {
                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder); trunk.name = "Heartwood Trunk"; trunk.transform.SetParent(go.transform, false); trunk.transform.localPosition = Vector3.up * 1.1f; trunk.transform.localScale = new Vector3(.72f, 1.8f, .72f); trunk.GetComponent<Renderer>().sharedMaterial = MaterialAsset("Heartwood Bark", new Color(.32f,.12f,.05f));
                CreateOrb(go.transform, "Heartwood Crown", Vector3.up * 2.35f, Vector3.one * 1.05f, material);
            }
            var collect = go.AddComponent<CollectibleController>(); Set(collect, "kind", kind); Set(collect, "objectives", tracker);
        }
        private static void CreateOrb(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = name; orb.transform.SetParent(parent, false); orb.transform.localPosition = position; orb.transform.localScale = scale; orb.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(orb.GetComponent<Collider>());
        }
        private static void CreateHazards()
        {
            Vector3[] positions = { new(100, 34, 100), new(130,34,225), new(210,34,115), new(290,34,230), new(390,34,260), new(75,34,350), new(230,34,315), new(315,34,395), new(420,34,410), new(110,34,430), new(445,34,120), new(50,34,250) };
            for (int i = 0; i < positions.Length; i++)
            {
                Material mat = i % 3 == 0 ? toxic : i % 3 == 1 ? fire : bramble; var type = i % 3 == 0 ? "Toxic Bog" : i % 3 == 1 ? "Fire Pit" : "Cursed Brambles";
                var go = new GameObject(type + " " + (i + 1)); go.transform.position = OnTerrain(positions[i].x, positions[i].z, .1f); var trigger = go.AddComponent<SphereCollider>(); trigger.radius = 4f; trigger.isTrigger = true;
                var baseDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder); baseDisk.name = "Hazard Ground"; baseDisk.transform.SetParent(go.transform, false); baseDisk.transform.localScale = new Vector3(8f, .22f, 8f); baseDisk.GetComponent<Renderer>().sharedMaterial = mat; Object.DestroyImmediate(baseDisk.GetComponent<Collider>());
                if (i % 3 == 0) CreateHazardParticles(go.transform, new Color(.18f,.95f,.3f), 18, 1.3f, 1.2f);
                if (i % 3 == 1)
                {
                    CreateHazardParticles(go.transform, new Color(1f,.25f,.03f), 36, 2.2f, 2.5f);
                    for (int stone = 0; stone < 7; stone++) { float angle = stone * Mathf.PI * 2f / 7f; var rock = GameObject.CreatePrimitive(PrimitiveType.Cube); rock.name = "Fire Pit Stone"; rock.transform.SetParent(go.transform, false); rock.transform.localPosition = new Vector3(Mathf.Cos(angle) * 3.2f, .25f, Mathf.Sin(angle) * 3.2f); rock.transform.localScale = new Vector3(.8f,.45f,.8f); rock.GetComponent<Renderer>().sharedMaterial = MaterialAsset("Charcoal", new Color(.08f,.06f,.05f)); Object.DestroyImmediate(rock.GetComponent<Collider>()); }
                }
                if (i % 3 == 2)
                    for (int thorn = 0; thorn < 11; thorn++) { float angle = thorn * 2.4f; var spike = GameObject.CreatePrimitive(PrimitiveType.Cylinder); spike.name = "Bramble Thorn"; spike.transform.SetParent(go.transform, false); spike.transform.localPosition = new Vector3(Mathf.Cos(angle) * (1.3f + thorn % 3), .45f, Mathf.Sin(angle) * (1.3f + thorn % 3)); spike.transform.localScale = new Vector3(.16f,.85f,.16f); spike.transform.rotation = Quaternion.Euler(60f, thorn * 37f, 0f); spike.GetComponent<Renderer>().sharedMaterial = bramble; Object.DestroyImmediate(spike.GetComponent<Collider>()); }
                var hazard = go.AddComponent<HazardVolume>(); Set(hazard, "slow", i % 3 == 0); Set(hazard, "damagePerSecond", i % 3 == 1 ? 22f : 12f);
            }
        }
        private static void CreateHazardParticles(Transform parent, Color color, int count, float lifetime, float height)
        {
            var particles = new GameObject("Hazard VFX").AddComponent<ParticleSystem>(); particles.transform.SetParent(parent, false); var main = particles.main; main.startColor = color; main.startLifetime = lifetime; main.startSpeed = height; main.startSize = .25f; main.maxParticles = count; var emission = particles.emission; emission.rateOverTime = count / lifetime; var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 2.8f;
        }
        private static void CreateEnvironmentalDetail()
        {
            var tree = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NatureStarterKit/Models/Tree.prefab"); var rocks = new[] { "Assets/NatureStarterKit/Models/rock01.fbx", "Assets/NatureStarterKit/Models/rock02.fbx", "Assets/NatureStarterKit/Models/rock03.fbx" };
            Material bark = MaterialAsset("Tree Bark", new Color(.16f, .08f, .035f)); Material leaves = MaterialAsset("Tree Leaves", new Color(.075f, .22f, .09f));
            for (int i = 0; i < 45; i++) { Vector3 pos = OnTerrain(Random.Range(15f, 485f), Random.Range(15f, 485f)); if (tree != null) { var t = (GameObject)PrefabUtility.InstantiatePrefab(tree); t.transform.position = pos; t.transform.localScale = Vector3.one * Random.Range(1.1f, 2.1f); foreach (var renderer in t.GetComponentsInChildren<Renderer>()) renderer.sharedMaterials = new[] { bark, leaves }; } }
            for (int i = 0; i < 30; i++) { var rock = AssetDatabase.LoadAssetAtPath<GameObject>(rocks[i % rocks.Length]); if (rock != null) { var r = (GameObject)PrefabUtility.InstantiatePrefab(rock); r.transform.position = OnTerrain(Random.Range(10f,490f), Random.Range(10f,490f)); r.transform.localScale = Vector3.one * Random.Range(2f, 5f); } }
        }
        private static void ConfigureSpawns(EnemySpawnDirector director, GameObject melee, GameObject turret, GameObject charger)
        {
            Vector2[] authoredZones = { new(260f, 88f), new(125f, 135f), new(375f, 150f), new(90f, 300f), new(240f, 285f), new(410f, 315f), new(260f, 430f) };
            Transform[] zones = new Transform[authoredZones.Length]; for (int i = 0; i < zones.Length; i++) { var z = new GameObject("Enemy Zone " + (i + 1)); z.transform.position = OnTerrain(authoredZones[i].x, authoredZones[i].y); zones[i] = z.transform; }
            Set(director, "meleePrefab", melee); Set(director, "turretPrefab", turret); Set(director, "chargerPrefab", charger); Set(director, "projectilePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/CursedBolt.prefab")); Set(director, "zones", zones); Set(director, "totalEnemies", 21);
        }
        private static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; go.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            if (Object.FindAnyObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            return canvas;
        }
        private static GameObject CreatePanel(Transform parent, string name, Color color) { var panel = new GameObject(name, typeof(RectTransform), typeof(Image)); panel.transform.SetParent(parent, false); var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(.5f,.5f); rect.anchorMax = new Vector2(.5f,.5f); rect.sizeDelta = new Vector2(720f,480f); panel.GetComponent<Image>().color = color; return panel; }
        private static Text AddText(Transform parent, string name, string value, int size, Vector2 position) { var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false); var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(650f, 70f); rect.anchoredPosition = position; var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = value; text.fontSize = size; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; return text; }
        private static void AddButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action) { var go = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false); var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(250f,58f); rect.anchoredPosition = position; go.GetComponent<Image>().color = new Color(.38f,.14f,.55f); var text = AddText(go.transform, "Text", label, 25, Vector2.zero); text.rectTransform.sizeDelta = rect.sizeDelta; UnityEventTools.AddPersistentListener(go.GetComponent<Button>().onClick, action); }
        private static void CreateHud(Transform canvas, Health playerHealth, GameFlowManager game, AudioManager audio, out Text objective, out GameObject gameOver, out GameObject victory)
        {
            var healthBack = new GameObject("Health Back", typeof(RectTransform), typeof(Image)); healthBack.transform.SetParent(canvas, false); var rect = healthBack.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(.5f,1f); rect.anchorMax = new Vector2(.5f,1f); rect.sizeDelta = new Vector2(420f,32f); rect.anchoredPosition = new Vector2(0f,-36f); healthBack.GetComponent<Image>().color = Color.black;
            var fillGo = new GameObject("Health Fill", typeof(RectTransform), typeof(Image), typeof(HealthBarUI)); fillGo.transform.SetParent(healthBack.transform, false); var fillRect = fillGo.GetComponent<RectTransform>(); fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one; fillRect.offsetMin = new Vector2(3f,3f); fillRect.offsetMax = new Vector2(-3f,-3f); var fill = fillGo.GetComponent<Image>(); fill.color = new Color(.15f,.85f,.35f); fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; var ui = fillGo.GetComponent<HealthBarUI>(); ui.Configure(playerHealth, fill);
            objective = AddText(canvas, "Objective", "Cursed charms: 0 / 3", 21, new Vector2(0f, -72f)); objective.rectTransform.anchorMin = new Vector2(.5f,1f); objective.rectTransform.anchorMax = new Vector2(.5f,1f);
            gameOver = CreatePanel(canvas, "Game Over", new Color(.18f,.02f,.04f,.95f)); AddText(gameOver.transform, "Title", "GAME OVER", 56, new Vector2(0f,90f)); AddButton(gameOver.transform, "Restart", new Vector2(0f,0f), game.Restart); AddButton(gameOver.transform, "Main Menu", new Vector2(0f,-75f), game.MainMenu); gameOver.SetActive(false);
            victory = CreatePanel(canvas, "Victory", new Color(.12f,.2f,.06f,.95f)); AddText(victory.transform, "Title", "THE WILDS ARE CLEANSED", 43, new Vector2(0f,90f)); AddButton(victory.transform, "Restart", new Vector2(0f,0f), game.Restart); AddButton(victory.transform, "Main Menu", new Vector2(0f,-75f), game.MainMenu); victory.SetActive(false);
        }
        private static void CreatePauseMenu(Transform canvas, GameFlowManager game, AudioManager audio)
        {
            var pause = new GameObject("Pause Menu").AddComponent<PauseMenu>(); var panel = CreatePanel(canvas, "Paused", new Color(.025f,.04f,.09f,.94f)); AddText(panel.transform, "Title", "PAUSED", 54, new Vector2(0f,125f)); AddText(panel.transform, "Hint", "Press Esc to resume", 18, new Vector2(0f,78f)); AddButton(panel.transform, "Resume", new Vector2(0f,15f), pause.Resume); AddButton(panel.transform, "Restart", new Vector2(0f,-55f), pause.Restart); AddButton(panel.transform, "Main Menu", new Vector2(0f,-125f), pause.MainMenu); CreateVolumeControl(panel.transform, audio); Set(pause, "panel", panel); panel.SetActive(false);
        }
        private static void CreateVolumeControl(Transform canvas, AudioManager audio)
        {
            var label = AddText(canvas, "Volume Label", "VOLUME", 14, new Vector2(0f, -190f)); label.rectTransform.anchorMin = new Vector2(.5f, .5f); label.rectTransform.anchorMax = new Vector2(.5f, .5f); label.rectTransform.sizeDelta = new Vector2(180f, 26f);
            var sliderObject = new GameObject("Volume Slider", typeof(RectTransform), typeof(Slider)); sliderObject.transform.SetParent(canvas, false); var rect = sliderObject.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(.5f,.5f); rect.anchorMax = new Vector2(.5f,.5f); rect.sizeDelta = new Vector2(180f,20f); rect.anchoredPosition = new Vector2(0f,-215f);
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image)); background.transform.SetParent(sliderObject.transform, false); background.GetComponent<RectTransform>().anchorMin = Vector2.zero; background.GetComponent<RectTransform>().anchorMax = Vector2.one; background.GetComponent<Image>().color = Color.black;
            var fillArea = new GameObject("Fill Area", typeof(RectTransform)); fillArea.transform.SetParent(sliderObject.transform, false); var fillAreaRect = fillArea.GetComponent<RectTransform>(); fillAreaRect.anchorMin = Vector2.zero; fillAreaRect.anchorMax = Vector2.one; fillAreaRect.offsetMin = new Vector2(5f, 4f); fillAreaRect.offsetMax = new Vector2(-5f, -4f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(fillArea.transform, false); fill.GetComponent<RectTransform>().anchorMin = Vector2.zero; fill.GetComponent<RectTransform>().anchorMax = Vector2.one; fill.GetComponent<Image>().color = new Color(.35f,.75f,1f);
            var slider = sliderObject.GetComponent<Slider>(); slider.fillRect = fill.GetComponent<RectTransform>(); slider.value = SaveSystem.Instance == null ? 1f : SaveSystem.Instance.MasterVolume; slider.onValueChanged.AddListener(audio.SetMasterVolume);
        }
        private static GameObject SavePrefab(GameObject source, string name)
        {
            string path = Root + "/Prefabs/" + name + ".prefab";
            // Recreate generated prefabs from scratch so an old missing MonoBehaviour cannot survive an overwrite.
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, path); Object.DestroyImmediate(source); return prefab;
        }
        private static void SaveScene(string name) { EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Root + "/Scenes/" + name + ".unity"); }
        private static void SetBuildScenes() => EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Root + "/Scenes/MainMenu.unity", true), new EditorBuildSettingsScene(Root + "/Scenes/CursedWilds.unity", true) };
        private static void RemoveMissingScripts(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) RemoveMissingScripts(root);
        }
        private static void RemoveMissingScripts(GameObject gameObject)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            foreach (Transform child in gameObject.transform) RemoveMissingScripts(child.gameObject);
        }
        private static Vector3 OnTerrain(float x, float z, float extraHeight = 1f)
        {
            Terrain terrain = Terrain.activeTerrain;
            return new Vector3(x, terrain == null ? extraHeight : terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y + extraHeight, z);
        }
        private static void Set(Object target, string property, object value) { var so = new SerializedObject(target); var p = so.FindProperty(property); if (p == null) throw new System.ArgumentException(property); if (value is Object obj) p.objectReferenceValue = obj; else if (value is float f) p.floatValue = f; else if (value is int i) p.intValue = i; else if (value is bool b) p.boolValue = b; else if (value is CollectibleKind kind) p.enumValueIndex = (int)kind; else if (value is Transform[] transforms) p.arraySize = transforms.Length; if (value is Transform[] array) for (int i = 0; i < array.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = array[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
