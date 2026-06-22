using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace CursedWilds
{
    [RequireComponent(typeof(Health))]
    public abstract class EnemyController : MonoBehaviour
    {
        [SerializeField] protected float detectionRadius = 65f;
        [SerializeField] protected float attackRadius = 2f;
        [SerializeField] protected float attackDamage = 12f;
        [SerializeField] protected float attackCooldown = 1.25f;
        [SerializeField] protected Transform healthBar;
        protected Transform player;
        protected NavMeshAgent agent;
        protected Health health;
        protected VisualAnimation visualAnimation;
        protected float nextAttack;
        // Terrain slopes must not make nearby, open-ground enemies permanently inert.
        protected bool PlayerVisible => player != null && Vector3.Distance(transform.position, player.position) <= detectionRadius;

        protected virtual void Awake()
        {
            health = GetComponent<Health>(); agent = GetComponent<NavMeshAgent>(); visualAnimation = GetComponent<VisualAnimation>(); player = GameObject.FindGameObjectWithTag("Player")?.transform;
            health.Died += OnDied;
        }
        protected virtual void Update() { if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform; if (healthBar != null && Camera.main != null) healthBar.forward = Camera.main.transform.forward; visualAnimation?.SetMoving(agent != null && agent.enabled && agent.velocity.sqrMagnitude > .05f); }
        protected void DamagePlayer() { if (player == null || Time.time < nextAttack) return; nextAttack = Time.time + attackCooldown; visualAnimation?.PlayAttack(); player.GetComponent<PlayerStatus>()?.ReceiveDamage(attackDamage, gameObject); AudioManager.PlaySfx(.2f, 180f); }
        private void OnDied(Health _, GameObject __) { if (agent != null) agent.isStopped = true; VfxFactory.Spawn(transform.position + Vector3.up, Color.magenta, 38); AudioManager.PlaySfx(.45f, 90f); Destroy(gameObject, 1.2f); }
    }

    public sealed class MeleeEnemy : EnemyController
    {
        protected override void Update()
        {
            base.Update(); if (health.IsDead || player == null || agent == null || !agent.isOnNavMesh) return;
            if (!PlayerVisible) { agent.isStopped = true; return; }
            float distance = Vector3.Distance(transform.position, player.position);
            if (distance > attackRadius) { agent.isStopped = false; agent.SetDestination(player.position); }
            else { agent.isStopped = true; transform.LookAt(new Vector3(player.position.x, transform.position.y, player.position.z)); DamagePlayer(); }
        }
    }

    public sealed class TurretEnemy : EnemyController
    {
        [SerializeField] private GameObject projectilePrefab;
        public void Configure(GameObject projectile) => projectilePrefab = projectile;
        protected override void Update()
        {
            base.Update(); if (health.IsDead || !PlayerVisible || player == null) return;
            Vector3 flat = player.position - transform.position; flat.y = 0f; if (flat.sqrMagnitude > .01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 5f * Time.deltaTime);
            if (Time.time < nextAttack) return; nextAttack = Time.time + attackCooldown;
            if (projectilePrefab != null) { var shot = Instantiate(projectilePrefab, transform.position + Vector3.up * 1.4f + transform.forward, Quaternion.LookRotation((player.position + Vector3.up - transform.position).normalized)); shot.AddComponent<EnemyProjectile>().Launch(player); }
            AudioManager.PlaySfx(.25f, 300f);
        }
    }

    public sealed class ChargerEnemy : EnemyController
    {
        [SerializeField] private float chargeSpeed = 15f;
        [SerializeField] private float windup = .65f;
        [SerializeField] private float recovery = 1.2f;
        private float chargeAt = -1f, recoveryUntil;
        protected override void Awake()
        {
            base.Awake();
            if (agent != null) agent.enabled = false;
        }
        protected override void Update()
        {
            base.Update(); if (health.IsDead || player == null || Time.time < recoveryUntil) return;
            float distance = Vector3.Distance(transform.position, player.position); if (!PlayerVisible) return;
            if (chargeAt < 0f && distance > attackRadius && distance < detectionRadius) { chargeAt = Time.time + windup; return; }
            if (chargeAt > 0f && Time.time >= chargeAt)
            {
                Vector3 direction = (player.position - transform.position); direction.y = 0f; transform.position += direction.normalized * chargeSpeed * Time.deltaTime; transform.forward = direction.normalized;
                if (Vector3.Distance(transform.position, player.position) <= attackRadius) { DamagePlayer(); chargeAt = -1f; recoveryUntil = Time.time + recovery; }
                else if (Time.time > chargeAt + 1.2f) { chargeAt = -1f; recoveryUntil = Time.time + recovery; }
            }
        }
    }

    public sealed class EnemyProjectile : MonoBehaviour
    {
        private Transform target; private float speed = 18f; private float damage = 14f;
        public void Launch(Transform player) { target = player; Destroy(gameObject, 4f); }
        private void Update()
        {
            if (target == null) { Destroy(gameObject); return; }
            Vector3 direction = (target.position + Vector3.up - transform.position).normalized; transform.position += direction * speed * Time.deltaTime; transform.forward = direction;
            if (Vector3.Distance(transform.position, target.position + Vector3.up) < .8f) { target.GetComponent<PlayerStatus>()?.ReceiveDamage(damage, gameObject); VfxFactory.Spawn(transform.position, Color.red, 15); Destroy(gameObject); }
        }
    }

    public sealed class WorldHealthBar : MonoBehaviour
    {
        [SerializeField] private Transform fill;
        [SerializeField] private Health target;
        public void Configure(Health health, Transform fillTransform) { target = health; fill = fillTransform; }
        private void Awake() { if (target == null) target = GetComponentInParent<Health>(); if (target != null) target.Changed += Changed; }
        private void OnDestroy() { if (target != null) target.Changed -= Changed; }
        private void Changed(float current, float max) { if (fill != null) fill.localScale = new Vector3(Mathf.Clamp01(current / max), 1f, 1f); }
        private void LateUpdate() { if (Camera.main != null) transform.forward = Camera.main.transform.forward; }
    }

    public sealed class EnemySpawnDirector : MonoBehaviour
    {
        [SerializeField] private GameObject meleePrefab;
        [SerializeField] private GameObject turretPrefab;
        [SerializeField] private GameObject chargerPrefab;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform[] zones;
        [SerializeField] private int totalEnemies = 21;
        private void Start()
        {
            if (zones == null || zones.Length == 0) { Debug.LogError("Enemy Spawn Director has no spawn zones."); return; }
            if (!NavMesh.SamplePosition(zones[0].position, out _, 80f, NavMesh.AllAreas))
            {
                NavMeshSurface surface = Object.FindAnyObjectByType<NavMeshSurface>();
                if (surface != null) surface.BuildNavMesh();
            }
            GameObject[] types = { meleePrefab, turretPrefab, chargerPrefab };
            for (int i = 0; i < totalEnemies; i++)
            {
                Transform zone = zones[i % zones.Length]; Vector3 point = zone.position + new Vector3(Random.Range(-18f, 18f), 0f, Random.Range(-18f, 18f));
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, 40f, NavMesh.AllAreas)) { Debug.LogWarning("Skipped enemy spawn: no navigable ground near " + zone.name); continue; }
                Spawn(types[i % types.Length], i % types.Length, hit.position);
            }
        }
        private void Spawn(GameObject template, int type, Vector3 position)
        {
            if (template == null) return;
            GameObject enemy = Instantiate(template, position, Quaternion.identity);
            Health health = enemy.AddComponent<Health>(); health.Configure(type == 0 ? 70f : type == 1 ? 85f : 110f);
            NavMeshAgent agent = enemy.AddComponent<NavMeshAgent>(); agent.radius = .45f; agent.height = 2f; agent.speed = type == 2 ? 4f : 3.5f; agent.stoppingDistance = 1.7f;
            if (type == 0) enemy.AddComponent<MeleeEnemy>();
            else if (type == 1) { agent.enabled = false; enemy.AddComponent<TurretEnemy>().Configure(projectilePrefab); }
            else enemy.AddComponent<ChargerEnemy>();
            VisualAnimation animation = enemy.AddComponent<VisualAnimation>(); animation.Configure(FindChild(enemy.transform, "Body"), FindChild(enemy.transform, "Head"), FindChild(enemy.transform, "Left Arm"), FindChild(enemy.transform, "Right Arm"), FindChild(enemy.transform, "Left Leg"), FindChild(enemy.transform, "Right Leg"));
            CreateHealthBar(enemy.transform, health);
        }
        private static Transform FindChild(Transform root, string childName)
        {
            Transform found = root.Find(childName);
            return found != null ? found : root;
        }
        private static void CreateHealthBar(Transform parent, Health health) // Just make the thing instead of having it in world
        {
            GameObject bar = new GameObject("HealthBar"); bar.transform.SetParent(parent); bar.transform.localPosition = new Vector3(0f, 2.75f, 0f); bar.transform.localScale = new Vector3(1.4f, .16f, .1f);
            GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube); back.transform.SetParent(bar.transform); back.transform.localScale = Vector3.one; Object.Destroy(back.GetComponent<Collider>());
            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube); fill.name = "Fill"; fill.transform.SetParent(bar.transform); fill.transform.localPosition = new Vector3(-.5f, 0f, -.06f); fill.transform.localScale = new Vector3(1f, .65f, .3f); Object.Destroy(fill.GetComponent<Collider>());
            bar.AddComponent<WorldHealthBar>().Configure(health, fill.transform);
        }
    }
}
