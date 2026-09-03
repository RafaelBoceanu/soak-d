using System.Collections.Generic;
using UnityEngine;

public class PlayersCameraController : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Which player this camera belongs to. It only reads that player's look input.")]
    [SerializeField] OwnerType owner = OwnerType.Boy;

    [SerializeField] Transform followTarget;
    [SerializeField] float distanceToTarget = 7f;
    [SerializeField] float minVerticalAngle = -20f;
    [SerializeField] float maxVerticalAngle = 60f;
    [SerializeField] Vector2 framingOffset = new Vector2(0, 1f);

    [Header("Look Sensitivity")]
    [Tooltip("Degrees per pixel of mouse movement.")]
    [SerializeField] float mouseSensitivity = 0.3f;
    [Tooltip("Degrees per second at full stick deflection.")]
    [SerializeField] float gamepadSensitivity = 220f;
    [SerializeField] bool invertVertical = false;

    [SerializeField] float smoothTime = 0.3f;

    float rotationX = 20f;
    float rotationY;
    Vector3 currentVelocity;
    Vector3 desiredPosition;

    Vector3 lookPoint;
    Vector3 lookVelocity;
    [SerializeField] float lookSmoothTime = 0.05f;
    [SerializeField] float rotationSharpness = 20f;

    static readonly Dictionary<OwnerType, PlayersCameraController> rigs =
        new Dictionary<OwnerType, PlayersCameraController>();

    public OwnerType Owner => owner;

    public static PlayersCameraController ForOwner(OwnerType owner) =>
        rigs.TryGetValue(owner, out PlayersCameraController rig) ? rig : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        rigs.Clear();
    }

    void Awake()
    {
        rigs[owner] = this;
    }

    void OnDestroy()
    {
        if (ForOwner(owner) == this)
            rigs.Remove(owner);
    }

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        rotationY = angles.y;
        if (followTarget != null) 
            lookPoint = followTarget.position;
    }

    private void LateUpdate()
    {
        if (followTarget == null) return;

        ApplyLook();

        Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0);
        Vector3 offset = rotation * new Vector3(0, 0, -distanceToTarget);

        Vector3 targetPosition = followTarget.position
                       + Vector3.up * framingOffset.y
                       + rotation * Vector3.right * framingOffset.x;
        desiredPosition = targetPosition + offset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, smoothTime);
        lookPoint = Vector3.SmoothDamp(lookPoint, targetPosition, ref lookVelocity, lookSmoothTime);

        Vector3 toTarget = lookPoint - transform.position;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(toTarget),
            1f - Mathf.Exp(-rotationSharpness * Time.deltaTime)
        );
    }

    private void ApplyLook()
    {
        PlayerInputContext input = TwoPlayerInputManager.GetPlayer(owner);

        if (input == null)
            return;

        Vector2 look = input.Look;

        Vector2 degrees = input.LookIsDelta
            ? look * mouseSensitivity
            : look * gamepadSensitivity * Time.deltaTime;

        rotationY += degrees.x;
        rotationX += invertVertical ? degrees.y : -degrees.y;
        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);
    }

    public void SetFollowTarget(Transform newTarget)
    {
        followTarget = newTarget;
        if (newTarget != null)
            lookPoint = newTarget.position;
    }

    public void SetOffset(float newDistance, Vector2 newFramingOffset)
    {
        distanceToTarget = newDistance;
        framingOffset = newFramingOffset;
    }
}
