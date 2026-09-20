using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class InventoryRefillZone : MonoBehaviour
{
    public enum RefillMode
    {
        [Tooltip("The bag is topped up in one go")]
        Instant,

        [Tooltip("Newspapers are handed over one by one while player stands in the zone")]
        OverTime
    }

    [Header("Contents")]
    [Tooltip("What the zone hands out. A newspaper stand leaves this on newspaper")]
    [SerializeField] private InventoryItemType item = InventoryItemType.Newspaper;

    [SerializeField] private RefillMode mode = RefillMode.Instant;

    [Tooltip("Papers handed over per refill in Instant mode")]
    [SerializeField, Min(0)] private int amountPerRefill = 0;

    [Tooltip("Papers per second in OveTime mode")]
    [SerializeField, Min(0.1f)] private float refillRate = 6f;

    [Tooltip("Seconds before the same player may be served again in Instant mode")]
    [SerializeField, Min(0f)] private float refillCooldown = 2f;

    [Header("Who may refill")]
    [Tooltip("Ticked to keep the zone for one player. Left clear if either of them may use it")]
    [SerializeField] private bool restrictToOwner = false;
    [SerializeField] private OwnerType owner = OwnerType.Boy;

    [Tooltip("On, walking into the zone is enough. Off, the player hass to press Interact")]
    [SerializeField] private bool refillOnApproach = true;

    [Header("Reach")]
    [Tooltip("How close a player has to get, measured flat on the ground from the middle of " +
             "the zone")]
    [SerializeField, Min(0f)] private float refillRadius = 3f;

    [Tooltip("How far above or below the zone the player may be, so a broom flying over the " +
             "roof is not served")]
    [SerializeField, Min(0f)] private float verticalReach = 3f;

    [Tooltip("Seconds between reach checks")]
    [SerializeField, Min(0f)] private float checkInterval = 0.05f;

    [Header("Stock")]
    [Tooltip("On, the stand never runs out of newspapers")]
    [SerializeField] private bool unlimitedStock = true;

    [Tooltip("Amount of newspaper the stand holds. Ignored while unlimited stock is on")]
    [SerializeField, Min(0)] private int stock = 60;

    [Tooltip("Seconds the stand stays shut once it is emptied")]
    [SerializeField, Min(0f)] private float restockDelay = 20f;

    [Tooltip("Papers the stand comes back with")]
    [SerializeField, Min(1)] private int restockAmount = 60;

    [Header("Presentation")]
    [Tooltip("Shown while someone who still has room is standing in the zone. A world space " +
             "\"Press Interact\" sign, and arrow, a glow")]
    [SerializeField] private GameObject prompt;

    [Tooltip("Shown while the stand has papers left")]
    [SerializeField] private GameObject openVisuals;

    [Tooltip("Shown while the stand is empty and waiting on its restock")]
    [SerializeField] private GameObject closedVisuals;

    [SerializeField] private AudioSource refillSound;
    [SerializeField] private ParticleSystem refillEffect;

    [Header("Debugging")]
    [Tooltip("Log every handful of paper handed over")]
    [SerializeField] private bool logRefills = false;

    struct Customer
    {
        public bool inReach;
        public bool serving;
        public bool askedForService;
        public float progress;
        public float nextRefillTime;
    }

    static readonly OwnerType[] owners = (OwnerType[])Enum.GetValues(typeof(OwnerType));

    Customer[] customers;
    float nextCheckTime;
    float lastCheckTime;
    bool warnedAboutCapacity;

    public InventoryItemType Item => item;

    public int Stock => unlimitedStock ? int.MaxValue : stock;

    public bool HasStock => unlimitedStock || stock > 0;

    public event Action<OwnerType, int> OnRefilled;

    public static event Action<OwnerType, InventoryItemType, int> OnAnyRefilled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        OnAnyRefilled = null;
    }

    void Awake()
    {
        customers = new Customer[owners.Length];

        if (!unlimitedStock && stock <= 0)
            stock = restockAmount;

        SetOpen(HasStock);
        ShowPrompt(false);
    }

    void OnEnable()
    {
        lastCheckTime = Time.time;
        nextCheckTime = 0f;
    }

    void OnDisable()
    {
        for (int i = 0; i < customers.Length; i++)
            customers[i] = default;

        ShowPrompt(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (!refillOnApproach)
            LatchInteractPresses();

        if (Time.time < nextCheckTime) return;

        float elapsed = Mathf.Min(Time.time - lastCheckTime, 0.5f);

        lastCheckTime = Time.time;
        nextCheckTime = Time.time + checkInterval;

        Serve(elapsed);
    }

    void LatchInteractPresses()
    {
        foreach (OwnerType candidate in owners)
        {
            PlayerInputContext input = TwoPlayerInputManager.GetPlayer(candidate);

            if (input != null && input.InteractPressed)
                customers[(int)candidate].askedForService = true;
        }
    }

    #region Serving
    void Serve(float elapsed)
    {
        bool anyoneWaiting = false;

        foreach (OwnerType candidate in owners)
        {
            int index = (int)candidate;

            PlayerInventory inventory = PlayerInventory.ForOwner(candidate);

            bool welcome =
                inventory != null &&
                (!restrictToOwner || candidate == owner) &&
                IsInReach(inventory.transform.position);

            if (!welcome)
            {
                customers[index] = default;
                continue;
            }

            customers[index].inReach = true;

            if (inventory.IsFull(item))
            {
                customers[index].serving = false;
                customers[index].progress = 0f;
                continue;
            }

            if (HasStock)
                anyoneWaiting = true;
            else
                continue;

            if (!customers[index].serving)
            {
                if (refillOnApproach)
                {
                    customers[index].serving = true;
                }
                else
                {
                    if (!customers[index].askedForService) continue;

                    customers[index].askedForService = false;
                    customers[index].serving = true;
                }
            }

            if (Time.time < customers[index].nextRefillTime) continue;

            if (mode == RefillMode.Instant)
                ServeInstant(candidate, inventory);
            else
                ServeOverTime(candidate, inventory, elapsed);
        }

        ShowPrompt(anyoneWaiting);
    }

    void ServeInstant(OwnerType candidate, PlayerInventory inventory)
    {
        int wanted = amountPerRefill > 0 ? amountPerRefill : MissingFor(inventory);

        if (wanted <= 0) return;

        int index = (int)candidate;

        if (HandOver(candidate, inventory, wanted) <= 0) return;

        customers[index].nextRefillTime = Time.time + refillCooldown;

        if (!refillOnApproach)
            customers[index].serving = false;
    }

    void ServeOverTime(OwnerType candidate, PlayerInventory inventory, float elapsed)
    {
        int index = (int)candidate;

        customers[index].progress += refillRate * elapsed;

        int whole = Mathf.FloorToInt(customers[index].progress);

        if (whole <= 0) return;

        customers[index].progress -= whole;

        if (HandOver(candidate, inventory, whole) <= 0)
            customers[index].progress = 0f;
    }

    int HandOver(OwnerType candidate, PlayerInventory inventory, int wanted)
    {
        if (wanted <= 0 || !HasStock) return 0;

        if (!unlimitedStock)
            wanted = Mathf.Min(wanted, stock);

        if (wanted <= 0) return 0;

        int handed = inventory.Add(item, wanted);

        if (handed <= 0) return 0;

        if (!unlimitedStock)
        {
            stock -= handed;

            if (stock <= 0)
                CloseForRestock();
        }

        PlayFeedback();

        if (logRefills)
        {
            Debug.Log(
                $"[InventoryRefillZone] {name} gave {candidate} {handed} {item} " +
                $"({inventory.GetCount(item)}/{inventory.GetCapacity(item)} held" +
                $"{(unlimitedStock ? string.Empty : $", {stock} left on the stand")}).", this);
        }

        OnRefilled?.Invoke(candidate, handed);
        OnAnyRefilled?.Invoke(candidate, item, handed);

        return handed;
    }

    int MissingFor(PlayerInventory inventory)
    {
        int capacity = inventory.GetCapacity(item);

        if (capacity <= 0)
        {
            if (!warnedAboutCapacity)
            {
                warnedAboutCapacity = true;

                Debug.LogWarning(
                    $"[InventoryRefillZone] {name} fills the bag to its maximum, but {inventory.Owner} " +
                    $"carries an unlimited amount of {item}. Set Max Amount on the player's " +
                    $"PlayerInventory, or give this zone an Amount Per Refill");
            }

            return 0;
        }

        return Mathf.Max(0, capacity - inventory.GetCount(item));
    }

    public bool TryRefill(PlayerInventory inventory)
    {
        if (inventory == null || !HasStock) return false;

        if (restrictToOwner && inventory.Owner != owner) return false;

        int wanted = amountPerRefill > 0 ? amountPerRefill : MissingFor(inventory);

        return HandOver(inventory.Owner, inventory, wanted) > 0;
    }
    #endregion

    #region Reach 
    bool IsInReach(Vector3 playerPosition)
    {
        Vector3 gap = playerPosition - transform.position;

        if (Mathf.Abs(gap.y) > verticalReach) return false;

        gap.y = 0f;

        return gap.sqrMagnitude <= refillRadius * refillRadius;
    }
    #endregion

    #region Stock
    void CloseForRestock()
    {
        stock = 0;

        SetOpen(false);
        ShowPrompt(false);

        for (int i = 0; i < customers.Length; i++)
        {
            customers[i].serving = false;
            customers[i].progress = 0f;
        }

        if (restockDelay <= 0f || !isActiveAndEnabled)
        {
            stock = restockAmount;
            SetOpen(true);
            return;
        }

        StartCoroutine(Restock());
    }

    IEnumerator Restock()
    {
        yield return new WaitForSeconds(restockDelay);

        stock = Mathf.Max(1, restockAmount);

        SetOpen(true);

        if (logRefills)
            Debug.Log($"[InventoryRefillZone] {name} restocked with {stock} {item}.", this);
    }

    void SetOpen(bool open)
    {
        if (openVisuals != null)
            openVisuals.SetActive(open);

        if (closedVisuals != null)
            closedVisuals.SetActive(!open);
    }

    void ShowPrompt(bool show)
    {
        if (prompt != null && prompt.activeSelf != show)
            prompt.SetActive(show);
    }
    #endregion

    void PlayFeedback()
    {
        if (refillSound != null && (mode == RefillMode.Instant || !refillSound.isPlaying))
            refillSound.Play();

        if (refillEffect != null)
            refillEffect.Play();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 1f);
        Gizmos.DrawWireSphere(transform.position, refillRadius);

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
        Gizmos.DrawLine(transform.position + Vector3.up * verticalReach,
                        transform.position + Vector3.down * verticalReach);
    }
}
