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
            if (animator != null && agent != null && agent.enabled) animator.SetFloat("Speed", agent.velocity.magnitude);
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
        [Header("Flying Charger")]
        [SerializeField] private float hoverHeight = 6f;
        [SerializeField] private float dipSpeed = 22f;
        [SerializeField] private float riseSpeed = 8f;
        [SerializeField] private float attackDamageDip = 18f;
        [SerializeField] private float windup = 1f;
        [SerializeField] private float recovery = 1.5f;
        [SerializeField] private float circleSpeed = 1.5f;
        [SerializeField] private float circleRadius = 8f;

        private enum State { Circling, Dipping, Rising }
        private State state = State.Circling;
        private float stateTimer;
        private float circleAngle;
        private Vector3 hoverTarget;
        private float baseY;

        protected override void Awake()
        {
            base.Awake();
            if (agent != null) agent.enabled = false;
        }

        protected override void Update()
        {
            base.Update();
            if (health.IsDead || player == null) return;

            // Maintain altitude always
            float groundY = GetGroundHeight();
            baseY = groundY + hoverHeight;

            switch (state)
            {
                case State.Circling:
                    UpdateCircling();
                    break;
                case State.Dipping:
                    UpdateDipping();
                    break;
                case State.Rising:
                    UpdateRising();
                    break;
            }

            // Always face the player horizontally
            if (player != null)
            {
                Vector3 flat = player.position - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > .01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 6f * Time.deltaTime);
            }
        }

        private void UpdateCircling()
        {
            if (!PlayerVisible) return;

            circleAngle += circleSpeed * Time.deltaTime;
            Vector3 offset = new Vector3(Mathf.Cos(circleAngle) * circleRadius, 0f, Mathf.Sin(circleAngle) * circleRadius);
            Vector3 targetPos = player.position + offset;
            targetPos.y = baseY;

            transform.position = Vector3.Lerp(transform.position, targetPos, 4f * Time.deltaTime);

            // Decide to dip
            float dist = Vector3.Distance(transform.position, player.position);
            if (dist < detectionRadius * 0.7f && Time.time > stateTimer)
            {
                state = State.Dipping;
                stateTimer = Time.time + windup;
            }
        }

        private void UpdateDipping()
        {
            // Hover down toward player
            Vector3 dipTarget = player.position + Vector3.up * 1.2f;
            transform.position = Vector3.MoveTowards(transform.position, dipTarget, dipSpeed * Time.deltaTime);

            float dist = Vector3.Distance(transform.position, player.position);

            // Hit the player when close enough
            if (dist < attackRadius)
            {
                if (Time.time >= stateTimer)
                {
                    player.GetComponent<PlayerStatus>()?.ReceiveDamage(attackDamageDip, gameObject);
                    nextAttack = Time.time + attackCooldown;
                    if (animator != null) animator.SetTrigger("Attack");
                    AudioManager.PlaySfx(.2f, 180f);
                    VfxFactory.Spawn(transform.position, Color.red, 15);
                }
                state = State.Rising;
                stateTimer = Time.time + recovery;
                return;
            }

            // Timeout — rise back up
            if (Time.time > stateTimer + 0.5f)
            {
                state = State.Rising;
                stateTimer = Time.time + recovery;
            }
        }

        private void UpdateRising()
        {
            // Fly back up to hover height
            Vector3 riseTarget = transform.position;
            riseTarget.y = baseY;
            transform.position = Vector3.MoveTowards(transform.position, riseTarget, riseSpeed * Time.deltaTime);

            if (transform.position.y >= baseY - 0.5f || Time.time > stateTimer)
            {
                state = State.Circling;
                stateTimer = Time.time + recovery * 0.5f;
            }
        }

        private float GetGroundHeight()
        {
            if (Physics.Raycast(transform.position + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, LayerMask.GetMask("Default")))
                return hit.point.y;
            return 0f;
        }
    }

    public sealed partial class EnemyProjectile : MonoBehaviour
    {
        private Vector3 direction;
        private float speed = 18f;
        private float damage = 14f;
        private float maxLifetime = 4f;
        private float spawnTime;
        private Transform player;
        private const float HitRadius = 1.1f;

        public void Launch(Transform playerTarget)
        {
            direction = ((playerTarget.position + Vector3.up) - transform.position).normalized;
            transform.forward = direction;
            player = playerTarget;
            spawnTime = Time.time;
            Destroy(gameObject, maxLifetime);
        }
        private void Update()
        {
            transform.position += direction * speed * Time.deltaTime;

            // Distance-based hit detection — works reliably against CharacterController.
            if (player != null && Vector3.Distance(transform.position, player.position + Vector3.up) < HitRadius)
            {
                PlayerStatus status = player.GetComponentInParent<PlayerStatus>();
                if (status != null) status.ReceiveDamage(damage, gameObject);
                VfxFactory.Spawn(transform.position, Color.red, 15);
                Destroy(gameObject);
            }
        }
    }

    public sealed partial class WorldHealthBar : MonoBehaviour
    {
        [SerializeField] private Transform fill;
        [SerializeField] private Health target;
        private float maxWidth = 1f;

        public void Configure(Health health, Transform fillTransform) { target = health; fill = fillTransform; }
        private void Awake()
        {
            if (fill != null) maxWidth = fill.localScale.x;
            if (target == null) target = GetComponentInParent<Health>();
            if (target != null)
            {
                target.Changed += Changed;
                // Force initial update
                Changed(target.CurrentHealth, target.MaximumHealth);
            }
        }
        private void OnDestroy() { if (target != null) target.Changed -= Changed; }
        private void Changed(float current, float max)
        {
            if (fill != null)
            {
                float ratio = Mathf.Clamp01(current / max);
                fill.localScale = new Vector3(maxWidth * ratio, fill.localScale.y, fill.localScale.z);
                fill.localPosition = new Vector3(-(maxWidth * (1f - ratio)) * 0.5f, fill.localPosition.y, fill.localPosition.z);
            }
        }
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
