using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PedestrianReaction : MonoBehaviour
{
    public enum Penalty
    {
        None,
        LoseOneDelivery
    }

    [Header("References")]
    [Tooltip("Left empty, the agent and animator are looked up in the parents.")]
    [SerializeField] private PedestrianAgent agent;
    [SerializeField] private Animator animator;

    [Header("Newspaper")]
    [Tooltip("What hitting a passer by costs the player who threw it.")]
    [SerializeField] private Penalty onNewspaperHit = Penalty.None;

    [SerializeField, Min(0f)] private float stunSeconds = 2f;

    [Tooltip("Trigger fired on the pedestrian's animator. Empty for no reaction clip.")]
    [SerializeField] private string reactionTrigger = "Hit";

    [Tooltip("Off, the paper bounces off and can still be picked up or land in a zone.")]
    [SerializeField] private bool destroyNewspaperOnHit = true;

    [SerializeField, Min(0f)] private float shakeOnHit = 0.15f;

    [Header("Pee")]
    [SerializeField] private bool reactToPee = true;
    [SerializeField, Min(0f)] private float peeStunSeconds = 1.5f;

    [Header("Feedback")]
    [SerializeField] private AudioSource hitSound;
    [SerializeField] private ParticleSystem hitEffect;

    [Tooltip("Ignore anything else arriving while the last reaction is still playing.")]
    [SerializeField, Min(0f)] private float reactionCooldown = 0.5f;

    public static event System.Action<OwnerType, PedestrianReaction> OnPedestrianHit;

    private float nextReactionTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        OnPedestrianHit = null;
    }

    void Awake()
    {
        if (agent == null) agent = GetComponentInParent<PedestrianAgent>();
        if (animator == null) animator = GetComponentInParent<Animator>();

        Collider trigger = GetComponent<Collider>();

        if (trigger != null && !trigger.isTrigger)
        {
            Debug.LogWarning(
                $"[PedestrianReaction] The collider on '{name}' is not a trigger, so newspapers bounce off instead of " +
                $"being noticed. Tick Is Trigger.", this);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (Time.time < nextReactionTime) return;

        ThrownProjectile projectile = other.GetComponent<ThrownProjectile>();

        if (projectile == null)
            projectile = other.GetComponentInParent<ThrownProjectile>();

        if (projectile != null)
        {
            HitByNewspaper(projectile);
            return;
        }

        if (reactToPee && other.CompareTag("Pee"))
            HitByPee();
    }

    void HitByNewspaper(ThrownProjectile projectile)
    {
        nextReactionTime = Time.time + reactionCooldown;

        React(stunSeconds);

        if (shakeOnHit > 0f)
            PlayersCameraController.Shake(projectile.owner, shakeOnHit);

        if (onNewspaperHit == Penalty.LoseOneDelivery)
            DeliveryScoreManager.ReportDeliveryLost(projectile.owner);

        OnPedestrianHit?.Invoke(projectile.owner, this);

        if (destroyNewspaperOnHit)
            Destroy(projectile.gameObject);
    }

    void HitByPee()
    {
        nextReactionTime = Time.time + reactionCooldown;

        React(peeStunSeconds);
    }

    void React(float seconds)
    {
        if (agent != null)
        {
            agent.Stun(seconds);
            agent.TurnBack();
        }

        if (animator != null && !string.IsNullOrEmpty(reactionTrigger))
            animator.SetTrigger(reactionTrigger);

        if (hitSound != null)
            hitSound.Play();

        if (hitEffect != null)
            hitEffect.Play();
    }
}
