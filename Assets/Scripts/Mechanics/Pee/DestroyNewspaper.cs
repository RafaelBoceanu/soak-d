using UnityEngine;

public class DestroyNewspaper : MonoBehaviour
{
    [Tooltip("Seconds of a full pressure stream a paper has to take before it is ruined.")]
    [SerializeField, Min(0f)] private float soakSecondsToRuin = 0.75f;

    [Tooltip("Least a stream can be worth, however feeble, so ruining a paper is never impossible.")]
    [SerializeField, Range(0.01f, 1f)] private float weakestUsefulStream = 0.2f;

    [Tooltip("Share of the soak a paper sheds per second once the stream moves off it.")]
    [SerializeField, Min(0f)] private float dryingPerSecond = 0.5f;

    [Tooltip("Extra meters around a live puddle that still count as soaking the paper.")]
    [SerializeField, Min(0f)] private float puddleReach = 0.1f;

    NewspaperDelivery delivery;
    Collider paperCollider;

    float soak;
    int lastSoakedFrame = -2;

    GameObject lastSource;
    PeeSystem lastSourceSystem;

    public float SoakProgress => soakSecondsToRuin <= 0f ? 0f : Mathf.Clamp01(soak / soakSecondsToRuin);

    void Awake()
    {
        paperCollider = GetComponent<Collider>();
    }

    Vector3 SoakPoint => paperCollider != null ? paperCollider.bounds.center : transform.position;

    public void Bind(NewspaperDelivery owner)
    {
        delivery = owner != null ? owner : GetComponentInParent<NewspaperDelivery>();
    }

    void OnEnable()
    {
        soak = 0f;
    }

    void Update()
    {
        if (delivery != null && delivery.wasDelivered &&
            PeePuddle.TryGetSoakingFlow(SoakPoint, puddleReach, out float puddleFlow))
        {
            Soak(Mathf.Max(puddleFlow, weakestUsefulStream));
            return;
        }

        if (soak <= 0f || Time.frameCount - lastSoakedFrame <= 1)
            return;

        soak = Mathf.Max(0f, soak - dryingPerSecond * soakSecondsToRuin * Time.deltaTime);
    }

    float StreamStrength(GameObject source)
    {
        if (source != lastSource)
        {
            lastSource = source;
            lastSourceSystem = source.GetComponentInParent<PeeSystem>();
        }

        if (lastSourceSystem == null)
            return 1f;

        return Mathf.Max(lastSourceSystem.CurrentFlow, weakestUsefulStream);
    }

    private void OnParticleCollision(GameObject other)
    {
        if (!other.CompareTag("Pee"))
            return;

        if (delivery == null || !delivery.wasDelivered)
            return;

        Soak(StreamStrength(other));
    }

    void Soak(float strength)
    {
        if (lastSoakedFrame == Time.frameCount)
            return;

        lastSoakedFrame = Time.frameCount;

        if (soakSecondsToRuin > 0f)
        {
            soak += Time.deltaTime * strength;

            if (soak < soakSecondsToRuin)
                return;
        }

        soak = 0f;

        delivery.NotifyNewspaperDestroyed();
    }
}
