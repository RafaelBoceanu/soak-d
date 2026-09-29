using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WitchMana))]
public class DiureticHexAbility : MonoBehaviour
{
    [SerializeField, Min(0f)] private float manaCost = 50f;

    [Header("Reach")]
    [Tooltip("Farthest the Boy can be for the hex to reach him")]
    [SerializeField, Min(1f)] private float range = 25f;
    [SerializeField] private bool needsLineOfSight = true;
    [Tooltip("What can block the hex")]
    [SerializeField] private LayerMask blockingMask = Physics.DefaultRaycastLayers;

    [Header("Curse")]
    [SerializeField, Min(0.1f)] private float curseSeconds = 6f;
    [Tooltip("How much faster water he has already drunk reched his bladded while hexed")]
    [SerializeField, Min(1f)] private float digestionMultiplier = 4f;
    [Tooltip("Bladder filled per second on top of that, so the hex applies even if he has not drunk")]
    [SerializeField, Min(0f)] private float fillPerSecond = 7f;

    [Header("Feedback")]
    [Tooltip("Looping effect parented to the Boy while he is cursed")]
    [SerializeField] private GameObject curseEffectPrefab;
    [SerializeField] private float effectHeight = 1.2f;
    [SerializeField] private ParticleSystem castEffect;
    [SerializeField] private AudioSource castSound;
    [Tooltip("Empty child where the bold leaves from")]
    [SerializeField] private Transform castPoint;
    [SerializeField] private SpellBolt boltPrefab;

    private WitchMana mana;
    private PlayerInputHandler inputHandler;
    private PlayerNeeds cursedTarget;
    private GameObject curseEffect;
    private readonly RaycastHit[] sightHits = new RaycastHit[16];

    Vector3 CastOrigin => castPoint != null ? castPoint.position : transform.position + Vector3.up * 1.5f;

    void Awake()
    {
        mana = GetComponent<WitchMana>();
        inputHandler = GetComponent<PlayerInputHandler>();
    }

    // Update is called once per frame
    void Update()
    {
        ClearFinishedCurse();

        PlayerInputContext input = TwoPlayerInputManager.GetPlayer(mana.Owner);

        if (input != null && input.HexPressed)
            TryCast();
    }

    public bool TryCast()
    {
        if (!mana.CanCast)
            return false;

        PlayerNeeds target = PlayerNeeds.ForOwner(MatchManager.Opponent(mana.Owner));

        if (target == null || !InReach(target))
        {
            mana.Fizzle();
            return false;
        }

        if (!mana.TrySpend(manaCost))
            return false;

        if (castSound != null)
            castSound.Play();

        if (castEffect != null)
            castEffect.Play();

        if (boltPrefab != null)
        {
            SpellBolt bolt = Instantiate(boltPrefab, CastOrigin, Quaternion.identity);
            bolt.Launch(target.transform, Vector3.up * effectHeight, () => Curse(target));
        }
        else
        {
            Curse(target);
        }

        return true;
    }

    void Curse(PlayerNeeds target)
    {
        if (target == null) return;

        target.ApplyDiuretic(curseSeconds, digestionMultiplier, fillPerSecond);

        if (curseEffectPrefab == null) return;

        if (curseEffect != null)
            Destroy(curseEffect);

        curseEffect = Instantiate(curseEffectPrefab, target.transform);
        curseEffect.transform.localPosition = Vector3.up * effectHeight;
        cursedTarget = target;
    }

    bool InReach(PlayerNeeds target)
    {
        Vector3 from = CastOrigin;
        Vector3 to = target.transform.position + Vector3.up * effectHeight;
        Vector3 gap = to - from;
        float distance = gap.magnitude;

        if (distance > range) return false;
        if (!needsLineOfSight || distance < 0.01f) return true;

        PlayerInputHandler targetInput = target.GetComponent<PlayerInputHandler>();

        int count = Physics.RaycastNonAlloc(from, gap / distance, sightHits, distance,
                                            blockingMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Transform hit = sightHits[i].collider.transform;

            if (BelongsTo(hit, inputHandler) || BelongsTo(hit, targetInput))
                continue;

            return false;
        }

        return true;
    }

    static bool BelongsTo(Transform hit, PlayerInputHandler player)
    {
        if (player == null) return false;
        if (hit.IsChildOf(player.transform)) return true;

        MountableVehicle vehicle = player.CurrentVehicle;
        return vehicle != null && hit.IsChildOf(vehicle.transform);
    }

    void ClearFinishedCurse()
    {
        if (curseEffect == null || (cursedTarget != null && cursedTarget.IsHexed))
            return;

        Destroy(curseEffect);
        curseEffect = null;
        cursedTarget = null;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 1f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
