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

    [Tooltip("This character's walking model, hidden while riding a vehicle")]
    [SerializeField] private GameObject characterModel;

    private PlayerMovement playerMovement;

    private IRideable ride;

    private MountableVehicle currentVehicle;
    
    private List<MountableVehicle> nearbyVehicles = new List<MountableVehicle>();

    public OwnerType Owner => owner;
    public bool CanHop => owner == OwnerType.Boy;
    public GameObject CharacterModel => characterModel;

    public PlayerInputContext Controls => TwoPlayerInputManager.GetPlayer(owner);

    public PlayersCameraController CameraController =>
        cameraController != null ? cameraController : PlayersCameraController.ForOwner(owner);
    public bool IsRiding => currentVehicle != null;

    public MountableVehicle CurrentVehicle => currentVehicle;

    public bool ForceDismount()
    {
        if (currentVehicle == null)
            return false;

        if (!currentVehicle.Dismount(this))
            return false;

        currentVehicle = null;
        return true;
    }    

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

        playerMovement.SetAiming(input.Aim && ride == null);

        // Mount / Dismount
        if (input.InteractPressed)
        {
            if (currentVehicle != null && currentVehicle.IsDriver(this))
            {
                if (currentVehicle.Dismount(this))
                    currentVehicle = null;
                return;
            }

            MountableVehicle nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (var vehicle in nearbyVehicles)
            {
                if (vehicle == null || !vehicle.CanMount(gameObject)) continue;

                float distance = (vehicle.transform.position - transform.position).sqrMagnitude;
                if (distance >= nearestDistance) continue;

                nearest = vehicle;
                nearestDistance = distance;
            }

            if (nearest != null)
            {
                nearest.Mount(this, playerMovement);
                currentVehicle = nearest;
            }
        }

        // Movement input
        Vector2 move = input.Move;

        // Vehicle Control
        if (ride != null)
        {
            ride.ReadInput(input);

            if (CanHop && input.JumpPressed && ride is GroundVehicleController vehicle)
                vehicle.Hop();

            return;
        }

        // Player Control
        playerMovement.SetInputVector(new Vector3(move.x, 0f, move.y));
        playerMovement.SetSprint(input.Sprint);
    }

    public void SetVehicle(IRideable vehicle)
    {
        ride = vehicle;
    }

    public void ClearVehicle()
    {
        ride = null;
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
