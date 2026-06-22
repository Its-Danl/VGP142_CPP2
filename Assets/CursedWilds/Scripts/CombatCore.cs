using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CursedWilds
{
    public interface IDamageable
    {
        void ApplyDamage(float amount, GameObject source = null);
    }

    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] private float maximumHealth = 100f;
        [SerializeField] private float currentHealth = 100f;
        public float MaximumHealth => maximumHealth;
        public float CurrentHealth => currentHealth;
        public bool IsDead { get; private set; }
        public event Action<float, float> Changed;
        public event Action<Health, GameObject> Died;

        private void Awake() => currentHealth = Mathf.Clamp(currentHealth, 0f, maximumHealth);
        public void Configure(float maximum) { maximumHealth = Mathf.Max(1f, maximum); currentHealth = maximumHealth; IsDead = false; Changed?.Invoke(currentHealth, maximumHealth); }
        public void ApplyDamage(float amount, GameObject source = null)
        {
            if (IsDead || amount <= 0f) return;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
            Changed?.Invoke(currentHealth, maximumHealth);
            if (currentHealth <= 0f) { IsDead = true; Died?.Invoke(this, source); }
        }
        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            currentHealth = Mathf.Min(maximumHealth, currentHealth + amount);
            Changed?.Invoke(currentHealth, maximumHealth);
        }
    }

    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public sealed class ThirdPersonPlayer : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float sprintSpeed = 8f;
        [SerializeField] private float jumpHeight = 1.4f;
        [SerializeField] private float gravity = -22f;
        [SerializeField] private float turnSmoothTime = .12f;
        private CharacterController controller;
        private Camera playerCamera;
        private VisualAnimation visualAnimation;
        private float verticalVelocity, turnVelocity;
        private float speedMultiplier = 1f, slowUntil;
        public Health Health { get; private set; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>(); Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
            visualAnimation = GetComponent<VisualAnimation>();
            playerCamera = Camera.main;
            Health.Died += (_, _) => enabled = false;
        }
        private void Update()
        {
            if (Health.IsDead) return;
            var keyboard = Keyboard.current;
            Vector2 move = keyboard == null ? Vector2.zero : new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0), (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            if (playerCamera == null) playerCamera = Camera.main;
            bool sprinting = keyboard != null && keyboard.leftShiftKey.isPressed;
            float modifier = Time.time < slowUntil ? .5f : speedMultiplier;
            float speed = (sprinting ? sprintSpeed : walkSpeed) * modifier;
            Vector3 input = new Vector3(move.x, 0f, move.y).normalized;
            visualAnimation?.SetMoving(input.sqrMagnitude > .01f);
            if (input.sqrMagnitude > .01f && playerCamera != null)
            {
                float targetAngle = Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg + playerCamera.transform.eulerAngles.y;
                float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnVelocity, turnSmoothTime);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
                Vector3 direction = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
                controller.Move(direction.normalized * speed * Time.deltaTime);
            }
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            if (controller.isGrounded && keyboard != null && keyboard.spaceKey.wasPressedThisFrame) verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            verticalVelocity += gravity * Time.deltaTime; controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
        }
        public void ApplySpeedBoost(float multiplier, float duration) { speedMultiplier = Mathf.Max(speedMultiplier, multiplier); Invoke(nameof(ClearSpeedBoost), duration); }
        public void ApplySlow(float duration) => slowUntil = Mathf.Max(slowUntil, Time.time + duration);
        private void ClearSpeedBoost() => speedMultiplier = 1f;
    }

    /// <summary>Applies Cursed Wilds power-up modifiers without competing with Starter Assets movement so that it can be affected by obst.</summary>
    [RequireComponent(typeof(StarterAssets.ThirdPersonController))]
    public sealed class PlayerMovementEffects : MonoBehaviour
    {
        private StarterAssets.ThirdPersonController controller;
        private float baseMoveSpeed;
        private float baseSprintSpeed;
        private float speedMultiplier = 1f;
        private float speedUntil;
        private float slowUntil;

        private void Awake()
        {
            controller = GetComponent<StarterAssets.ThirdPersonController>();
            if (controller == null) { enabled = false; return; }
            baseMoveSpeed = controller.MoveSpeed;
            baseSprintSpeed = controller.SprintSpeed;
        }

        private void Update()
        {
            if (controller == null) return;
            float boost = Time.time < speedUntil ? speedMultiplier : 1f;
            float slow = Time.time < slowUntil ? .5f : 1f;
            controller.MoveSpeed = baseMoveSpeed * boost * slow;
            controller.SprintSpeed = baseSprintSpeed * boost * slow;
        }

        public void ApplySpeedBoost(float multiplier, float duration)
        {
            speedMultiplier = Mathf.Max(speedMultiplier, multiplier);
            speedUntil = Mathf.Max(speedUntil, Time.time + duration);
        }

        public void ApplySlow(float duration) => slowUntil = Mathf.Max(slowUntil, Time.time + duration);
    }

    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 2.8f, -6.5f);
        private float yaw;
        private float pitch = 15f;
        public void Configure(Transform follow) => target = follow;
        private void Start()
        {
            if (target != null) yaw = target.eulerAngles.y;
            CaptureCursor();
        }
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && Time.timeScale > 0f) CaptureCursor();
        }
        public static void CaptureCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        private void LateUpdate()
        {
            if (target == null) return;
            if (Mouse.current != null) { yaw += Mouse.current.delta.ReadValue().x * .14f; pitch = Mathf.Clamp(pitch - Mouse.current.delta.ReadValue().y * .1f, -25f, 55f); }
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target.position + rotation * offset;
            transform.rotation = rotation;
        }
    }

    [RequireComponent(typeof(Health))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private float damage = 25f;
        [SerializeField] private float range = 2f;
        [SerializeField] private float radius = 1.1f;
        [SerializeField] private float cooldown = .55f;
        [SerializeField] private LayerMask targetMask = ~0;
        private float nextAttack;
        private Animator animator;
        private VisualAnimation visualAnimation;
        private void Awake() { animator = GetComponentInChildren<Animator>(); visualAnimation = GetComponent<VisualAnimation>(); }
        private void Update()
        {
            bool clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            if (!clicked || Time.time < nextAttack) return;
            ThirdPersonCamera.CaptureCursor();
            nextAttack = Time.time + cooldown;
            if (animator != null && HasParameter(animator, "Attack", AnimatorControllerParameterType.Trigger)) animator.SetTrigger("Attack");
            visualAnimation?.PlayAttack();
            StartCoroutine(ApplyHitAtImpact());
            AudioManager.PlaySfx(.18f, 150f);
        }

        private IEnumerator ApplyHitAtImpact()
        {
            // Keeps damage at the punch impact instead of immediately on click.
            yield return new WaitForSeconds(.16f);
            Vector3 center = transform.position + Vector3.up + transform.forward * range;
            var damagedTargets = new HashSet<IDamageable>();
            foreach (Collider hit in Physics.OverlapSphere(center, radius, targetMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.root == transform.root) continue;
                IDamageable target = hit.GetComponentInParent<IDamageable>(); if (target != null && damagedTargets.Add(target)) target.ApplyDamage(damage, gameObject);
            }
        }

        private static bool HasParameter(Animator target, string name, AnimatorControllerParameterType type)
        {
            foreach (var parameter in target.parameters)
                if (parameter.name == name && parameter.type == type) return true;
            return false;
        }
    }

    public sealed class PlayerStatus : MonoBehaviour
    {
        [SerializeField] private float shieldMultiplier = .5f;
        private Health health; private ThirdPersonPlayer player; private PlayerMovementEffects starterPlayer; private float shieldUntil;
        private void Awake()
        {
            health = GetComponent<Health>(); player = GetComponent<ThirdPersonPlayer>(); starterPlayer = GetComponent<PlayerMovementEffects>();
            if (health != null) health.Died += OnDied;
        }
        private void OnDestroy() { if (health != null) health.Died -= OnDied; }
        public void Heal(float value) => health.Heal(value);
        public void Speed(float multiplier, float duration)
        {
            if (starterPlayer != null) starterPlayer.ApplySpeedBoost(multiplier, duration);
            else if (player != null) player.ApplySpeedBoost(multiplier, duration);
        }
        public void Slow(float duration)
        {
            if (starterPlayer != null) starterPlayer.ApplySlow(duration);
            else if (player != null) player.ApplySlow(duration);
        }
        public void Shield(float duration) => shieldUntil = Mathf.Max(shieldUntil, Time.time + duration);
        public void ReceiveDamage(float value, GameObject source = null) { health.ApplyDamage(Time.time < shieldUntil ? value * shieldMultiplier : value, source); AudioManager.PlaySfx(.08f, 95f); }
        private void OnDied(Health _, GameObject __)
        {
            var starter = GetComponent<StarterAssets.ThirdPersonController>(); if (starter != null) starter.enabled = false;
            var combat = GetComponent<PlayerCombat>(); if (combat != null) combat.enabled = false;
        }
    }

    public sealed class HealthBarUI : MonoBehaviour
    {
        [SerializeField] private Health target;
        [SerializeField] private Image fill;
        [SerializeField] private float smoothing = 8f;
        private float desired = 1f;
        public void Configure(Health health, Image image) { target = health; fill = image; }
        private void Start()
        {
            if (target == null) target = GameObject.FindGameObjectWithTag("Player")?.GetComponent<Health>();
            if (target != null) Bind(target);
        }
        public void Bind(Health health)
        {
            if (target != null) target.Changed -= OnChanged;
            target = health;
            if (target == null) return;
            target.Changed += OnChanged;
            OnChanged(target.CurrentHealth, target.MaximumHealth);
            if (fill != null) fill.fillAmount = desired;
        }
        private void OnDestroy() { if (target != null) target.Changed -= OnChanged; }
        private void OnChanged(float current, float maximum) => desired = Mathf.Clamp01(current / maximum);
        private void Update() { if (fill != null) fill.fillAmount = Mathf.Lerp(fill.fillAmount, desired, Time.deltaTime * smoothing); }
    }
}
