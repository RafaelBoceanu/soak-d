using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class WaterBalloon : MonoBehaviour
{
    [Header("Splash")]
    [SerializeField, Min(0.1f)] private float splashRadius = 1.6f;
    [SerializeField, Min(0.01f)] private float spreadSeconds = 0.35f;
    [Tooltip("Seconds the full size puddle keeps soaking")]
    [SerializeField, Min(0f)] private float soakSeconds = 1.2f;

    [Header("Passers by")]
    [SerializeField] private LayerMask pedestrianMask = 0;
    [SerializeField, Range(0f, 1f)] private float pedestrianSoakStrength = 1f;

    [Header("Fligh")]
    [Tooltip("Seconds after the throw during which collisions are ignored, so it doesn't burst on the thrower")]
    [SerializeField, Min(0f)] private float armDelay = 0.08f;
    [SerializeField, Min(0.5f)] private float maxLifetime = 5f;

    [Header("Feedback")]
    [Tooltip("Hidden when it bursts")]
    [SerializeField] private GameObject visual;
    [SerializeField] private ParticleSystem burstEffect;
    [SerializeField] private AudioSource burstSound;
    [SerializeField, Min(0f)] private float shakeOnBurst = 0.15f;

    private readonly Collider[] splashHits = new Collider[16];

    private Rigidbody body;
    private PeePuddle puddle;
    private OwnerType owner;
    private float launchedAt;
    private bool burst;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        launchedAt = Time.time;

        if (pedestrianMask.value == 0)
            pedestrianMask = LayerMask.GetMask("Pedestrian");
    }

    public void Launch(OwnerType thrower, PeePuddle source)
    {
        owner = thrower;
        puddle = source;
        launchedAt = Time.time;
    }

    // Update is called once per frame
    void Update()
    {
        if (!burst && Time.time - launchedAt >= maxLifetime)
            Burst(transform.position);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (burst || Time.time - launchedAt < armDelay)
            return;

        Burst(collision.contactCount > 0 ? collision.GetContact(0).point : transform.position);
    }

    void Burst(Vector3 point)
    {
        burst = true;

        if (puddle != null)
            puddle.Splash(point, splashRadius, spreadSeconds, soakSeconds);

        body.isKinematic = true;

        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        if (visual != null)
            visual.SetActive(false);
        else
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                if (burstEffect == null || !r.transform.IsChildOf(burstEffect.transform))
                    r.enabled = false;

        if (burstEffect != null)
        {
            burstEffect.transform.SetPositionAndRotation(point, Quaternion.identity);
            burstEffect.Play(true);
        }

        if (burstSound != null)
            burstSound.Play();

        if (shakeOnBurst > 0f)
            PlayersCameraController.Shake(owner, shakeOnBurst);

        StartCoroutine(SoakPassersBy(point));
    }

    IEnumerator SoakPassersBy(Vector3 point)
    {
        float until = Time.time + spreadSeconds + soakSeconds;

        while (Time.time < until)
        {
            int count = Physics.OverlapSphereNonAlloc(point, splashRadius, splashHits,
                                                      pedestrianMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                PedestrianReaction pedestrian = splashHits[i].GetComponentInParent<PedestrianReaction>();

                if (pedestrian != null)
                    pedestrian.SoakWithPee(owner, pedestrianSoakStrength);
            }

            yield return null;
        }

        yield return new WaitForSeconds(1f);

        Destroy(gameObject);
    }
}
