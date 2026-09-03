using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
public class TwoPlayerInputManager : MonoBehaviour
{
    public const string KeyboardMouseScheme = "Keyboard&Mouse";
    public const string GamepadScheme = "Gamepad";
    public const string KeyboardAltScheme = "KeyboardAlt";

    private const string DefaultControlsResource = "Input/PlayerControls";

    [Header("Controls")]
    [Tooltip("Leave empty to load Resources/Input/PlayerControls.inputactions.")]
    [SerializeField] private InputActionAsset controls;

    [Header("Device assignment")]
    [Tooltip("Character driven by keyboard & mouse. The other character is driven by the gamepad.")]
    [SerializeField] private OwnerType keyboardMousePlayer = OwnerType.Boy;

    [Tooltip("With no gamepad connected, give the second player the keyboard fallback scheme " +
             "(arrow keys + numpad) so the game stays playable on a single keyboard.")]
    [SerializeField] private bool keyboardFallbackWhenNoGamepad = true;

    [Tooltip("Log every device pairing change.")]
    [SerializeField] private bool logDeviceChanges = false;

    private static TwoPlayerInputManager instance;
    private static bool quitting;

    private readonly Dictionary<OwnerType, PlayerInputContext> contexts =
        new Dictionary<OwnerType, PlayerInputContext>();

    public OwnerType KeyboardMousePlayer => keyboardMousePlayer;

    public OwnerType GamepadPlayer => Other(keyboardMousePlayer);

    #region Lifetime
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        quitting = false;
    }

    private void Awake()
    {
       if (instance != null && instance != this)
        {
            Debug.LogWarning($"[TwoPlayeInputManager] A second manager on '{name}' was destroyed.", this);
            Destroy(this);
            return;
        }

        instance = this;
        BuildContexts();
        AssignDevices();
    }

    private void OnEnable()
    {
        InputSystem.onDeviceChange += HandleDeviceChange;
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= HandleDeviceChange;
    }

    private void OnDestroy()
    {
        foreach (PlayerInputContext context in contexts.Values)
            context.Dispose();

        contexts.Clear();

        if (instance == this)
            instance = null;
    }

    private void OnApplicationQuit()
    {
        quitting = true;
    }
    #endregion

    #region Access
    public static PlayerInputContext GetPlayer(OwnerType owner)
    {
        TwoPlayerInputManager manager = EnsureInstance();

        if (manager == null)
            return null;

        return manager.contexts.TryGetValue(owner, out PlayerInputContext context) ? context : null;
    }

    public static bool AnyPausePressed()
    {
        foreach (OwnerType owner in Owners)
        {
            PlayerInputContext context = GetPlayer(owner);

            if (context != null && context.PausePressed)
                return true;
        }

        return false;
    }

    public static bool AnyCycleViewPressed()
    {
        foreach (OwnerType owner in Owners)
        {
            PlayerInputContext context = GetPlayer(owner);

            if (context != null && context.CycleViewPressed)
                return true;
        }

        return false;
    }

    public static OwnerType[] Owners { get; } = (OwnerType[])Enum.GetValues(typeof(OwnerType));

    private static TwoPlayerInputManager EnsureInstance()
    {
        if (instance != null || quitting || !Application.isPlaying)
            return instance;

        instance = FindFirstObjectByType<TwoPlayerInputManager>(FindObjectsInactive.Include);

        if (instance == null)
        {
            var host = new GameObject(nameof(TwoPlayerInputManager));
            instance = host.AddComponent<TwoPlayerInputManager>();
        }

        return instance;
    }

    private static OwnerType Other(OwnerType owner) =>
        owner == OwnerType.Boy ? OwnerType.Witch : OwnerType.Boy;
    #endregion

    #region Setup
    private void BuildContexts()
    {
        InputActionAsset asset = controls != null
            ? controls
            : Resources.Load<InputActionAsset>(DefaultControlsResource);

        if (asset == null)
        {
            Debug.LogError($"[TwoPlayerInputManager] No control asset assigned and none found at " +
                           $"Resources/{DefaultControlsResource}.", this);
            enabled = false;
            return;
        }

        foreach (OwnerType owner in Owners)
        {
            contexts[owner] = new PlayerInputContext(owner, Instantiate(asset));
        }
    }

    public void AssignDevices()
    {
        if (contexts.Count == 0)
            return;

        var keyboardAndMouse = new List<InputDevice>(2);

        if (Keyboard.current != null)
            keyboardAndMouse.Add(Keyboard.current);

        if (Mouse.current != null)
            keyboardAndMouse.Add(Mouse.current);

        PlayerInputContext first = contexts[keyboardMousePlayer];
        PlayerInputContext second = contexts[GamepadPlayer];

        if (keyboardAndMouse.Count > 0)
            first.Bind(KeyboardMouseScheme, keyboardAndMouse.ToArray());
        else
            first.Unbind();

        Gamepad gamepad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null;

        if (gamepad != null)
            second.Bind(GamepadScheme, gamepad);
        else if (keyboardFallbackWhenNoGamepad && Keyboard.current != null)
            second.Bind(KeyboardAltScheme, Keyboard.current);
        else
            second.Unbind();

        if (logDeviceChanges)
        {
            Debug.Log($"[TwoPlayerInputManager] {first.Owner} -> {first.ControlScheme ?? "none"}, " +
                      $"{second.Owner} -> {second.ControlScheme ?? "none"}", this);
        }
    }

    private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
    {
        switch(change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Removed:
            case InputDeviceChange.Reconnected:
            case InputDeviceChange.Disconnected:
                AssignDevices();
                break;
        }
    }
    #endregion
}
