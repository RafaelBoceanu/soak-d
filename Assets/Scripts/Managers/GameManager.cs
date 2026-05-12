using System;
using UnityEngine;
using UnityEngine.Android;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;
    public static event Action<GameState> OnGameStateChanged;

    public GameState gameState;

    [SerializeField] private GameObject boyCharacter;
    [SerializeField] private GameObject witchCharacter;
    [SerializeField] private GameObject boyCamera;
    [SerializeField] private GameObject witchCamera;
    [SerializeField] private GameObject handcarPrefab;

    private GameState previousState;

    public GameState PreviousState => previousState;
    public GameObject HandcarPrefab => handcarPrefab;
    public GameObject BoyCamera => boyCamera;
    public GameObject WitchCamera => witchCamera;

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        UpdateGameState(GameState.Delivery);
    }
    void Update()
    {
        // Debug: Press 'T' to toggle game state
        /*if (Input.GetKeyDown(KeyCode.T))
        {
            if (gameState == GameState.Handcar)
                UpdateGameState(GameState.Delivery);
            else
                UpdateGameState(GameState.Handcar);
        }*/

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (gameState != GameState.Pause)
            {
                previousState = gameState; // Save current state
                handcarPrefab.GetComponent<HandcarMovement>().enabled = false; // Disable handcar movement
                boyCamera.SetActive(false); // Disable camera control
                witchCamera.SetActive(false);
                UpdateGameState(GameState.Pause); // Pause game
            }
            else
            {
                UpdateGameState(previousState); // Resume game
                if (previousState == GameState.Handcar)
                {
                    handcarPrefab.GetComponent<HandcarMovement>().enabled = true; // Re-enable handcar movement
                }
                else if (previousState == GameState.Delivery)
                {
                    boyCamera.SetActive(true); // Disable camera control
                    witchCamera.SetActive(true);
                }
            }
        }
    }

    public void UpdateGameState (GameState newState)
    {
        gameState = newState;

        switch (newState)
        {
            case GameState.Handcar:
                HandleHandcar();
                break;
            case GameState.Delivery:
                HandleDelivery();
                break;
        }

        OnGameStateChanged?.Invoke(newState);
    }

    private void HandleHandcar()
    {
        if (boyCharacter != null)
        {
            boyCharacter.SetActive(false);
            boyCamera.SetActive(false);
        }
        if (witchCharacter != null)
        {
            witchCharacter.SetActive(false);
            witchCamera.SetActive(false);
        }
        if (handcarPrefab != null)
            handcarPrefab.SetActive(true);
    }

    private void HandleDelivery()
    {
        if (boyCharacter != null)
        {
            boyCharacter.SetActive(true);
            boyCamera.SetActive(true);
        }
        if (witchCharacter != null)
        {
            witchCharacter.SetActive(true);
            witchCamera.SetActive(true);
        }
        if (handcarPrefab != null)
            handcarPrefab.SetActive(false);
    }
}
public enum GameState
{
    Handcar,
    Delivery,
    Pause
}