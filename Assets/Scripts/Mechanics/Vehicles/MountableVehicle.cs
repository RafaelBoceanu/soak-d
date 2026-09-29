using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class MountableVehicle : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform mountPoint;

    [System.Serializable]
    private struct RiderModel
    {
        public OwnerType owner;
        public GameObject model;
    }

    [Header("Restrictions")]
    [Tooltip("\"Boy\" pr \"Witch\". Left empty, either character can ride it")]
    [SerializeField] private string allowedTag; // "Boy" or "Witch"

    [Header("Character Models")]
    [Tooltip("Model shown on the vehicle while riding")]
    [SerializeField] private RiderModel[] riderModels;

    [Header("Camera Offsets")]
    [SerializeField] private float vehicleDistance = 7f;
    [SerializeField] private Vector2 vehicleFramingOffset = new Vector2(0, 1f);
    [SerializeField] private float playerDistance = 7f;
    [SerializeField] private Vector2 playerFramingOffset = new Vector2(0, 1f);

    [Header("Dismount")]
    [Tooltip("Optional exact spot the rider is placed on. Leave empty to step off to the side automatically")]
    [SerializeField] private Transform dismountPoint;
    [Tooltip("How far to the side of the vehicle the rider steps off")]
    [SerializeField] private float dismountSideOffset = 1.2f;
    [Tooltip("Free space the rider needs at the spot they step off on")]
    [SerializeField] private float dismountClearanceRadius = 0.35f;
    [Tooltip("Refuse to dismount when the ground is further below then this")]
    [SerializeField] private float maxDismountHeight = 2.5f;
    [Tooltip("When off, the rider can leave the vehicle even with no ground underneath")]
    [SerializeField] private bool requireGroundToDismount = true;
    [SerializeField] private LayerMask dismountGroundMask = ~0;

    private const float GroundProbeHeight = 1f;
    private const float GroundSnapOffset = 0.05f;

    private bool isOccupied = false;
    private PlayerInputHandler currentPlayerInput;
    private PlayerMovement currentPlayerMovement;
    private CharacterController currentController;

    private IRideable ride;
    private GameObject activeRiderModel;

    private Transform RiderAnchor => ride != null ? ride.RiderAnchor : transform;
    private float RiderRise => currentController != null 
        ? currentController.height * 0.5f - currentController.center.y + currentController.skinWidth 
        : 0f;

    private void Awake()
    {
        ride = GetComponent<IRideable>();

        if (ride == null)
            Debug.LogError($"{name}: no BicycleController or FlyingBroomController found", this);
        
        foreach (RiderModel rider in riderModels)
        {
            if (rider.model)
                rider.model.SetActive(false);
        }
    }

    public bool CanMount(GameObject player)
    {
        if (isOccupied) return false;
        if (!IsShared && !player.CompareTag(allowedTag)) return false;

        return true;
    }

    public bool IsDriver(PlayerInputHandler player)
    {
        return currentPlayerInput == player;
    }

    public string AllowedTag => allowedTag;
    public bool IsShared => string.IsNullOrEmpty(allowedTag);

    public PlayerInputHandler Rider => currentPlayerInput;

    public bool CanDismount(PlayerInputHandler player)
    {
        if (!isOccupied || currentPlayerInput != player) return false;

        return TryGetDismountPlacement(out _, out _);
    }

    public void Mount(PlayerInputHandler playerInput, PlayerMovement movement)
    {
        if (isOccupied) return;

        PeeSystem peeSystem = playerInput.GetComponent<PeeSystem>();
        if (peeSystem != null)
            peeSystem.CancelPeeing();

        isOccupied = true;
        currentPlayerInput = playerInput;
        currentPlayerMovement = movement;

        activeRiderModel = GetRiderModel(playerInput.Owner);
        if (activeRiderModel)
            activeRiderModel.SetActive(true);

        if (playerInput.CharacterModel)
            playerInput.CharacterModel.SetActive(false);

        // Disable player movement
        movement.enabled = false;

        // Disable character controller
        currentController = playerInput.GetComponent<CharacterController>();
        if (currentController != null)
            currentController.enabled = false;

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
        PlayersCameraController camController = playerInput.GetComponentInChildren<PlayersCameraController>(); ;
        if (camController != null)
        {
            camController.SetFollowTarget(transform); // follow vehicle
            camController.SetOffset(vehicleDistance, vehicleFramingOffset); // optional offset
            camController.SetFollowHeading(ride != null && ride.FollowHeading);
        }

        // Snap to mount point
        playerInput.transform.SetPositionAndRotation(mountPoint.position, mountPoint.rotation);

        // Parent player
        playerInput.transform.SetParent(RiderAnchor, true);

        // Assign control
        if (ride != null)
        {
            playerInput.SetVehicle(ride);
            ride.SetControl(true);
        }
    }

    public void SetRiderControl(bool allowed)
    {
        if (!isOccupied) return;

        if (ride != null) ride.SetControl(allowed);
    }

    public bool Dismount(PlayerInputHandler player)
    {
        if (!isOccupied || currentPlayerInput != player) return false;

        if (!TryGetDismountPlacement(out Vector3 dismountPosition, out Quaternion dismountRotation))
        {
            Debug.Log($"{name}: no safe spot to dismount on, get closer to the ground first.");
            return false;
        }

        // Stop the vehicle before releasing the rider, so it cannot drift off or shove them around
        if (ride != null) ride.SetControl(false);

        // Clear control
        currentPlayerInput.ClearVehicle();

        if (activeRiderModel)
            activeRiderModel.SetActive(false);
        activeRiderModel = null;

        if (currentPlayerInput.CharacterModel)
            currentPlayerInput.CharacterModel.SetActive(true);

        // Unparent player
        currentPlayerInput.transform.SetParent(null);

        currentPlayerInput.transform.SetPositionAndRotation(dismountPosition, dismountRotation);

        // Re-enable character controller
        CharacterController controller = currentPlayerInput.GetComponent<CharacterController>();
        if (controller != null)
            controller.enabled = true;

        // Restore rigidbody
        Rigidbody rb = currentPlayerInput.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Restore animator
        Animator anim = currentPlayerInput.GetComponentInChildren<Animator>();
        if (anim != null)
            anim.enabled = true;

        // Restore player collider
        Collider col = currentPlayerInput.GetComponent<Collider>();
        if (col != null)
            col.enabled = true;

        // Re-enable player movement
        currentPlayerMovement.enabled = true;
        currentPlayerMovement.ResetFall();

        // Restore camera to follow player
        PlayersCameraController camController = currentPlayerInput.GetComponentInChildren<PlayersCameraController>();
        if (camController != null)
        {
            camController.SetFollowTarget(currentPlayerInput.transform); // follow player
            camController.SetOffset(playerDistance, playerFramingOffset); // optional offset
            camController.SetFollowHeading(false);
        }

        // Clear control
        currentPlayerInput = null;
        currentPlayerMovement = null;
        currentController = null;
        isOccupied = false;

        return true;
    }

    private GameObject GetRiderModel(OwnerType owner)
    {
        foreach (RiderModel rider in riderModels)
        {
            if (rider.owner == owner)
                return rider.model;
        }

        return null;
    }

    #region Dismount Placement
    private bool TryGetDismountPlacement(out Vector3 position, out Quaternion rotation)
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();
        rotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 anchor = transform.position;

        Vector3[] candidates = dismountPoint != null
            ? new[]
            {
                dismountPoint.position,
                anchor + right * dismountSideOffset,
                anchor - right * dismountSideOffset,
                anchor - forward * dismountSideOffset,
                anchor
            }
            : new[]
            {
                anchor + right * dismountSideOffset,
                anchor - right * dismountSideOffset,
                anchor - forward * dismountSideOffset,
                anchor
            };

        bool hasFallback = false;
        Vector3 fallback = Vector3.zero;

        foreach (Vector3 candidate in candidates)
        {
            if (!TryGetGroundedSpot(candidate, out Vector3 grounded)) continue;

            if (IsSpotClear(grounded))
            {
                position = grounded;
                return true;
            }

            if (!hasFallback)
            {
                hasFallback = true;
                fallback = grounded;
            }
        }

        if (hasFallback)
        {
            position = fallback;
            return true;
        }

        position = anchor + right * dismountSideOffset;
        return !requireGroundToDismount;
    }

    private bool TryGetGroundedSpot(Vector3 candidate, out Vector3 grounded)
    {
        grounded = candidate;

        Vector3 origin = candidate + Vector3.up * GroundProbeHeight;
        float maxDistance = GroundProbeHeight + maxDismountHeight;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            maxDistance,
            dismountGroundMask,
            QueryTriggerInteraction.Ignore
        );

        bool found = false;
        float closest = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (IsOwnCollider(hit.collider)) continue;
            if (hit.distance >= closest) continue;

            closest = hit.distance;
            grounded = hit.point + Vector3.up * (GroundSnapOffset + RiderRise);
            found = true;
        }

        return found;
    }

    private bool IsSpotClear(Vector3 pivot)
    {
        if (currentController == null || dismountClearanceRadius <= 0f) return true;

        Vector3 centre = pivot + currentController.center;
        float half = Mathf.Max(0f, currentController.height * 0.5f - dismountClearanceRadius);

        Collider[] overlaps = Physics.OverlapCapsule(
            centre - Vector3.up * half,
            centre + Vector3.up * half,
            dismountClearanceRadius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider overlap in overlaps)
        {
            if (!IsOwnCollider(overlap)) return false;
        }

        return true;
    }

    private bool IsOwnCollider(Collider col)
    {
        if (col == null) return true;

        Transform t = col.transform;

        if (t.IsChildOf(transform)) return true;

        if (currentPlayerInput != null && t.IsChildOf(currentPlayerInput.transform)) return true;

        if (ride != null && ride.OwnsCollider(col)) return true;

        return false;
    }
    #endregion
}
