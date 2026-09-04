using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputContext
{
    public const string PlayerMap = "Player";

    private readonly InputActionAsset actions;

    private readonly InputAction move;
    private readonly InputAction look;
    private readonly InputAction sprint;
    private readonly InputAction interact;
    private readonly InputAction aim;
    private readonly InputAction throwAction;
    private readonly InputAction zip;
    private readonly InputAction pee;
    private readonly InputAction drink;
    private readonly InputAction brake;
    private readonly InputAction throttle;
    private readonly InputAction yaw;
    private readonly InputAction pumpLeft;
    private readonly InputAction pumpRight;
    private readonly InputAction cycleView;
    private readonly InputAction pause;

    public OwnerType Owner { get; }

    public string ControlScheme { get; private set; }

    public bool IsPaired { get; private set; }

    public bool UsesGamepad => ControlScheme == TwoPlayerInputManager.GamepadScheme;

    public bool LookIsDelta => ControlScheme == TwoPlayerInputManager.KeyboardMouseScheme;

    public PlayerInputContext(OwnerType owner, InputActionAsset actionsAsset)
    {
        Owner = owner;
        actions = actionsAsset;
        actions.name = $"{actionsAsset.name} ({owner})";

        InputActionMap map = actions.FindActionMap(PlayerMap, throwIfNotFound: false);
        if (map == null)
        {
            Debug.LogError($"[PlayerInputContext] Action map '{PlayerMap}' is missing from {actions.name}.");
            return;
        }

        move = Find(map, "Move");
        look = Find(map, "Look");
        sprint = Find(map, "Sprint");
        interact = Find(map, "Interact");
        aim = Find(map, "Aim");
        throwAction = Find(map, "Throw");
        zip = Find(map, "Zip");
        pee = Find(map, "Pee");
        drink = Find(map, "Drink");
        brake = Find(map, "Brake");
        throttle = Find(map, "Throttle");
        yaw = Find(map, "Yaw");
        pumpLeft = Find(map, "PumpLeft");
        pumpRight = Find(map, "PumpRight");
        cycleView = Find(map, "CycleView");
        pause = Find(map, "Pause");
    }

    private static InputAction Find(InputActionMap map, string actionName)
    {
        InputAction action = map.FindAction(actionName, throwIfNotFound: false);

        if (action == null)
            Debug.LogWarning($"[PlayerInputContext] Action '{actionName}' is missing from map '{map.name}'.");

        return action;
    }

    #region Device binding

    public void Bind(string controlScheme, params InputDevice[] devices)
    {
        actions.Disable();

        ControlScheme = controlScheme;
        actions.bindingMask = InputBinding.MaskByGroup(controlScheme);
        actions.devices = devices;

        actions.Enable();
        IsPaired = devices != null && devices.Length > 0;
    }

    public void Unbind()
    {
        actions.Disable();

        ControlScheme = null;
        actions.devices = System.Array.Empty<InputDevice>();
        IsPaired = false;
    }

    public void Dispose()
    {
        IsPaired = false;
        actions.Disable();
        Object.Destroy(actions);
    }
    #endregion

    #region Reading
    public Vector2 Move => Axis2(move);
    public Vector2 Look => Axis2(look);
    public float Throttle => Axis(throttle);
    public float Yaw => Axis(yaw);

    public bool Sprint => Held(sprint);
    public bool InteractPressed => Pressed(interact);
    public bool Aim => Held(aim);
    public bool ThrowPressed => Pressed(throwAction);
    public bool ThrowHeld => Held(throwAction);
    public bool ThrowReleased => Released(throwAction);
    public bool ZipPressed => Pressed(zip);
    public bool Pee => Held(pee);
    public bool DrinkPressed => Pressed(drink);
    public bool Brake => Held(brake);
    public bool PumpLeftPressed => Pressed(pumpLeft);
    public bool PumpRightPressed => Pressed(pumpRight);
    public bool CycleViewPressed => Pressed(cycleView);
    public bool PausePressed => Pressed(pause);

    private Vector2 Axis2(InputAction action) =>
        IsPaired && action != null ? action.ReadValue<Vector2>() : Vector2.zero;

    private float Axis(InputAction action) =>
        IsPaired && action != null ? action.ReadValue<float>() : 0f;

    private bool Held(InputAction action) =>
        IsPaired && action != null && action.IsPressed();

    private bool Pressed(InputAction action) =>
        IsPaired && action != null && action.WasPressedThisFrame();

    private bool Released(InputAction action) =>
        IsPaired && action != null && action.WasReleasedThisFrame();
    #endregion
}
