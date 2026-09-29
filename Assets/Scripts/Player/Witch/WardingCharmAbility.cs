using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WitchMana))]
public class WardingCharmAbility : MonoBehaviour
{
    [SerializeField, Min(0f)] private float manaCost = 35f;

    [Header("Ward")]
    [Tooltip("Her delivered papers within this flat distance are warded")]
    [SerializeField, Min(1f)] private float radius = 15f;
    [SerializeField, Min(0.1f)] private float wardSeconds = 10f;

    [Header("Feedback")]
    [SerializeField] private AudioSource castSound;
    [SerializeField] private ParticleSystem castEffect;
    [Tooltip("Empty child the orbs leave from")]
    [SerializeField] private Transform castPoint;
    [SerializeField] private SpellBolt boltPrefab;

    private WitchMana mana;

    Vector3 CastOrigin => castPoint != null ? castPoint.position : transform.position + Vector3.up * 1.5f;

    void Awake()
    {
        mana = GetComponent<WitchMana>();        
    }

    // Update is called once per frame
    void Update()
    {
        PlayerInputContext input = TwoPlayerInputManager.GetPlayer(mana.Owner);

        if (input != null && input.WardPressed)
            TryCast();
    }

    public bool TryCast()
    {
        if (!mana.CanCast)
            return false;

        if (CountWardable() == 0)
        {
            mana.Fizzle();
            return false;
        }

        if (!mana.TrySpend(manaCost))
            return false;

        foreach (NewspaperDelivery zone in NewspaperDelivery.All)
        {
            if (!IsWardable(zone))
                continue;

            NewspaperDelivery warded = zone;

            if (boltPrefab != null)
            {
                SpellBolt bolt = Instantiate(boltPrefab, CastOrigin, Quaternion.identity);
                bolt.Launch(warded.transform, Vector3.up * 0.3f, () =>
                {
                    if (warded != null)
                        warded.Ward(wardSeconds);
                });
            }
            else
            {
                warded.Ward(wardSeconds);
            }
        }

        if (castSound != null) castSound.Play();
        if (castEffect != null) castEffect.Play();

        return true;
    }

    bool IsWardable(NewspaperDelivery zone)
    {
        if (zone == null || zone.AllowedOwner != mana.Owner || !zone.CanBeWarded)
            return false;

        Vector3 gap = zone.transform.position - transform.position;
        gap.y = 0f;

        return gap.sqrMagnitude <= radius * radius;
    }

    int CountWardable()
    {
        int count = 0;

        foreach (NewspaperDelivery zone in NewspaperDelivery.All)
        {
            if (IsWardable(zone))
                count++;
        }

        return count;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
