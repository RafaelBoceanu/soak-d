using System;
using System.Collections;
using UnityEngine;

public class PlayerNeeds : MonoBehaviour
{
    [Header("Bars")]
    public float maxHydration = 100f;
    public float maxPee = 100f;

    [Header("Pressure, measured against one round")]
    [Tooltip("How much of the hydration bar thirst alone drinks through over a full round.")]
    [SerializeField, Min(0f)] private float hydrationDrainedPerRound = 0.7f;

    [Tooltip("How much of the bladder fills on its own over a full round, for a player who " +
             "never touches a bottle. Kept small: drinking is meant to be the real source")]
    [SerializeField, Min(0f)] private float bladderFilledPerRound = 0.15f;

    [Tooltip("Round length assumed when there is no MatchManager in the scene, so the needs " +
             "still behave sensibly in a test scene.")]
    [SerializeField, Min(1f)] private float fallbackRoundSeconds = 180f;

    [Header("Effort by state")]
    [Tooltip("What standing still costs, as a multiple of the drain above")]
    [SerializeField, Min(0f)] private float idleEffort = 1f;

    [Tooltip("What walking costs, as a multiple of the idle drain.")]
    [SerializeField, Min(0f)] private float walkingEffort = 1.25f;

    [Tooltip("What sprinting costs, as a multiple of the idle drain.")]
    [SerializeField, Min(0f)] private float sprintingEffort = 1.9f;

    [Tooltip("What riding a vehicle costs, as a multiple of the idle drain.")]
    [SerializeField, Min(0f)] private float ridingEffort = 1.35f;

    [Header("Digestion")]
    [Tooltip("Bladder filled per point of hydration swallowed. 1 means every drop comes back out.")]
    [SerializeField, Range(0f, 2f)] private float bladderPerHydration = 1f;

    [Tooltip("Seconds for a full bladder's worth of water to pass from stomach through to " +
             "the bladder. One bottle arrives proportionally sooner, so this is really the lag " +
             "between taking a drink and paying for it.")]
    [SerializeField, Min(0.1f)] private float digestionSecondsPerBar = 60f;

    [Header("Relief")]
    [Tooltip("Bar drained per second on an almost empty bladder. A weak dribble.")]
    [SerializeField, Min(0f)] private float peeDrainAtEmpty = 4f;

    [Tooltip("Bar drained per second on a full bladder. Matches the pressure PeeSystem draws.")]
    [SerializeField, Min(0f)] private float peeDrainAtFull = 12f;

    [Header("Running dry")]
    [Tooltip("Below this share of the hydration bar the player is too dry to sprint and the " +
             "stream weakens.")]
    [SerializeField, Range(0f, 1f)] private float dehydratedBelow = 0.2f;

    [Tooltip("Flow left at zero hydration, as a share of a healthy stream.")]
    [SerializeField, Range(0f, 1f)] private float flowWhenBoneDry = 0.35f;

    [Header("Accident")]
    [Tooltip("Seconds the player is rooted after wetting themselves.")]
    [SerializeField, Min(0f)] private float accidentStunSeconds = 3f;

    [Tooltip("Share of the bladder left once the accident is over.")]
    [SerializeField, Range(0f, 1f)] private float bladderAfterAccident = 0f;

    [HideInInspector] public float hydration;
    [HideInInspector] public float pee;

    public enum CharacterType { Boy, Witch }
    public CharacterType characterType;

    public enum NeedFailure { Dehydrated, BladderFull }

    public static event Action<OwnerType, NeedFailure> OnNeedCritical;
    public static event Action<OwnerType> OnAccident;

    public OwnerType Owner =>
        characterType == CharacterType.Boy ? OwnerType.Boy : OwnerType.Witch;

    private bool bladderReported;
    private bool dehydrationReported;

    private float digesting;

    private float hydrationDrainPerSecond;
    private float bladderTricklePerSecond;
    private float digestionPerSecond;

    private bool inAccident;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        OnNeedCritical = null;
        OnAccident = null;
    }

    [SerializeField] private CanvasManager canvasManager;

    [SerializeField] private PlayerMovement playerMovement;

    [SerializeField] private PlayerInputHandler inputHandler;

    [SerializeField] private PeePuddle peePuddle;

    public bool IsDehydrated => hydration <= maxHydration * dehydratedBelow;
    public bool CanSprint => !IsDehydrated;
    public bool IsHavingAccident => inAccident;

    public float FlowScale
    {
        get
        {
            float threshold = maxHydration * dehydratedBelow;

            if (hydration >= threshold || threshold <= 0f)
                return 1f;

            return Mathf.Lerp(flowWhenBoneDry, 1f, hydration / threshold);
        }
    }

    void Awake()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (inputHandler == null)
            inputHandler = GetComponent<PlayerInputHandler>();

        if (peePuddle == null)
            peePuddle = GetComponent<PeePuddle>();
    }

    void OnDisable()
    {
        if (!inAccident) return;

        inAccident = false;

        if (playerMovement != null)
            playerMovement.SetStunned(false);

        // The coroutine cannot finish on a disabled object, so a frozen vehicle would never
        // get its controls back.
        if (inputHandler != null && inputHandler.CurrentVehicle != null)
            inputHandler.CurrentVehicle.SetRiderControl(true);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        hydration = maxHydration;
        pee = 0f;
        digesting = 0f;

        ResolveRates();
    }

    void ResolveRates()
    {
        float roundSeconds = MatchManager.instance != null
            ? MatchManager.instance.RoundSeconds
            : fallbackRoundSeconds;

        if (roundSeconds <= 0f)
            roundSeconds = fallbackRoundSeconds;

        hydrationDrainPerSecond = maxHydration * hydrationDrainedPerRound / roundSeconds;
        bladderTricklePerSecond = maxPee * bladderFilledPerRound / roundSeconds;
        digestionPerSecond = digestionSecondsPerBar > 0f ? maxPee / digestionSecondsPerBar : 0f;
    }

    // Update is called once per frame
    void Update()
    {
        if (MatchManager.instance == null || MatchManager.instance.IsRunning)
        {
            Tick(Time.deltaTime);
            CheckCritical();
        }

        PushToCanvas();
    }

    void Tick(float deltaTime)
    {
        hydration = Mathf.Clamp(hydration - hydrationDrainPerSecond * CurrentEffort * deltaTime, 0f, maxHydration);

        if (digesting > 0f)
        {
            float arriving = Mathf.Min(digesting, digestionPerSecond * deltaTime);

            digesting -= arriving;
            pee = Mathf.Clamp(pee + arriving, 0f, maxPee);
        }

        pee = Mathf.Clamp(pee + bladderTricklePerSecond * deltaTime, 0f, maxPee);
    }

    public float CurrentEffort
    {
        get
        {
            if (inputHandler != null && inputHandler.IsRiding)
                return ridingEffort;

            if (playerMovement == null || !playerMovement.IsMoving)
                return idleEffort;

            return playerMovement.IsSprinting ? sprintingEffort : walkingEffort;
        }
    }

    void PushToCanvas()
    {
        if (canvasManager == null)
            return;

        float hydrationNormalized = hydration / maxHydration;
        float peeNormalized = pee / maxPee;

        if (characterType == CharacterType.Boy)
        {
            canvasManager.SetBoyHydration(hydrationNormalized);
            canvasManager.SetBoyPee(peeNormalized);
        }
        else if (characterType == CharacterType.Witch)
        {
            canvasManager.SetWitchHydration(hydrationNormalized);
            canvasManager.SetWitchPee(peeNormalized);
        }
    }

    private void CheckCritical()
    {
        if (pee >= maxPee)
        {
            if (!bladderReported)
            {
                bladderReported = true;
                OnNeedCritical?.Invoke(Owner, NeedFailure.BladderFull);
                StartCoroutine(Accident());
            }
        }
        else if (pee < maxPee * 0.95f)
        {
            bladderReported = false;
        }

        if (hydration <= 0f)
        {
            if (!dehydrationReported)
            {
                dehydrationReported = true;
                OnNeedCritical?.Invoke(Owner, NeedFailure.Dehydrated);
            }
        }
        else if (hydration > maxHydration * 0.05f)
        {
            dehydrationReported = false;
        }
    }

    IEnumerator Accident()
    {
        inAccident = true;

        pee = maxPee * bladderAfterAccident;
        digesting = 0f;
        bladderReported = false;

        OnAccident?.Invoke(Owner);

        // A rider has PlayerMovement disabled, so stunning it alone does nothing and they
        // carry on riding. Put them on the ground first: coming off the bike is the point.
        MountableVehicle heldVehicle = null;

        if (inputHandler != null && inputHandler.IsRiding)
        {
            heldVehicle = inputHandler.CurrentVehicle;

            if (inputHandler.ForceDismount())
                heldVehicle = null;
            else if (heldVehicle != null)
                heldVehicle.SetRiderControl(false);   // mid air, so freeze it where it is
        }

        if (playerMovement != null)
            playerMovement.SetStunned(true);

        if (peePuddle != null)
            peePuddle.Spill(transform.position);

        yield return new WaitForSeconds(accidentStunSeconds);

        if (playerMovement != null)
            playerMovement.SetStunned(false);

        if (heldVehicle != null)
            heldVehicle.SetRiderControl(true);

        inAccident = false;
    }

    public void Drink (float amount)
    {
        if (amount <= 0f) return;

        hydration = Mathf.Clamp(hydration + amount, 0f, maxHydration);

        digesting += amount * bladderPerHydration;
    }

    public float ReliefRatePerSecond =>
        Mathf.Lerp(peeDrainAtEmpty, peeDrainAtFull, maxPee > 0f ? pee / maxPee : 0f) * FlowScale;

    public float Relieve(float deltaTime)
    {
        float wanted = ReliefRatePerSecond * deltaTime;
        float spent = Mathf.Min(wanted, pee);

        pee -= spent;

        return spent;
    }

    public void Pee (float amount)
    {
        pee = Mathf.Clamp(pee - amount, 0f, maxPee);
    }
}
