using UnityEngine;

namespace CursedWilds
{
    [RequireComponent(typeof(Health))]
    public sealed class VisualAnimation : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftArm;
        [SerializeField] private Transform rightArm;
        [SerializeField] private Transform leftLeg;
        [SerializeField] private Transform rightLeg;
        [SerializeField] private float idleBob = .06f;
        [SerializeField] private float walkSpeed = 8f;
        private Health health;
        private Vector3 bodyStart;
        private bool moving;
        private float attackUntil;

        public void Configure(Transform visualBody, Transform visualHead, Transform visualLeftArm, Transform visualRightArm, Transform visualLeftLeg, Transform visualRightLeg)
        {
            body = visualBody; head = visualHead; leftArm = visualLeftArm; rightArm = visualRightArm; leftLeg = visualLeftLeg; rightLeg = visualRightLeg;
        }

        private void Awake()
        {
            health = GetComponent<Health>();
            if (health == null) health = gameObject.AddComponent<Health>();
            if (body != null) bodyStart = body.localPosition;
            health.Died += OnDeath;
        }

        private void OnDestroy()
        {
            if (health != null) health.Died -= OnDeath;
        }

        private void Update()
        {
            if (health == null || health.IsDead) return;
            float cycle = Time.time * (moving ? walkSpeed : 2.5f);
            if (body != null) body.localPosition = bodyStart + Vector3.up * Mathf.Sin(cycle) * (moving ? idleBob * 1.8f : idleBob);
            float swing = moving ? Mathf.Sin(cycle) * 38f : Mathf.Sin(cycle) * 4f;
            if (leftArm != null) leftArm.localRotation = Quaternion.Euler(swing, 0f, 0f);
            if (rightArm != null) rightArm.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            if (leftLeg != null) leftLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            if (rightLeg != null) rightLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
            if (Time.time < attackUntil)
            {
                float punch = Mathf.Sin((attackUntil - Time.time) * 18f) * 85f;
                if (rightArm != null) rightArm.localRotation = Quaternion.Euler(-punch, 0f, 0f);
            }
            if (head != null) head.localRotation = Quaternion.Euler(0f, Mathf.Sin(cycle * .5f) * 8f, 0f);
        }

        public void SetMoving(bool isMoving) => moving = isMoving;
        public void PlayAttack(float duration = .3f) => attackUntil = Mathf.Max(attackUntil, Time.time + duration);
        private void OnDeath(Health _, GameObject __)
        {
            if (body != null) body.localRotation = Quaternion.Euler(0f, 0f, 85f);
            if (leftArm != null) leftArm.localRotation = Quaternion.Euler(0f, 0f, 100f);
            if (rightArm != null) rightArm.localRotation = Quaternion.Euler(0f, 0f, -100f);
        }
    }
}
