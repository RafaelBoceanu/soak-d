using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CanvasManager : MonoBehaviour
{
    [SerializeField] private GameObject deliveryPanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Slider boyHydrationSlider;
    [SerializeField] private Slider boyPeeSlider;
    [SerializeField] private Slider witchHydrationSlider;
    [SerializeField] private Slider witchPeeSlider;

    [Header("Match")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text matchTimerText;
    [SerializeField] private TMP_Text boyScoreText;
    [SerializeField] private TMP_Text witchScoreText;
    [SerializeField] private TMP_Text resultHeadlineText;
    [SerializeField] private TMP_Text resultDetailText;
    [SerializeField] private ParticleSystem boyConfetti;
    [SerializeField] private ParticleSystem witchConfetti;

    [Tooltip("{0} is deliveries made, {1} the zones that player owns.")]
    [SerializeField] private string scoreFormat = "{0}/{1}";

    [Tooltip("Seconds left at which the clock turns urgent.")]
    [SerializeField] private float lowTimeSeconds = 30f;
    [SerializeField] private Color normalTimeColor = Color.white;
    [SerializeField] private Color lowTimeColor = new Color(1f, 0.35f, 0.3f);

    [Header("Inventory")]
    [SerializeField] private TMP_Text boyNewspaperCount;
    [SerializeField] private TMP_Text boyWaterBottleCount;
    [SerializeField] private TMP_Text witchNewspaperCount;
    [SerializeField] private TMP_Text witchWaterBottleCount;
    [SerializeField] private Image boyNewspaperIcon;
    [SerializeField] private Image boyWaterBottleIcon;
    [SerializeField] private Image witchNewspaperIcon;
    [SerializeField] private Image witchWaterBottleIcon;

    [Tooltip("{0} is the amount held, {1} the maximum. \"{0}\" shows 3, \"{0}/{1}\" shows 3/6.")]
    [SerializeField] private string inventoryCountFormat = "{0}/{1}";

    [Header("Feedback")]
    [SerializeField] private float punchAmount = 0.35f;
    [SerializeField] private float punchDuration = 0.25f;
    [SerializeField]
    private AnimationCurve punchCurve =
        new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.30f, 1f), new Keyframe(1f, 0f));
    [SerializeField] private float shakeAmplitude = 6f;
    [SerializeField] private float shakeDuration = 0.35f;
    [SerializeField, Min(0f)] private float urgentTimeSeconds = 10f;
    [SerializeField] private Color scoreLossColor = new Color(1f, 0.35f, 0.3f);
    [SerializeField] private Color emptyCountColor = new Color(1f, 0.4f, 0.35f);
    [SerializeField] private Color normalCountColor = Color.white;

    private readonly Dictionary<RectTransform, Coroutine> scaleTweens =
        new Dictionary<RectTransform, Coroutine>();
    private readonly Dictionary<RectTransform, Coroutine> shakeTweens =
        new Dictionary<RectTransform, Coroutine>();
    private readonly Dictionary<RectTransform, Vector2> homePositions =
        new Dictionary<RectTransform, Vector2>();

    private readonly int[] lastScores =
        new int[System.Enum.GetValues(typeof(OwnerType)).Length];
    private Color boyScoreBase, witchScoreBase;

    [Header("Need bar colours")]
    [SerializeField] private Gradient peeFillGradient;
    [SerializeField] private Image boyPeeFill;
    [SerializeField] private Image witchPeeFill;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        GameManager.OnGameStateChanged += GameManagerOnGameStateChanged;
        PlayerInventory.OnAnyItemCountChanged += InventoryOnItemCountChanged;
        MatchManager.OnTimeRemainingChanged += MatchOnTimeRemainingChanged;
        MatchManager.OnMatchEnded += MatchOnMatchEnded;
        DeliveryScoreManager.OnScoreChanged += ScoreOnChanged;
        DeliveryScoreManager.onZoneCountChanged += ZoneCountOnChanged;
    }

    void OnDestroy()
    {
        GameManager.OnGameStateChanged -= GameManagerOnGameStateChanged;
        PlayerInventory.OnAnyItemCountChanged -= InventoryOnItemCountChanged;
        MatchManager.OnTimeRemainingChanged -= MatchOnTimeRemainingChanged;
        MatchManager.OnMatchEnded -= MatchOnMatchEnded;
        DeliveryScoreManager.OnScoreChanged -= ScoreOnChanged;
        DeliveryScoreManager.onZoneCountChanged -= ZoneCountOnChanged;
    }

    void Start()
    {
        RefreshInventoryLabels();

        RefreshScoreLabels();

        foreach (OwnerType owner in TwoPlayerInputManager.Owners)
            lastScores[(int)owner] = DeliveryScoreManager.GetScore(owner);

        if (boyScoreBase != null)   boyScoreBase = boyScoreText.color;
        if (witchScoreBase != null) witchScoreBase = witchScoreText.color;

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    private void GameManagerOnGameStateChanged(GameState state)
    {
        deliveryPanel.SetActive(state == GameState.Delivery);

        if (pausePanel != null)
            pausePanel.SetActive(state == GameState.Pause);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(state == GameState.GameOver);

        bool frozen = state == GameState.Pause || state == GameState.GameOver;

        Time.timeScale = frozen ? 0f : 1f;
        Cursor.lockState = frozen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = frozen;
    }

    public void ResumeGame()
    {   GameManager.instance.UpdateGameState(GameManager.instance.PreviousState);
        if (GameManager.instance.PreviousState == GameState.Handcar)
        {
            var handcar = GameManager.instance.HandcarPrefab;
            if (handcar != null)
                handcar.GetComponent<HandcarMovement>().enabled = true; // Re-enable handcar movement
        }
        else if (GameManager.instance.PreviousState == GameState.Delivery)
        {
            var boyCamera = GameManager.instance.BoyCamera;
            var witchCamera = GameManager.instance.WitchCamera;
            if (boyCamera != null)
                boyCamera.SetActive(true); // Re-enable camera control
            if (witchCamera != null)
                witchCamera.SetActive(true); // Re-enable camera control

        }
    }

    public void QuitGame()
    {
        // Quit the game
        Debug.Log("Game is quitting...");
        Application.Quit();
    }

    public void SetBoyHydration(float value) => boyHydrationSlider.value = value;
    public void SetBoyPee(float value)
    {
        boyPeeSlider.value = value;
        if (boyPeeFill != null) boyPeeFill.color = peeFillGradient.Evaluate(value);
    }
    public void SetWitchHydration(float value) => witchHydrationSlider.value = value;
    public void SetWitchPee(float value)
    {
        witchPeeSlider.value = value;
        if (witchPeeFill != null) witchPeeFill.color = peeFillGradient.Evaluate(value);
    }

    #region Inventory counters
    public void RefreshInventoryLabels()
    {
        foreach (OwnerType owner in TwoPlayerInputManager.Owners)
        {
            PlayerInventory inventory = PlayerInventory.ForOwner(owner);

            foreach (InventoryItemType item in InventoryItems)
            {
                int count = inventory != null ? inventory.GetCount(item) : 0;
                int capacity = inventory != null ? inventory.GetCapacity(item) : 0;

                SetInventoryLabel(owner, item, count, capacity);
            }
        }
    }

    static readonly InventoryItemType[] InventoryItems =
        (InventoryItemType[])System.Enum.GetValues(typeof(InventoryItemType));

    private void InventoryOnItemCountChanged(OwnerType owner, InventoryItemType item, int count)
    {
        PlayerInventory inventory = PlayerInventory.ForOwner(owner);
        int capacity = inventory != null ? inventory.GetCapacity(item) : 0;

        SetInventoryLabel(owner, item, count, capacity);

        TMP_Text label = InventoryLabel(owner, item);

        if (label != null)
            PunchScale(label.rectTransform, punchAmount * 0.6f);
    }

    private void SetInventoryLabel(OwnerType owner, InventoryItemType item, int count, int capacity)
    {
        TMP_Text label = InventoryLabel(owner, item);

        if (label == null)
            return;

        label.text = string.Format(inventoryCountFormat, count, capacity);
        label.color = count == 0 ? emptyCountColor : normalCountColor;

        Image icon = InventoryIcon(owner, item);

        if (icon != null)
            icon.color = count == 0 ? emptyCountColor : normalCountColor;
    }

    private TMP_Text InventoryLabel(OwnerType owner, InventoryItemType item)
    {
        bool boy = owner == OwnerType.Boy;

        switch (item)
        {
            case InventoryItemType.Newspaper:
                return boy ? boyNewspaperCount : witchNewspaperCount;

            case InventoryItemType.WaterBottle:
                return boy ? boyWaterBottleCount : witchWaterBottleCount;

            default:
                return null;
        }
    }

    private Image InventoryIcon(OwnerType owner, InventoryItemType item)
    {
        bool boy = owner == OwnerType.Boy;

        switch (item)
        {
            case InventoryItemType.Newspaper:
                return boy ? boyNewspaperIcon : witchNewspaperIcon;

            case InventoryItemType.WaterBottle:
                return boy ? boyWaterBottleIcon : witchWaterBottleIcon;

            default:
                return null;
        }
    }
    #endregion

    #region Match HUD
    private void MatchOnTimeRemainingChanged(float secondsLeft)
    {
        if (matchTimerText == null)
            return;

        int total = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft));

        matchTimerText.text = $"<mspace=0.65em>{total / 60:0}:{total % 60:00}</mspace>";
        matchTimerText.color = secondsLeft <= lowTimeSeconds ? lowTimeColor : normalTimeColor;

        if (secondsLeft <= lowTimeSeconds && secondsLeft > 0f)
        {
            bool urgent = secondsLeft <= urgentTimeSeconds;

            PunchScale(matchTimerText.rectTransform, punchAmount * (urgent ? 1.6f : 0.7f));

            if (urgent)
                Shake(matchTimerText.rectTransform, shakeAmplitude);
        }
    }

    private void ZoneCountOnChanged(OwnerType owner, int _) => RefreshScoreLabel(owner);

    private void ScoreOnChanged(OwnerType owner, int newScore)
    {
        int previous = lastScores[(int)owner];
        lastScores[(int)owner] = newScore;

        RefreshScoreLabel(owner);

        TMP_Text label = owner == OwnerType.Boy ? boyScoreText : witchScoreText;

        if (label == null)
            return;

        if (newScore > previous)
        {
            PunchScale(label.rectTransform, punchAmount);
        }
        else if (newScore < previous)
        {
            Shake(label.rectTransform, shakeAmplitude);

            StartCoroutine(FlashRoutine(label, scoreLossColor,
                owner == OwnerType.Boy ? boyScoreBase : witchScoreBase));
        }
    }

    private IEnumerator FlashRoutine(TMP_Text label, Color flash, Color restore)
    {
        float t = 0f;
        label.color = flash;

        while (t < 0.3f)
        {
            t += Time.unscaledDeltaTime;
            label.color = Color.Lerp(flash, restore, t / 0.3f);
            yield return null;
        }

        label.color = restore;
    }

    public void RefreshScoreLabels()
    {
        foreach (OwnerType owner in TwoPlayerInputManager.Owners)
            RefreshScoreLabel(owner);
    }

    private void RefreshScoreLabel(OwnerType owner)
    {
        TMP_Text label = owner == OwnerType.Boy ? boyScoreText : witchScoreText;

        if (label == null)
            return;

        label.text = string.Format(scoreFormat,
            DeliveryScoreManager.GetScore(owner),
            DeliveryScoreManager.GetZoneCount(owner));
    }

    private void MatchOnMatchEnded(MatchManager.MatchResult result)
    {
        if (resultHeadlineText != null)
            resultHeadlineText.text = result.Headline();

        if (resultDetailText != null)
            resultDetailText.text = $"{result.Detail()}\nBoy {result.boyScore} - {result.witchScore} Witch";

        if (result.isDraw)
        {
            if (boyConfetti != null) boyConfetti.Play();
            if (witchConfetti != null) witchConfetti.Play();
        }
        else if (result.winner == OwnerType.Boy)
        {
            if (boyConfetti != null) boyConfetti.Play();
        }
        else if (witchConfetti != null)
        {
            witchConfetti.Play();
        }
    }

    public void RestartGame()
    {
        if (MatchManager.instance != null)
            MatchManager.instance.Restart();
    }
    #endregion

    #region Feedback
    private void PunchScale(RectTransform rt, float amount)
    {
        if (rt == null) return;

        if (scaleTweens.TryGetValue(rt, out Coroutine running) && running != null)
            StopCoroutine(running);

        scaleTweens[rt] = StartCoroutine(PunchRoutine(rt, amount));
    }

    private IEnumerator PunchRoutine(RectTransform rt, float amount)
    {
        float t = 0f;

        while (t < punchDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / punchDuration);
            rt.localScale = Vector3.one * (1f + punchCurve.Evaluate(k) * amount);
            yield return null;
        }

        rt.localScale = Vector3.one;
        scaleTweens[rt] = null;
    }

    private void Shake(RectTransform rt, float amplitude)
    {
        if (rt == null) return;

        if (!homePositions.ContainsKey(rt))
            homePositions[rt] = rt.anchoredPosition;

        if (shakeTweens.TryGetValue(rt, out Coroutine running) && running != null)
            StopCoroutine(running);

        shakeTweens[rt] = StartCoroutine(ShakeRoutine(rt, amplitude));
    }

    private IEnumerator ShakeRoutine(RectTransform rt, float amplitude)
    {
        Vector2 home = homePositions[rt];
        float t = 0f;

        while (t < shakeDuration)
        {
            t += Time.unscaledDeltaTime;
            float decay = 1f - Mathf.Clamp01(t / shakeDuration);
            rt.anchoredPosition = home + Random.insideUnitCircle * (amplitude * decay);
            yield return null;
        }

        rt.anchoredPosition = home;
        shakeTweens[rt] = null;
    }
    #endregion
}
