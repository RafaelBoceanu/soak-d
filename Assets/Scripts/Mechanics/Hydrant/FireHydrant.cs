using System.Collections;
using UnityEngine;

public class FireHydrant : MonoBehaviour
{
    [Header("Flood")]
    [SerializeField, Min(0.5f)] private float floodRadius = 12f;
    [SerializeField, Min(0.01f)] private float spreadSeconds = 2.5f;
    [Tooltip("Seconds the hydrant gushed, spread included")]
    [SerializeField, Min(0.1f)] private float gushSeconds = 6f;
    [Tooltip("Most papers one hydrant mai ruin")]
    [SerializeField, Min(0)] private int maxPapersRuined = 3;

    [Header("Look")]
    [Tooltip("Decal material using 'Shader Graphs/Decal'")]
    [SerializeField] private Material floodMaterial;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0f)] private float floodLingerSeconds = 8f;
    [SerializeField, Min(0.01f)] private float floodFadeSeconds = 4f;
    [SerializeField] private ParticleSystem fountain;
    [SerializeField] private AudioSource gushSound;
    [SerializeField, Min(0f)] private float shakeOnOpen = 0.35f;
    [Tooltip("Seconds the hydrant stays once it has stopped")]
    [SerializeField, Min(0f)] private float removeAfterSeconds = 4f;

    private OwnerType openedBy;
    private bool open;
    private int ruined;
    private float currentRadius;

    public bool IsGushing => open;

    public void Open(OwnerType opener)
    {
        if (open) return;

        openedBy = opener;
        open = true;
        ruined = 0;

        StartCoroutine(Gush());
    }

    IEnumerator Gush()
    {
        OwnerType target = MatchManager.Opponent(openedBy);

        if (fountain != null) fountain.Play(true);
        if (gushSound != null) gushSound.Play();

        if (shakeOnOpen > 0f)
        {
            PlayersCameraController.Shake(openedBy, shakeOnOpen);
            PlayersCameraController.Shake(target, shakeOnOpen);
        }

        SpawnFloodDecal();

        float elapsed = 0f;

        while (elapsed < gushSeconds)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / spreadSeconds);
            currentRadius = floodRadius * (1f - (1f - t) * (1f - t));

            FloodPapers(target);

            yield return null;
        }

        open = false;

        if (fountain != null) fountain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (gushSound != null) gushSound.Stop();

        if (removeAfterSeconds > 0f)
            Destroy(gameObject, removeAfterSeconds);
    }

    void FloodPapers(OwnerType target)
    {
        if (maxPapersRuined > 0 && ruined >= maxPapersRuined)
            return;

        Vector3 centre = transform.position;
        float reach = currentRadius * currentRadius;

        foreach (NewspaperDelivery zone in NewspaperDelivery.All)
        {
            if (zone == null || !zone.wasDelivered || zone.AllowedOwner != target)
                continue;

            Vector3 gap = zone.transform.position - centre;
            gap.y = 0f;

            if (gap.sqrMagnitude > reach)
                continue;

            zone.NotifyNewspaperDestroyed();
            ruined++;

            if (maxPapersRuined > 0 && ruined >= maxPapersRuined)
                return;
        }
    }

    void SpawnFloodDecal()
    {
        if (floodMaterial == null)
            return;

        if (!Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit,
                             3f, groundMask, QueryTriggerInteraction.Ignore))
            return;

        VomitPuddle.Spawn(hit, floodRadius, 0f, spreadSeconds,
                          Mathf.Max(0f, gushSeconds - spreadSeconds) + floodLingerSeconds,
                          floodFadeSeconds, floodMaterial);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
