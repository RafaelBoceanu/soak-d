using System.Collections.Generic;
using UnityEngine;

public class PlayerInputHandler : MonoBehaviour
{
    [Header("Input Axes")]
    [SerializeField] private string horizontalAxis;
    [SerializeField] private string verticalAxis;

    [Header("Input Buttons")]
    [SerializeField] private string sprintButton;
    [SerializeField] private string interactButton;

    private PlayerMovement playerMovement;

    private BicycleController bike;
    private FlyingBroomController broom;

    private MountableVehicle currentVehicle;
    
    private List<MountableVehicle> nearbyVehicles = new List<MountableVehicle>();

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
        // Mount / Dismount
        if (Input.GetButtonDown(interactButton))
        {
            if (currentVehicle != null && currentVehicle.IsDriver(this))
            {
                currentVehicle.Dismount(this);
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
        float x = Input.GetAxis(horizontalAxis);
        float z = Input.GetAxis(verticalAxis);

        // Sprint input
        bool isSprinting = Input.GetButton(sprintButton);

        // Bike Control
        if (bike != null)
        {
            bool brake = Input.GetKey(KeyCode.Space);
            bike.SetInput(z, x, brake);
            return;
        }

        // Broom Control
        if (broom != null)
        {
            float roll = Input.GetAxis("Roll");
            float pitch = Input.GetAxis("Pitch");
            float yaw = Input.GetAxis("Yaw");

            bool throttleUp = Input.GetKey(KeyCode.Space);
            bool throttleDown = Input.GetKey(KeyCode.LeftControl);

            broom.SetInput(roll, pitch, yaw, throttleUp, throttleDown);
            return;
        }

        // Player Control
        playerMovement.SetInputVector(new Vector3(x, 0f, z));
        playerMovement.SetSprint(isSprinting);
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
