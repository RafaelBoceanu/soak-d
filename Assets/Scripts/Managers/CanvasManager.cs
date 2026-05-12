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

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        GameManager.OnGameStateChanged += GameManagerOnGameStateChanged;
    }

    void OnDestroy()
    {
        GameManager.OnGameStateChanged -= GameManagerOnGameStateChanged;
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

}
