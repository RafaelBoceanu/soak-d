using System;
using System.Collections;
using UnityEngine;

public class InventoryPickup : MonoBehaviour
{
    [Header("Contents")]
    [SerializeField] private InventoryItemType item = InventoryItemType.WaterBottle;
    [SerializeField, Min(1)] private int amount = 1;

    [Header("Who may take it")]
    [Tooltip("Ticked to reserve the pickup for one player. Left clear if either of them may take it")]
    [SerializeField] private bool restrictToOwner = false;
    [SerializeField] private OwnerType owner = OwnerType.Boy;

    [Header("Reach")]
    [Tooltip("How close a player has to get, measured flat on the grond from the middle of " +
             "the pickup. This is what actually collects the item. It needs no collider so it " +
             "keeps working on a bike or a broom, where the rider's own collider is switched off")]
    [SerializeField, Min(0f)] private float pickupRadius = 1.5f;

    [Tooltip("How far above or below the pickup the player may be. Stops a broom flying over " +
             "the street from hoovering it up")]
    [SerializeField, Min(0f)] private float verticalReach = 2.5f;

    [Tooltip("Seconds between reach checks")]
    [SerializeField, Min(0f)] private float checkInterval = 0.05f;

    [Header("Picking up")]
    [Tooltip("Off, the pickup hides and comes back after the respawn delay.")]
    [SerializeField] private bool destroyOnPickup = true;
    [SerializeField, Min(0f)] private float respawnDelay = 10f;

    [SerializeField, Min(0f)] private float fullBagRetryDelay = 0.5f;

    [Header("Presentation")]
    [Tooltip("Model to hide while the pickup is on cooldown.")]
    [SerializeField] private GameObject visuals;
    [SerializeField] private AudioSource pickupSound;
    [SerializeField] private ParticleSystem pickupEffect;

    [Header("Idle Motion")]
    [SerializeField] private float spinSpeed = 60f;
    [SerializeField, Min(0f)] private float bobHeight = 0.12f;
    [SerializeField, Min(0f)] private float bobSpeed = 2f;

    [Header("Debugging")]
    [Tooltip("Log how far the nearest player is once a second. Turn it on for one pickup when " +
             "nothing is being collected")]
    [SerializeField] private bool logReach = false;

    static readonly OwnerType[] owners = (OwnerType[])Enum.GetValues(typeof(OwnerType));

    Collider trigger;
    Transform motionRoot;
    Vector3 motionOrigin;
    float bobPhase;
    float nextCheckTime;
    float nextLogTime;
    bool collected;

    public InventoryItemType Item => item;

    public int Amount => amount;

    public bool DestroysOnPickup => destroyOnPickup;

    public bool Collected => collected;

    public event Action<InventoryPickup, OwnerType> OnPickedUp;

    public static event Action<OwnerType, InventoryItemType, int> OnAnyPickedUp;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        OnAnyPickedUp = null;
    }

    void Reset()
    {
        Collider col = GetComponent<Collider>();

        if (col != null)
            col.isTrigger = true;
    }

    private void Awake()
    {
        trigger = GetComponent<Collider>();
        
        if (trigger != null)
            trigger.isTrigger = true;

        motionRoot = visuals != null ? visuals.transform : transform;
        motionOrigin = motionRoot.localPosition;

        bobPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        if (!destroyOnPickup && visuals == null)
        {
            Debug.LogWarning(
                $"[InventoryPickup] {name} respawns in place but has no visuals assigned, so " +
                $"it stays on screen while it is on cooldown.", this);
        }
    }

    void Update()
    {
        Animate();

        if (collected) return;

        if (Time.time < nextCheckTime) return;

        nextCheckTime = Time.time + checkInterval;

        LookForPlayer();
    }

    void Animate()
    {
        if (collected || motionRoot == null) return;

        if (spinSpeed != 0f)
            motionRoot.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);

        if (bobHeight > 0f && bobSpeed > 0f)
        {
            float bob = Mathf.Sin(Time.time * bobSpeed + bobPhase) * bobHeight;

            motionRoot.localPosition = motionOrigin + Vector3.up * bob;
        }
    }

    #region Collecting
    void LookForPlayer()
    {
        bool log = logReach && Time.time >= nextLogTime;

        if (log)
            nextLogTime = Time.time + 1f;

        foreach (OwnerType candidate in owners)
        {
            PlayerInventory inventory = PlayerInventory.ForOwner(candidate);

            if (inventory == null)
            {
                if (log)
                    Debug.Log($"[InventoryPickup] {name}: no inventory registered for {candidate}", this);

                continue;
            }

            if (log)
            {
                Vector3 gap = inventory.transform.position - transform.position;

                Debug.Log(
                    $"[InventoryPickup] {name}: {candidate} is {new Vector2(gap.x, gap.z).magnitude:0.00}m" +
                    $"away, {gap.y:0.00}m up. Reach is {pickupRadius}m / {verticalReach}m. " +
                    $"Carrying {inventory.GetCount(item)}/{inventory.GetCapacity(item)} {item}.", this);
            }

            if (!IsInReach(inventory.transform.position)) continue;

            if (TryPickUp(inventory)) return;
        }
    }

    bool IsInReach(Vector3 playerPosition)
    {
        Vector3 gap = playerPosition - transform.position;

        if (Mathf.Abs(gap.y) > verticalReach) return false;

        gap.y = 0f;

        return gap.sqrMagnitude <= pickupRadius * pickupRadius;
    }

    void OnTriggerEnter(Collider other)
    {
        TryPickUp(other);
    }

    public bool TryPickUp(Collider other)
    {
        if (other == null) return false;

        return TryPickUp(other.GetComponentInParent<PlayerInventory>());
    }

    public bool TryPickUp(PlayerInventory inventory)
    {
        if (collected || inventory == null) return false;

        if (restrictToOwner && inventory.Owner != owner) return false;

        if (inventory.IsFull(item))
        {
            nextCheckTime = Time.time + fullBagRetryDelay;
            return false;
        }

        int added = inventory.Add(item, amount);

        if (added <= 0)
        {
            nextCheckTime = Time.time + fullBagRetryDelay;
            return false;
        }

        PickUp(inventory.Owner, added);
        return true;
    }

    void PickUp(OwnerType collector, int added)
    {
        collected = true;

        SetPickupActive(false);

        OnPickedUp?.Invoke(this, collector);
        OnAnyPickedUp?.Invoke(collector, item, added);

        if (destroyOnPickup)
        {
            PlayDetachedFeedback();

            Destroy(gameObject);
            return;
        }

        if (pickupSound != null)
            pickupSound.Play();

        if (pickupEffect != null)
            pickupEffect.Play();

        StartCoroutine(Respawn());
    }

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnDelay);

        collected = false;
        nextCheckTime = 0f;

        SetPickupActive(true);
    }

    void SetPickupActive(bool active)
    {
        if (trigger != null)
            trigger.enabled = active;

        if (visuals != null)
            visuals.SetActive(active);
    }

    void PlayDetachedFeedback()
    {
        if (pickupSound != null && pickupSound.clip != null)
            AudioSource.PlayClipAtPoint(pickupSound.clip, transform.position, pickupSound.volume);

        if (pickupEffect != null)
        {
            ParticleSystem.MainModule main = pickupEffect.main;

            pickupEffect.transform.SetParent(null, true);
            pickupEffect.Play();

            Destroy(pickupEffect.gameObject, main.duration + main.startLifetime.constantMax);
        }    
    }
    #endregion

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
