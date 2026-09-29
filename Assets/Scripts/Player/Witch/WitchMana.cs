using System;
using UnityEngine;

[DisallowMultipleComponent]
public class WitchMana : MonoBehaviour
{
    [Tooltip("Taken from the PlayerInputHandler if left at default")]
    [SerializeField] private OwnerType owner = OwnerType.Witch;

    [Header("Pool")]
    [SerializeField, Min(1f)] private float maxMana = 100f;
    [SerializeField, Min(0f)] private float startingMana = 25f;
    [Tooltip("Start the round with a full bar for testing purposes")]
    [SerializeField] private bool startFull = true;

    [Header("Gain")]
    [Tooltip("Passive mana per second")]
    [SerializeField, Min(0f)] private float manaPerSecond = 0.5f;
    [SerializeField, Min(0f)] private float manaPerDelivery = 8f;
    [SerializeField, Min(0f)] private float manaPerOpponentPaperLost = 15f;
    [SerializeField, Min(0f)] private float manaPerPedestrianVomit = 12f;

    [Header("Casting")]
    [Tooltip("Seconds after any cast before another spell can go off, so one press never casts twice")]
    [SerializeField, Min(0f)] private float castLockout = 1f;
    [Tooltip("Playerd when a spell cannot be cast")]
    [SerializeField] private AudioSource fizzleSound;

    public static event Action<OwnerType, float> OnManaChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => OnManaChanged = null;

    private PlayerNeeds playerNeeds;
    private PlayerMovement playerMovement;
    private float mana;
    private float lockedUntil;
    private float lastManaSent = -1f;
    private readonly int[] lastScores = new int[Enum.GetValues(typeof(OwnerType)).Length];

    public OwnerType Owner => owner;
    public float Mana => mana;
    public float Mana01 => Mathf.Clamp01(mana / maxMana);

    bool RoundRunning => MatchManager.instance == null || MatchManager.instance.IsRunning;

    void Awake()
    {
        playerNeeds = GetComponent<PlayerNeeds>();
        playerMovement = GetComponent<PlayerMovement>();

        PlayerInputHandler inputHandler = GetComponent<PlayerInputHandler>();

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

        mana = startFull ? maxMana : Mathf.Min(startingMana, maxMana);
        PushMana(true);
    }

    // Update is called once per frame
    void Update()
    {
        if (RoundRunning)
            Add(manaPerSecond * Time.deltaTime);
    }

    public bool CanCast
    {
        get
        {
            if (!RoundRunning) return false;
            if (GameManager.instance != null && GameManager.instance.gameState == GameState.Pause) return false;
            if (Time.time < lockedUntil) return false;
            if (playerNeeds != null && playerNeeds.IsHavingAccident) return false;

            return playerMovement == null || !playerMovement.IsStunned;
        }
    }

    public bool CanAfford(float cost) => mana >= cost;

    public bool TrySpend(float cost)
    {
        if (!CanCast || !CanAfford(cost))
        {
            Fizzle();
            return false;
        }

        mana -= cost;
        lockedUntil = Time.time + castLockout;
        PushMana(true);

        return true;
    }

    public void Fizzle()
    {
        if (fizzleSound != null)
            fizzleSound.Play();
    }

    void HandleScoreChanged(OwnerType scorer, int score)
    {
        int previous = lastScores[(int)scorer];
        lastScores[(int)scorer] = score;

        if (!RoundRunning)
            return;

        if (scorer == owner && score > previous)
            Add(manaPerDelivery * (score - previous));
        else if (scorer != owner && score < previous)
            Add(manaPerOpponentPaperLost * (previous - score));
    }

    void HandlePedestrianVomit(OwnerType culprit, PedestrianReaction pedestrian)
    {
        if (culprit == owner && RoundRunning)
            Add(manaPerPedestrianVomit);
    }

    void Add(float amount)
    {
        if (amount <= 0f || mana >= maxMana)
            return;

        mana = Mathf.Min(mana + amount, maxMana);
        PushMana(false);
    }

    void PushMana(bool force)
    {
        float rounded = Mathf.Floor(Mana01 * 100f) / 100f;

        if (!force && Mathf.Approximately(rounded, lastManaSent))
            return;

        lastManaSent = rounded;
        OnManaChanged?.Invoke(owner, Mana01);
    }
}
