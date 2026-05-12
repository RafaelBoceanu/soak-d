using UnityEngine;
using UnityEngine.InputSystem;

public class MountableVehicle : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform mountPoint;

    [Header("Restrictions")]
    [SerializeField] private string allowedTag; // "Boy" or "Witch"

    [Header("Character Models")]
    [SerializeField] private GameObject mountedCharacterModel;
    [SerializeField] private GameObject playableCharacterModel;

    [Header("Camera Offsets")]
    [SerializeField] private float vehicleDistance = 7f;
    [SerializeField] private Vector2 vehicleFramingOffset = new Vector2(0, 1f);
    [SerializeField] private float playerDistance = 7f;
    [SerializeField] private Vector2 playerFramingOffset = new Vector2(0, 1f);

    private bool isOccupied = false;
    private PlayerInputHandler currentPlayerInput;
    private PlayerMovement currentPlayerMovement;

    private BicycleController bike;
    private FlyingBroomController broom;

    private void Awake()
    {
        bike = GetComponent<BicycleController>();
        broom = GetComponent<FlyingBroomController>();

        if (mountedCharacterModel)
            mountedCharacterModel.SetActive(false);
    }

    public bool CanMount(GameObject player)
    {
        if (isOccupied) return false;
        if (!player.CompareTag(allowedTag)) return false;

        return true;
    }

    public bool IsDriver(PlayerInputHandler player)
    {
        return currentPlayerInput == player;
    }

    public void Mount(PlayerInputHandler playerInput, PlayerMovement movement)
    {
        if (isOccupied) return;

        isOccupied = true;
        currentPlayerInput = playerInput;
        currentPlayerMovement = movement;

        if (mountedCharacterModel)
            mountedCharacterModel.SetActive(true);

        if (playableCharacterModel)
            playableCharacterModel.SetActive(false);

        // Disable player movement
        movement.enabled = false;

        // Disable character controller
        CharacterController controller = playerInput.GetComponent<CharacterController>();
        if (controller != null) 
            controller.enabled = false;

        // Disable player rigidbody
        Rigidbody rb = playerInput.GetComponent<Rigidbody>();
        if (rb != null)
            rb.isKinematic = true;

        // Disable player animator
        Animator anim = playerInput.GetComponentInChildren<Animator>();
        if (anim != null)
            anim.enabled = false; 

        // Disable player collider
        Collider col = playerInput.GetComponent<Collider>();
        if (col != null)
            col.enabled = false;

        // Make the player's camera follow the vehicle
        PlayersCameraController camController = playerInput.GetComponentInChildren<PlayersCameraController>();
        if (camController != null)
        {
            camController.SetFollowTarget(transform); // follow vehicle
            camController.SetOffset(vehicleDistance, vehicleFramingOffset); // optional offset
        }

        // Snap to mount point
        playerInput.transform.position = mountPoint.position;
        playerInput.transform.rotation = mountPoint.rotation;

        // Parent player
        playerInput.transform.SetParent(transform);

        // Assing control
        if (bike != null)
        {
            playerInput.SetBike(bike);
            bike.SetControl(true);
        }
        else if (broom != null)
        {
            playerInput.SetBroom(broom);
            broom.SetControl(true);
        }
    }

    public void Dismount(PlayerInputHandler player)
    {
        if (!isOccupied || currentPlayerInput != player) return;

        if (mountedCharacterModel)
            mountedCharacterModel.SetActive(false);
        if (playableCharacterModel)
            playableCharacterModel.SetActive(true);

        // Unparent player
        currentPlayerInput.transform.SetParent(null);

        // Re-enable player movement
        currentPlayerMovement.enabled = true;

        // Re-enable character controller
        CharacterController controller = currentPlayerInput.GetComponent<CharacterController>();
        if (controller != null) 
            controller.enabled = true;

        // Restore rigidbody
        Rigidbody rb = currentPlayerInput.GetComponent<Rigidbody>();
        if (rb != null)
            rb.isKinematic = false;

        // Restore animator
        Animator anim = currentPlayerInput.GetComponentInChildren<Animator>();
        if (anim != null)
            anim.enabled = true;

        // Restore player collider
        Collider col = currentPlayerInput.GetComponent<Collider>();
        if (col != null)
            col.enabled = true;

        // Restore camera to follow player
        PlayersCameraController camController = currentPlayerInput.GetComponentInChildren<PlayersCameraController>();
        if (camController != null)
        {
            camController.SetFollowTarget(currentPlayerInput.transform); // follow player
            camController.SetOffset(playerDistance, playerFramingOffset); // optional offset
        }

        // Clear control
        currentPlayerInput.ClearVehicle();

        // Disable vehicle control
        if (bike != null) bike.SetControl(false);
        if (broom != null) broom.SetControl(false);

        isOccupied = false;
    }
}
