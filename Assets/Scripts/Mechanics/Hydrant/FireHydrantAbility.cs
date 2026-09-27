using System;
using Unity.Cinemachine;
using UnityEngine;

public class FireHydrantAbility : MonoBehaviour
{
    [Tooltip("Left at the default it is taken from the PlayerInputHandler on the object")]
    [SerializeField] private OwnerType owner = OwnerType.Boy;

    [Header("Charge")]
    [SerializeField, Min(1f)] private float chargeToFill = 100f;
    [Tooltip("Passive charge per second")]
    [SerializeField, Min(0f)] private float chargePerSecond = 0.4f;
    [SerializeField, Min(0f)] private float chargePerOpponentPaperLost = 25f;
    [SerializeField, Min(0f)] private float chargePerPedestrianVomit = 15f;
    [SerializeField, Min(0f)] private float chargePerDelivery = 10f;
    [Tooltip("Start the round with a full meter. Used for testing in Scene")]
    [SerializeField] private bool startCharged = true;

    [Header("Placement")]
    [SerializeField] private FireHydrant hydrantPrefab;
    [SerializeField, Min(0f)] private float placeDistance = 1.5f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0.5f)] private float groundProbe = 4f;
    [SerializeField] private AudioSource activateSound;
    [SerializeField] private AudioSource notReadySound;

    public static event Action<OwnerType, float> OnChargeChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => OnChargeChanged = null;

    private PlayerInputHandler inputHandler;
    private PlayerNeeds playerNeeds;
    private FireHydrant activeHydrant;
    private float charge;
    private float lastChargeSent = -1f;
    private readonly int[] lastScores = new int[Enum.GetValues(typeof(OwnerType)).Length];

    public float Charge01 => Mathf.Clamp01(charge / chargeToFill);
    public bool IsReady => charge >= chargeToFill;

    bool RoundRunning => MatchManager.instance == null || MatchManager.instance.IsRunning;
    bool HydrantGushing => activeHydrant != null && activeHydrant.IsGushing;

    void Awake()
    {
        inputHandler = GetComponent<PlayerInputHandler>();
        playerNeeds = GetComponent<PlayerNeeds>();

        if (inputHandler != null)
            owner = inputHandler.Owner;
    }

    void OnEnable()
    {
        DeliveryScoreManager.OnScoreChanged += HandleScoreChanged;
        PedestrianReaction.OnPedestrianVomit += HandlePedestrianVomit;
    }

    void OnDisable()
    {
        DeliveryScoreManager.OnScoreChanged -= HandleScoreChanged;
        PedestrianReaction.OnPedestrianVomit -= HandlePedestrianVomit;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        foreach (OwnerType o in TwoPlayerInputManager.Owners)
            lastScores[(int)o] = DeliveryScoreManager.GetScore(o);

        charge = startCharged ? chargeToFill : 0f;
        PushCharge(true);
    }

    // Update is called once per frame
    void Update()
    {
        if (!RoundRunning)
            return;

        AddCharge(chargePerSecond * Time.deltaTime);

        PlayerInputContext input = TwoPlayerInputManager.GetPlayer(owner);

        if (input != null && input.UltimatePressed)
            TryActivate();
    }

    public bool TryActivate()
    {
        if (!IsReady || !RoundRunning || hydrantPrefab == null ||
            (playerNeeds != null && playerNeeds.IsHavingAccident))
        {
            if (notReadySound != null) notReadySound.Play();
            return false;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 spot = transform.position + forward * placeDistance;

        if (Physics.Raycast(spot + Vector3.up * (groundProbe * 0.5f), Vector3.down, out RaycastHit hit,
                            groundProbe, groundMask, QueryTriggerInteraction.Ignore))
            spot = hit.point;

        FireHydrant hydrant = Instantiate(hydrantPrefab, spot, Quaternion.LookRotation(forward, Vector3.up));

        charge = 0f;
        PushCharge(true);

        if (activateSound != null) activateSound.Play();

        activeHydrant = hydrant;
        hydrant.Open(owner);

        return true;
    }

    void HandleScoreChanged(OwnerType scorer, int score)
    {
        int previous = lastScores[(int)scorer];
        lastScores[(int)scorer] = score;

        if (!RoundRunning)
            return;

        if (scorer == owner && score > previous)
            AddCharge(chargePerDelivery * (score - previous));
        else if (scorer != owner && score < previous && !HydrantGushing)
            AddCharge(chargePerOpponentPaperLost * (previous - score));
    }

    void HandlePedestrianVomit(OwnerType culprit, PedestrianReaction pedestrian)
    {
        if (culprit == owner && RoundRunning)
            AddCharge(chargePerPedestrianVomit);
    }

    void AddCharge(float amount)
    {
        if (amount <= 0f || IsReady)
            return;

        charge = Mathf.Min(charge + amount, chargeToFill);
        PushCharge(false);
    }

    void PushCharge(bool force)
    {
        float rounded = Mathf.Floor(Charge01 * 100f) / 100f;

        if (!force && Mathf.Approximately(rounded, lastChargeSent))
            return;

        lastChargeSent = rounded;
        OnChargeChanged?.Invoke(owner, Charge01);
    }
}
