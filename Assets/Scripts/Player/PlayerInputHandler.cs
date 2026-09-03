using System.Collections.Generic;
using UnityEngine;

public class PlayerInputHandler : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Identifies this character. The input manager pairs one owner with keyboard and mouse and the other with the gamepad.")]
    [SerializeField] private OwnerType owner = OwnerType.Boy;

    [Header("References")]
    [Tooltip("Camera rig of this player. Left empty, the rig registered for the same owner is used.")]
    [SerializeField] private PlayersCameraController cameraController;

    private PlayerMovement playerMovement;

    private BicycleController bike;
    private FlyingBroomController broom;

    private MountableVehicle currentVehicle;
    
    private List<MountableVehicle> nearbyVehicles = new List<MountableVehicle>();

    public OwnerType Owner => owner;

    public PlayerInputContext Controls => TwoPlayerInputManager.GetPlayer(owner);

    public PlayersCameraController CameraController =>
        cameraController != null ? cameraController : PlayersCameraController.ForOwner(owner);

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();

        if (playerMovement == null)
        {
            Debug.LogError("PlayerMovement not found on " + gameObject.name);
        }
    }

    // Update is called once per frame
    void Update()
    {
        PlayerInputContext input = Controls;

        if (input == null)
            return;

        playerMovement.SetAiming(input.Aim && bike == null && broom == null);

        // Mount / Dismount
        if (input.InteractPressed)
        {
            if (currentVehicle != null && currentVehicle.IsDriver(this))
            {
                if (currentVehicle.Dismount(this))
                    currentVehicle = null;
                return;
            }

            foreach (var vehicle in nearbyVehicles)
            {
                if (vehicle.CanMount(gameObject))
                {
                    vehicle.Mount(this, playerMovement);
                    currentVehicle = vehicle;
                    break;
                }
            }
        }

        // Movement input
        Vector2 move = input.Move;

        // Bike Control
        if (bike != null)
        {
            bike.SetInput(move.y, move.x, input.Brake);
            return;
        }

        // Broom Control
        if (broom != null)
        {
            float throttle = input.Throttle;

            broom.SetInput(move.x, move.y, input.Yaw, throttle > 0.5f, throttle < -0.5f);
            return;
        }

        // Player Control
        playerMovement.SetInputVector(new Vector3(move.x, 0f, move.y));
        playerMovement.SetSprint(input.Sprint);
    }

    // Assign bike
    public void SetBike(BicycleController newBike)
    {
        bike = newBike;
        broom = null;
    }

    public void SetBroom(FlyingBroomController newBroom)
    {
        broom = newBroom;
        bike = null;
    }

    public void ClearVehicle()
    {
        bike = null;
        broom = null;
    }

    #region Nearby Vehicle Detection
    private void OnTriggerEnter(Collider other)
    {
        MountableVehicle vehicle = other.GetComponent<MountableVehicle>();
        if (vehicle != null && !nearbyVehicles.Contains(vehicle))
        {
            nearbyVehicles.Add(vehicle);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        MountableVehicle vehicle = other.GetComponent<MountableVehicle>();
        if (vehicle != null && nearbyVehicles.Contains(vehicle))
        {
            nearbyVehicles.Remove(vehicle);
        }
    }
    #endregion
}
