using UnityEditor.PackageManager;
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

    [Tooltip("Seconds of a full stream someone has to take before they throw up")]
    [SerializeField, Min(0.05f)] private float soakSecondsToVomit = 0.6f;

    [Tooltip("Share of the soak shed per second once the stream moves off them")]
    [SerializeField, Min(0f)] private float soakDryingPerSecond = 0.5f;

    [Header("Vomit")]
    [Tooltip("Seconds spent doubled over throwing up")]
    [SerializeField, Min(0f)] private float vomitSeconds = 3f;

    [Tooltip("Trigger fired on the animator when they throw up")]
    [SerializeField] private string vomitTrigger = "Vomit";

    [Tooltip("Seconds after throwing up before they can be made to do it again")]
    [SerializeField, Min(0f)] private float vomitCooldown = 6f;

    [Tooltip("Seconds they hurry away forn once they are done")]
    [SerializeField, Min(0f)] private float fleeSeconds = 4f;

    [Tooltip("Where the mouth is, relative to the pedestrian character")]
    [SerializeField] private Vector3 mouthOffset = new Vector3(0f, 1.55f, 0.2f);

    [SerializeField] private ParticleSystem vomitEffect;

    [SerializeField] private Material vomitMaterial;
    [SerializeField] private AudioSource vomitSound;
    [SerializeField, Min(0f)] private float shakeOnVomit = 0.1f;

    [Header("Feedback")]
    [SerializeField] private AudioSource hitSound;
    [SerializeField] private ParticleSystem hitEffect;

    [Tooltip("Ignore anything else arriving while the last reaction is still playing.")]
    [SerializeField, Min(0f)] private float reactionCooldown = 0.5f;

    public static event System.Action<OwnerType, PedestrianReaction> OnPedestrianHit;
    public static event System.Action<OwnerType, PedestrianReaction> OnPedestrianVomit;

    private float nextReactionTime;
    private float soak;
    private int lastSoakedFrame = -2;
    private float vomitUntil;
    private float nextVomitTime;
    private bool fleeWhenDone;
    private bool hasVomitParameter;

    private static Material fallbackVomitMaterial;

    public bool IsVomiting => Time.time < vomitUntil;
    public float SoakProgress => Mathf.Clamp01(soak / soakSecondsToVomit);

    private Transform Body => agent != null ? agent.transform : transform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        OnPedestrianHit = null;
        OnPedestrianVomit = null;
    }

    void Awake()
    {
        if (agent == null) agent = GetComponentInParent<PedestrianAgent>();
        if (animator == null) animator = GetComponentInParent<Animator>();
        if (animator == null && agent != null) animator = agent.GetComponentInChildren<Animator>();

        hasVomitParameter = HasTrigger(vomitTrigger);

        Collider trigger = GetComponent<Collider>();

        if (trigger != null && !trigger.isTrigger)
        {
            Debug.LogWarning(
                $"[PedestrianReaction] The collider on '{name}' is not a trigger, so newspapers bounce off instead of " +
                $"being noticed. Tick Is Trigger.", this);
        }
    }

    void Update()
    {
        if (fleeWhenDone && !IsVomiting)
        {
            fleeWhenDone = false;

            if (agent != null)
            {
                agent.TurnBack();
                agent.Hurry(fleeSeconds);
            }
        }

        if (soak > 0f && Time.frameCount - lastSoakedFrame > 1)
            soak = Mathf.Max(0f, soak - soakDryingPerSecond * soakSecondsToVomit * Time.deltaTime);
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

    public void SoakWithPee(OwnerType owner, float strength)
    {
        if (!reactToPee || lastSoakedFrame == Time.frameCount) return;

        lastSoakedFrame = Time.frameCount;

        if (IsVomiting || Time.time < nextVomitTime) return;

        if (soak <= 0f && Time.time >= nextReactionTime)
            HitByPee();

        soak += Time.deltaTime * Mathf.Clamp01(strength);

        if (soak >= soakSecondsToVomit)
            Vomit(owner);
    }

    void Vomit(OwnerType owner)
    {
        soak = 0f;
        vomitUntil = Time.time + vomitSeconds;
        nextVomitTime = vomitUntil + vomitCooldown;
        nextReactionTime = vomitUntil;
        fleeWhenDone = true;

        if (agent != null)
            agent.Stun(vomitSeconds);

        if (animator != null)
        {
            if (hasVomitParameter)
                animator.SetTrigger(vomitTrigger);
            else if (!string.IsNullOrEmpty(reactionTrigger))
                animator.SetTrigger(reactionTrigger);
        }

        ParticleSystem effect = VomitEffect();

        if (effect != null)
        {
            Transform body = Body;

            effect.transform.SetPositionAndRotation(
                body.TransformPoint(mouthOffset),
                body.rotation * Quaternion.Euler(35f, 0f, 0f));

            effect.Play(true);
        }

        if (vomitSound != null)
            vomitSound.Play();

        if (shakeOnVomit > 0f)
            PlayersCameraController.Shake(owner, shakeOnVomit);

        OnPedestrianVomit?.Invoke(owner, this);
    }

    ParticleSystem VomitEffect()
    {
        if (vomitEffect == null)
            vomitEffect = BuildVomitEffect();

        return vomitEffect;
    }

    ParticleSystem BuildVomitEffect()
    {
        GameObject go = new GameObject("VomitEffect");
        go.SetActive(false);
        go.transform.SetParent(Body, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = Mathf.Max(0.1f, vomitSeconds * 0.6f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.gravityModifier = 1.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.55f, 0.62f, 0.18f),
            new Color(0.78f, 0.72f, 0.32f));

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 120f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.03f;

        ParticleSystem.CollisionModule collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.dampen = 0.7f;
        collision.bounce = 0.05f;
        collision.lifetimeLoss = 0.2f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = vomitMaterial != null ? vomitMaterial : FallbackVomitMaterial();

        go.SetActive(true);

        return ps;
    }

    static Material FallbackVomitMaterial()
    {
        if (fallbackVomitMaterial != null)
            return fallbackVomitMaterial;

        Shader shader = Shader.Find("Universal Render Pipelin/Particles/Unlit");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning("[PedestrianReaction] No particle shader found for the vomit effect. " +
                             "Assign a Vomit Material or a Vomit Effect");
            return null;
        }

        fallbackVomitMaterial = new Material(shader) { name = "Vomit (Runtime)" };

        return fallbackVomitMaterial;
    }

    bool HasTrigger(string parameter)
    {
        if (animator == null || string.IsNullOrEmpty(parameter) || animator.runtimeAnimatorController == null)
            return false;

        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == parameter)
                return true;
        }

        return false;
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
