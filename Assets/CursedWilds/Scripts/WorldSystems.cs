using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CursedWilds
{
    public enum CollectibleKind { Heal, Speed, Shield, HeartwoodRelic }

    [Serializable]
    internal sealed class CursedWildsSaveData
    {
        public int collectedMask;
        public float masterVolume = 1f;
    }

    /// <summary>Small local encrypted JSON save for player collectibles and audio settings.</summary>
    public sealed partial class SaveSystem : MonoBehaviour
    {
        private const byte Key = 0x5D;
        private static string SavePath => Path.Combine(Application.persistentDataPath, "cursed-wilds.save");
        public static SaveSystem Instance { get; private set; }
        private CursedWildsSaveData data = new CursedWildsSaveData();
        public float MasterVolume => data.masterVolume;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; Load();
        }
        private void OnApplicationQuit() => Save();
        public bool WasCollected(CollectibleKind kind) => (data.collectedMask & (1 << (int)kind)) != 0;
        public void MarkCollected(CollectibleKind kind) { data.collectedMask |= 1 << (int)kind; Save(); }
        public void SetMasterVolume(float value) { data.masterVolume = Mathf.Clamp01(value); Save(); }

        private void Load()
        {
            if (!File.Exists(SavePath)) return;
            try
            {
                byte[] bytes = Convert.FromBase64String(File.ReadAllText(SavePath));
                for (int i = 0; i < bytes.Length; i++) bytes[i] ^= Key;
                data = JsonUtility.FromJson<CursedWildsSaveData>(Encoding.UTF8.GetString(bytes)) ?? new CursedWildsSaveData();
                data.masterVolume = Mathf.Clamp01(data.masterVolume);
            }
            catch (Exception exception) { Debug.LogWarning("Cursed Wilds save could not be loaded: " + exception.Message); data = new CursedWildsSaveData(); }
        }
        private void Save()
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
                for (int i = 0; i < bytes.Length; i++) bytes[i] ^= Key;
                File.WriteAllText(SavePath, Convert.ToBase64String(bytes));
            }
            catch (Exception exception) { Debug.LogWarning("Cursed Wilds save could not be written: " + exception.Message); }
        }
    }

    public sealed partial class ObjectiveTracker : MonoBehaviour
    {
        [SerializeField] private Text label;
        private readonly HashSet<CollectibleKind> required = new HashSet<CollectibleKind>();
        public bool HasRequirements => required.Contains(CollectibleKind.Heal) && required.Contains(CollectibleKind.Speed) && required.Contains(CollectibleKind.Shield);
        private void Start()
        {
            foreach (CollectibleKind kind in new[] { CollectibleKind.Heal, CollectibleKind.Speed, CollectibleKind.Shield })
                if (SaveSystem.Instance != null && SaveSystem.Instance.WasCollected(kind)) required.Add(kind);
            Refresh();
        }
        public void Collected(CollectibleKind kind)
        {
            if (kind != CollectibleKind.HeartwoodRelic) required.Add(kind);
            Refresh();
        }
        private void Refresh() { if (label != null) label.text = HasRequirements ? "Heartwood Relic unlocked — find it!" : "Cursed charms: " + required.Count + " / 3"; }
    }

    [RequireComponent(typeof(Collider))]
    public sealed partial class CollectibleController : MonoBehaviour
    {
        [SerializeField] private CollectibleKind kind;
        [SerializeField] private float bobHeight = .35f;
        [SerializeField] private float bobSpeed = 2f;
        [SerializeField] private float pickupRadius = 1.7f;
        [SerializeField] private ObjectiveTracker objectives;
        private Vector3 origin;
        private bool collected;
        private void Awake() { origin = transform.position; GetComponent<Collider>().isTrigger = true; }
        private void Update()
        {
            transform.position = origin + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight); transform.Rotate(0f, 55f * Time.deltaTime, 0f);
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && (player.transform.position - transform.position).sqrMagnitude <= pickupRadius * pickupRadius) TryCollect(player);
        }
        private void OnTriggerEnter(Collider other)
        {
            TryCollect(other.gameObject);
        }
        private void TryCollect(GameObject candidate)
        {
            if (collected) return;
            PlayerStatus status = candidate.GetComponentInParent<PlayerStatus>(); if (status == null) return;
            if (kind == CollectibleKind.HeartwoodRelic && (objectives == null || !objectives.HasRequirements)) return;
            collected = true;
            switch (kind) { case CollectibleKind.Heal: status.Heal(45f); break; case CollectibleKind.Speed: status.Speed(1.45f, 12f); break; case CollectibleKind.Shield: status.Shield(12f); break; }
            objectives?.Collected(kind); VfxFactory.SpawnPickup(transform.position, kind == CollectibleKind.HeartwoodRelic);
            AudioManager.PlaySfx(kind == CollectibleKind.HeartwoodRelic ? .8f : .45f, kind == CollectibleKind.HeartwoodRelic ? 820f : 520f);
            SaveSystem.Instance?.MarkCollected(kind);
            if (kind == CollectibleKind.HeartwoodRelic) GameFlowManager.Instance?.Victory();
            Destroy(gameObject);
        }
    }

    [RequireComponent(typeof(Collider))]
    public sealed partial class HazardVolume : MonoBehaviour
    {
        [SerializeField] private float damagePerSecond = 12f;
        [SerializeField] private bool slow;
        private void Awake() => GetComponent<Collider>().isTrigger = true;
        private void Update()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player"); if (player == null) return;
            Vector3 delta = player.transform.position - transform.position; delta.y = 0f;
            float radius = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * .5f;
            if (delta.sqrMagnitude <= radius * radius) ApplyTo(player);
        }
        private void ApplyTo(GameObject candidate)
        {
            PlayerStatus status = candidate.GetComponentInParent<PlayerStatus>(); if (status == null) return;
            status.ReceiveDamage(damagePerSecond * Time.deltaTime, gameObject); if (slow) status.Slow(.25f);
        }
    }

    public sealed partial class GameFlowManager : MonoBehaviour
    {
        public static GameFlowManager Instance { get; private set; }
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private GameObject victoryPanel;
        [SerializeField] private Health playerHealth;
        private void Awake() => Instance = this;
        private void Start()
        {
            if (playerHealth == null) playerHealth = GameObject.FindGameObjectWithTag("Player")?.GetComponent<Health>();
            WatchPlayer(playerHealth);
        }
        private void OnDestroy()
        {
            if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
            if (Instance == this) Instance = null;
        }
        public void WatchPlayer(Health health)
        {
            if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
            playerHealth = health;
            if (playerHealth != null) playerHealth.Died += OnPlayerDied;
        }
        private void OnPlayerDied(Health _, GameObject __) => GameOver();
        public void GameOver() { if (gameOverPanel != null) gameOverPanel.SetActive(true); AudioManager.PlaySfx(1f, 100f); }
        public void Victory() { if (victoryPanel != null) victoryPanel.SetActive(true); AudioManager.PlaySfx(1f, 880f); }
        public void Restart() { Time.timeScale = 1f; SceneManager.LoadScene(SceneManager.GetActiveScene().name); }
        public void MainMenu() { Time.timeScale = 1f; SceneManager.LoadScene("MainMenu"); }
    }

    public sealed partial class PauseMenu : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        private bool paused;
        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (paused) Resume(); else Pause();
            }
        }
        public void Pause()
        {
            paused = true; Time.timeScale = 0f; if (panel != null) panel.SetActive(true); Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        }
        public void Resume()
        {
            paused = false; Time.timeScale = 1f; if (panel != null) panel.SetActive(false); ThirdPersonCamera.CaptureCursor();
        }
        public void Restart() { Time.timeScale = 1f; GameFlowManager.Instance?.Restart(); }
        public void MainMenu() { Time.timeScale = 1f; GameFlowManager.Instance?.MainMenu(); }
    }

    public sealed partial class MenuController : MonoBehaviour
    {
        public void Play() => SceneManager.LoadScene("CursedWilds");
        public void Quit() { Application.Quit(); }
    }

    public sealed partial class AudioManager : MonoBehaviour
    {
        [SerializeField] private AudioMixer mixer;
        [SerializeField] private AudioSource music;
        public static AudioManager Instance { get; private set; }
        private void Awake()
        {
            Instance = this;
            if (music == null) music = GetComponent<AudioSource>();
            if (music != null && music.clip == null) { music.clip = CreateScore(SceneManager.GetActiveScene().name == "MainMenu" ? 92f : 72f); music.loop = true; music.volume = .16f; music.Play(); }
            SetMasterVolume(SaveSystem.Instance == null ? 1f : SaveSystem.Instance.MasterVolume);
        }
        public void SetMasterVolume(float slider) { slider = Mathf.Clamp01(slider); AudioListener.volume = slider; if (mixer != null) mixer.SetFloat("MasterVolume", Mathf.Lerp(-80f, 0f, slider)); SaveSystem.Instance?.SetMasterVolume(slider); }
        public static void PlaySfx(float duration, float frequency)
        {
            var source = new GameObject("Sfx").AddComponent<AudioSource>(); source.clip = Tone(frequency, duration); source.spatialBlend = 0f; source.volume = .22f; source.Play(); Destroy(source.gameObject, duration + .1f);
        }
        private static AudioClip Tone(float frequency, float duration)
        {
            int rate = 22050, samples = Mathf.CeilToInt(rate * duration); var clip = AudioClip.Create("CursedWildsTone", samples, 1, rate, false); var data = new float[samples];
            for (int i = 0; i < samples; i++) { float envelope = Mathf.Clamp01(1f - (float)i / samples); data[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / rate) * envelope * .22f; }
            clip.SetData(data, 0); return clip;
        }
        private static AudioClip CreateScore(float root)
        {
            const int rate = 22050; const float duration = 12f; int samples = Mathf.CeilToInt(rate * duration); var clip = AudioClip.Create("CursedWildsScore", samples, 1, rate, false); var data = new float[samples];
            float[] chord = { 1f, 1.1892f, 1.4983f, 1.7818f };
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate; float bar = Mathf.Floor(t / 3f); float low = root * chord[(int)bar % chord.Length]; float drone = Mathf.Sin(2f * Mathf.PI * low * t) * .11f + Mathf.Sin(2f * Mathf.PI * low * .5f * t) * .08f;
                float pulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * (t % .75f) / .75f)), 8f) * Mathf.Sin(2f * Mathf.PI * low * 2f * t) * .045f;
                data[i] = (drone + pulse) * (.72f + Mathf.Sin(t * .14f) * .08f);
            }
            clip.SetData(data, 0); return clip;
        }
    }

    public static class VfxFactory
    {
        public static void SpawnPickup(Vector3 position, bool relic)
        {
            var go = new GameObject(relic ? "RelicVictoryParticles" : "PickupParticles"); go.transform.position = position; var ps = go.AddComponent<ParticleSystem>(); var main = ps.main; main.startColor = relic ? new Color(1f,.78f,.08f) : Color.cyan; main.startLifetime = relic ? 1.8f : .8f; main.startSpeed = relic ? 5f : 2.5f; main.maxParticles = relic ? 90 : 35; var emission = ps.emission; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(relic ? 90 : 35)) }); var shape = ps.shape; shape.shapeType = relic ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Sphere; shape.radius = .25f; ps.Play(); UnityEngine.Object.Destroy(go, relic ? 2.4f : 1.4f);
        }
        public static void SpawnEnemyDeath(Vector3 position)
        {
            var go = new GameObject("EnemyDeathParticles"); go.transform.position = position; var ps = go.AddComponent<ParticleSystem>(); var main = ps.main; main.startColor = new Color(.8f,.08f,1f); main.startLifetime = 1.1f; main.startSpeed = 4.2f; main.maxParticles = 48; var emission = ps.emission; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 48) }); var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = .45f; ps.Play(); UnityEngine.Object.Destroy(go, 1.7f);
        }
        public static void Spawn(Vector3 position, Color color, int count) => SpawnEnemyDeath(position);
    }
}
