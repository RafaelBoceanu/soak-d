using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Dynamic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public struct ItemSlot
    {
        public InventoryItemType item;

        [Tooltip("Amount the player spwans with.")]
        [Min(0)] public int startingAmount;

        [Tooltip("Upper limit for this item. 0 means unlimited.")]
        [Min(0)] public int maxAmount;
    }

    static readonly int itemTypeCount = Enum.GetValues(typeof(InventoryItemType)).Length;

    static readonly Dictionary<OwnerType, PlayerInventory> inventories =
        new Dictionary<OwnerType, PlayerInventory>();

    [Header("Player")]
    [Tooltip("Which player owns this inventory. Left at the default it is taken from the PlayerInputHandler on this object.")]
    [SerializeField] private OwnerType owner = OwnerType.Boy;

    [Header("Items")]
    [SerializeField]
    private List<ItemSlot> slots = new List<ItemSlot>
    {
        new ItemSlot { item = InventoryItemType.Newspaper,   startingAmount = 10, maxAmount = 20 },
        new ItemSlot { item = InventoryItemType.WaterBottle, startingAmount = 3,  maxAmount = 6 }
    };

    [Tooltip("Log every add and every consume.")]
    [SerializeField] private bool logChanges = false;

    int[] amounts;
    int[] capacities;

    public OwnerType Owner => owner;

    public event Action<InventoryItemType, int> OnItemCountChanged;

    public static event Action<OwnerType, InventoryItemType, int> OnAnyItemCountChanged;

    public static PlayerInventory ForOwner(OwnerType owner) =>
        inventories.TryGetValue(owner, out PlayerInventory inventory) ? inventory : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        inventories.Clear();
        OnAnyItemCountChanged = null;
    }

    void Awake()
    {
        PlayerInputHandler inputHandler = GetComponent<PlayerInputHandler>();

        if (inputHandler != null)
            owner = inputHandler.Owner;

        BuildSlots();

        inventories[owner] = this;
    }

    void OnDestroy()
    {
        if (ForOwner(owner) == this)
            inventories.Remove(owner);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        for (int i = 0; i < itemTypeCount; i++)
            Notify((InventoryItemType)i);
    }

    void BuildSlots()
    {
        amounts = new int[itemTypeCount];
        capacities = new int[itemTypeCount];

        foreach (ItemSlot slot in slots)
        {
            int index = (int)slot.item;

            if (index < 0 || index >= itemTypeCount)
                continue;

            capacities[index] = Mathf.Max(0, slot.maxAmount);
            amounts[index] = Clamp(index, slot.startingAmount);
        }
    }

    int Clamp(int index, int value)
    {
        value = Mathf.Max(0, value);

        int capacity = capacities[index];

        return capacity > 0 ? Mathf.Min(value, capacity) : value;
    }

    #region Queries
    public int GetCount(InventoryItemType item) => amounts[(int)item];

    public int GetCapacity(InventoryItemType item) => capacities[(int)item];

    public bool Has(InventoryItemType item, int amount = 1) =>
        amount > 0 && amounts[(int)item] >= amount;

    public bool IsFull(InventoryItemType item)
    {
        int index = (int)item;
        return capacities[index] > 0 && amounts[index] >= capacities[index];
    }
    #endregion

    #region Mutations
    public int Add(InventoryItemType item, int amount = 1)
    {
        if (amount <= 0) return 0;

        int index = (int)item;
        int before = amounts[index];

        amounts[index] = Clamp(index, before + amount);

        int added = amounts[index] - before;

        if (added != 0)
        {
            if (logChanges)
                Debug.Log($"[PlayerInventory] {owner} picked up {added} {item}  ({amounts[index]} held.", this);

            Notify(item);
        }

        return added;
    }

    public bool TryConsume(InventoryItemType item, int amount = 1)
    {
        if (amount <= 0) return false;

        int index = (int)item;

        if (amounts[index] < amount)
            return false;

        amounts[index] -= amount;

        if (logChanges)
            Debug.Log($"[PlayerInventory] {owner} used {amount} {item} ({amounts[index]} left.", this);

        Notify(item);
        return true;
    }

    public void SetCount(InventoryItemType item, int amount)
    {
        int index = (int)item;
        int clamped = Clamp(index, amount);

        if (clamped == amounts[index]) return;

        amounts[index] = clamped;
        Notify(item);
    }
    #endregion

    void Notify(InventoryItemType item)
    {
        int count = amounts[(int)item];

        OnItemCountChanged?.Invoke(item, count);
        OnAnyItemCountChanged?.Invoke(owner, item, count);
    }
}
