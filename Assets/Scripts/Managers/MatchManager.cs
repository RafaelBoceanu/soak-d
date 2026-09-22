using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-200)]
public class MatchManager : MonoBehaviour
{
    public enum MatchPhase { Warmup, Running, Finished }
    public enum EndReason { TimeUp, RoundComplete, Knockout }
    public enum NeedPenalty { Ignore, LoseOneDelivery, Knockout }

    public struct MatchResult
    {
        public EndReason reason;
        public bool isDraw;
        public OwnerType winner;
        public OwnerType knockedOut;
        public PlayerNeeds.NeedFailure knockoutFailure;
        public int boyScore;
        public int witchScore;

        public string Headline() =>
            isDraw ? "Draw!" : (winner == OwnerType.Boy ? "Boy wins!" : "Witch wins!");

        public string Detail()
        {
            switch (reason)
            {
                case EndReason.RoundComplete:
                    return $"{winner} finished the whole round!";

                case EndReason.Knockout:
                    return knockoutFailure == PlayerNeeds.NeedFailure.BladderFull
                        ? $"{knockedOut} couldn't hold it in"
                        : $"{knockedOut} ran completely dry";

                default:
                    return "Time's up";
            }
        }
    }

    public static MatchManager instance;

    [Header("Round")]
    [SerializeField, Min(10f)] private float roundSeconds = 180f;

    [Tooltip("Hold the clock until the procedural map has registered its delivery zones")]
    [SerializeField] private bool waitForZones = true;

    [Tooltip("Hold the clock until the players have been placed in the scene.")]
    [SerializeField] private PlayerStartManager playerStart;

    [Tooltip("Start anyway if no zone has registered after this long.")]
    [SerializeField, Min(0f)] private float warmupTimeout = 10f;

    [Header("Win conditions")]
    [Tooltip("End the round the moment a player has delivered to every one of their own zones")]
    [SerializeField] private bool endOnAllDelivered = true;

    [Tooltip("What happens to a player whose bladder fills completely")]
    [SerializeField] private NeedPenalty onBladderFull = NeedPenalty.LoseOneDelivery;

    [Tooltip("What happens to a player who runs completely dry")]
    [SerializeField] private NeedPenalty onDehydrated = NeedPenalty.Ignore;

    public static event Action OnMatchStarted;
    public static event Action<float> OnTimeRemainingChanged;
    public static event Action<MatchResult> OnMatchEnded;

    private MatchPhase phase = MatchPhase.Warmup;
    private float timeRemaining;
    private float lastWholeSecondSent = -1f;

    private bool hasKnockout;
    private OwnerType knockoutLoser;
    private PlayerNeeds.NeedFailure knockoutFailure;

    public MatchPhase Phase => phase;
    public float TimeRemaining => timeRemaining;
    public float RoundSeconds => roundSeconds;
    public bool IsRunning => phase == MatchPhase.Running;

    public static OwnerType Opponent(OwnerType owner) =>
        owner == OwnerType.Boy ? OwnerType.Witch : OwnerType.Boy;

    void Awake()
    {
        instance = this;

        DeliveryScoreManager.ResetScores();

        timeRemaining = roundSeconds;
    }

    void OnEnable()
    {
        DeliveryScoreManager.onOwnerCompleted += HandleOwnerCompleted;
        PlayerNeeds.OnNeedCritical += HandleNeedCritical;
    }

    void OnDisable()
    {
        DeliveryScoreManager.onOwnerCompleted -= HandleOwnerCompleted;
        PlayerNeeds.OnNeedCritical -= HandleNeedCritical;
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
        
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        OnTimeRemainingChanged?.Invoke(timeRemaining);
        StartCoroutine(BeginWhenReady());
    }

    IEnumerator BeginWhenReady()
    {
        if (waitForZones)
        {
            float waited = 0f;

            while (TotalZones() == 0 && waited < warmupTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (TotalZones() == 0)
                Debug.LogWarning("[MatchManager] No delivery zones registered; starting the round anyway", this);
        }

        if (playerStart != null)
        {
            float startWait = 0f;

            while (!PlayerStartManager.HasStarted && startWait < warmupTimeout)
            {
                startWait += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        phase = MatchPhase.Running;
        OnMatchStarted?.Invoke();
    }

    // Update is called once per frame
    void Update()
    {
        if (phase != MatchPhase.Running)
            return;

        if (GameManager.instance != null && GameManager.instance.gameState == GameState.Pause)
            return;

        timeRemaining = Mathf.Max(0f, timeRemaining - Time.deltaTime);

        float whole = Mathf.Ceil(timeRemaining);

        if (!Mathf.Approximately(whole, lastWholeSecondSent))
        {
            lastWholeSecondSent = whole;
            OnTimeRemainingChanged?.Invoke(timeRemaining);
        }

        if (timeRemaining <= 0f)
            EndMatch(EndReason.TimeUp, null);
    }

    private void HandleOwnerCompleted(OwnerType owner)
    {
        if (!endOnAllDelivered)
            return;

        EndMatch(EndReason.RoundComplete, owner);
    }

    private void HandleNeedCritical(OwnerType owner, PlayerNeeds.NeedFailure failure)
    {
        if (phase != MatchPhase.Running)
            return;

        NeedPenalty penalty = failure == PlayerNeeds.NeedFailure.BladderFull ? onBladderFull : onDehydrated;

        switch (penalty)
        {
            case NeedPenalty.LoseOneDelivery:
                NewspaperDelivery.RuinOneDelivered(owner);
                break;

            case NeedPenalty.Knockout:
                hasKnockout = true;
                knockoutLoser = owner;
                knockoutFailure = failure;
                EndMatch(EndReason.Knockout, Opponent(owner));
                break;
        }
    }

    void EndMatch(EndReason reason, OwnerType? forcedWinner)
    {
        if (phase == MatchPhase.Finished)
            return;

        phase = MatchPhase.Finished;

        int boy = DeliveryScoreManager.GetScore(OwnerType.Boy);
        int witch = DeliveryScoreManager.GetScore(OwnerType.Witch);

        MatchResult result = new MatchResult
        {
            reason = reason,
            boyScore = boy,
            witchScore = witch,
            knockedOut = knockoutLoser,
            knockoutFailure = knockoutFailure,
        };

        if (forcedWinner.HasValue)
        {
            result.winner = forcedWinner.Value;
        }
        else if (boy == witch)
        {
            result.isDraw = true;
        }
        else
        {
            result.winner = boy > witch ? OwnerType.Boy : OwnerType.Witch;
        }

        Debug.Log($"[MatchManager] {result.Headline()} ({result.Detail()}) - Boy {boy}, Witch {witch}");

        OnMatchEnded?.Invoke(result);

        if (GameManager.instance != null)
            GameManager.instance.UpdateGameState(GameState.GameOver);
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        DeliveryScoreManager.ResetScores();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static int TotalZones()
    {
        int total = 0;

        foreach (OwnerType owner in TwoPlayerInputManager.Owners)
            total += DeliveryScoreManager.GetZoneCount(owner);

        return total;
    }
}
