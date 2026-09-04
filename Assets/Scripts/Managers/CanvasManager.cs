using TMPro;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;

public class CanvasManager : MonoBehaviour
{
    [SerializeField] private GameObject deliveryPanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Slider boyHydrationSlider;
    [SerializeField] private Slider boyPeeSlider;
    [SerializeField] private Slider witchHydrationSlider;
    [SerializeField] private Slider witchPeeSlider;

    [Header("Inventory")]
    [SerializeField] private TMP_Text boyNewspaperCount;
    [SerializeField] private TMP_Text boyWaterBottleCount;
    [SerializeField] private TMP_Text witchNewspaperCount;
    [SerializeField] private TMP_Text witchWaterBottleCount;

    [Tooltip("{0} is the amount held, {1} the maximum. \"{0}\" shows 3, \"{0}/{1}\" shows 3/6.")]
    [SerializeField] private string inventoryCountFormat = "{0}/{1}";

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        GameManager.OnGameStateChanged += GameManagerOnGameStateChanged;
        PlayerInventory.OnAnyItemCountChanged += InventoryOnItemCountChanged;
    }

    void OnDestroy()
    {
        GameManager.OnGameStateChanged -= GameManagerOnGameStateChanged;
        PlayerInventory.OnAnyItemCountChanged -= InventoryOnItemCountChanged;
    }

    void Start()
    {
        RefreshInventoryLabels();
    }

    private void GameManagerOnGameStateChanged(GameState state)
    {
        deliveryPanel.SetActive(state == GameState.Delivery);

        if (state == GameState.Pause)
        {
            // Pause the game
            pausePanel.SetActive(true);
            Time.timeScale = 0f;
        }
        else
        {
            // Resume the game
            pausePanel.SetActive(false);
            Time.timeScale = 1f;
        }
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
    public void SetBoyPee(float value) => boyPeeSlider.value = value;
    public void SetWitchHydration(float value) => witchHydrationSlider.value = value;
    public void SetWitchPee(float value) => witchPeeSlider.value = value;

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
    }

    private void SetInventoryLabel(OwnerType owner, InventoryItemType item, int count, int capacity)
    {
        TMP_Text label = InventoryLabel(owner, item);

        if (label == null)
            return;

        label.text = string.Format(inventoryCountFormat, count, capacity);
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
    #endregion
}
