using UnityEngine;

public class WaterBottleDrinking : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Which player owns this character. Left at the default it is taken from the PlayerInputHandler on this object.")]
    [SerializeField] private OwnerType owner = OwnerType.Boy;

    [Header("Drinking")]
    [Tooltip("Hydration restored by one water bottle.")]
    [SerializeField] private float hydrationPerBottle = 25f;

    [Tooltip("Seconds before another bottle can be opened.")]
    [SerializeField] private float drinkCooldown = 0.5f;

    [Tooltip("Refuse to drink at full hydration so a bottle is never wasted.")]
    [SerializeField] private bool blockWhenFull = true;

    [Tooltip("Hydration within this much of the maximum counts as full")]
    [SerializeField, Min(0f)] private float fullTolerance = 1f;

    [Header("Reference")]
    [SerializeField] private PlayerNeeds playerNeeds;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private AudioSource drinkSound;
    [SerializeField] private ParticleSystem drinkEffect;

    float nextDrinkTime;

    private PlayerInputContext Controls => TwoPlayerInputManager.GetPlayer(owner);

    public int BottlesLeft => inventory != null ? inventory.GetCount(InventoryItemType.WaterBottle) : 0;

    void Awake()
    {
        PlayerInputHandler inputHandler = GetComponent<PlayerInputHandler>();

        if (inputHandler != null)
            owner = inputHandler.Owner;

        if (playerNeeds == null)
            playerNeeds = GetComponent<PlayerNeeds>();

        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (inventory == null)
            inventory = PlayerInventory.ForOwner(owner);

        if (playerNeeds == null)
            Debug.LogError($"[WaterBottleDrinking] {name} has no PlayerNeeds - drinking is disabled.", this);

        if (inventory == null)
            Debug.LogError($"[WaterBottleDrinking] {name} has no PlayerInventory - drinking is disabled.", this);
    }

    // Update is called once per frame
    void Update()
    {
        PlayerInputContext input = Controls;

        if (input == null || !input.DrinkPressed)
            return;

        TryDrink();
    }

    public bool TryDrink()
    {
        if (playerNeeds == null || inventory == null)
            return false;

        if (Time.time < nextDrinkTime)
            return false;

        if (!inventory.Has(InventoryItemType.WaterBottle))
            return false;

        if (blockWhenFull && playerNeeds.hydration >= playerNeeds.maxHydration - fullTolerance)
            return false;

        if (!inventory.TryConsume(InventoryItemType.WaterBottle))
            return false;

        playerNeeds.Drink(hydrationPerBottle);
        nextDrinkTime = Time.time + drinkCooldown;

        if (drinkSound != null)
            drinkSound.Play();

        if (drinkEffect != null)
            drinkEffect.Play();

        return true;
    }
}
