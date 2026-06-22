using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace CursedWilds
{
    [RequireComponent(typeof(Health))]
    public abstract partial class EnemyController : MonoBehaviour
    {
        [SerializeField] protected float detectionRadius = 65f;
        [SerializeField] protected float attackRadius = 2f;
        [SerializeField] protected float attackDamage = 12f;
        [SerializeField] protected float attackCooldown = 1.25f;
        [SerializeField] protected Transform healthBar;
        protected Transform player;
        protected NavMeshAgent agent;
        protected Health health;
        protected Animator animator;
        protected float nextAttack;
        // Terrain slopes must not make nearby, open-ground enemies permanently inert.
        protected bool PlayerVisible => player != null && Vector3.Distance(transform.position, player.position) <= detectionRadius;

        protected virtual void Awake()
        {
            health = GetComponent<Health>(); agent = GetComponent<NavMeshAgent>(); animator = GetComponentInChildren<Animator>(); player = GameObject.FindGameObjectWithTag("Player")?.transform;
            health.Died += OnDied;
        }
        protected virtual void Update()
        {
            if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (healthBar != null && Camera.main != null) healthBar.forward = Camera.main.transform.forward;
            if (animator != null) animator.SetFloat("Speed", agent != null && agent.enabled ? agent.velocity.magnitude : 0f);
        }
        protected void DamagePlayer() { if (player == null || Time.time < nextAttack) return; nextAttack = Time.time + attackCooldown; if (animator != null) animator.SetTrigger("Attack"); player.GetComponent<PlayerStatus>()?.ReceiveDamage(attackDamage, gameObject); AudioManager.PlaySfx(.2f, 180f); }
        private void OnDied(Health _, GameObject __) { if (agent != null && agent.enabled) agent.isStopped = true; if (animator != null) animator.SetBool("Dead", true); VfxFactory.SpawnEnemyDeath(transform.position + Vector3.up); AudioManager.PlaySfx(.45f, 90f); Destroy(gameObject, 1.4f); }
    }

    public sealed partial class MeleeEnemy : EnemyController
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

    public sealed partial class TurretEnemy : EnemyController
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

    public sealed partial class ChargerEnemy : EnemyController
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

    public sealed partial class EnemyProjectile : MonoBehaviour
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

    public sealed partial class WorldHealthBar : MonoBehaviour
    {
        [SerializeField] private Transform fill;
        [SerializeField] private Health target;
        public void Configure(Health health, Transform fillTransform) { target = health; fill = fillTransform; }
        private void Awake() { if (target == null) target = GetComponentInParent<Health>(); if (target != null) target.Changed += Changed; }
        private void OnDestroy() { if (target != null) target.Changed -= Changed; }
        private void Changed(float current, float max) { if (fill != null) fill.localScale = new Vector3(Mathf.Clamp01(current / max), 1f, 1f); }
        private void LateUpdate() { if (Camera.main != null) transform.forward = Camera.main.transform.forward; }
    }

    public sealed partial class EnemySpawnDirector : MonoBehaviour
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
            Health health = enemy.GetComponent<Health>(); if (health != null) health.Configure(type == 0 ? 70f : type == 1 ? 85f : 110f);
            if (type == 1) enemy.GetComponent<TurretEnemy>()?.Configure(projectilePrefab);
        }
    }
}
