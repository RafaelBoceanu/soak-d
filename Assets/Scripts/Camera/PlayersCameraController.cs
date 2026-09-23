using System.Collections.Generic;
using Unity.VisualScripting;
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

    [Header("Pee Framing")]
    [Tooltip("Camera distance while the zipper is open.")]
    [SerializeField] float peeDistance = 3.2f;
    [Tooltip("Framing while the zipper is open")]
    [SerializeField] Vector2 peeFramingOffset = new Vector2(0.85f, 0.15f);
    [Tooltip("How far down the camera may look while the zipper is open")]
    [SerializeField] float peeMaxPitch = 65f;
    [Tooltip("How far in front of the character the camera looks while the zipper is open.")]
    [SerializeField] float peeLookAhead = 1.5f;
    [Tooltip("The camera is pushed at least this far down when the zipper is open.")]
    [SerializeField] float peeEntryPitch = 32f;
    [Tooltip("How fast the view slides between the normal and the peeing frame")]
    [SerializeField] float viewBlendSpeed = 4f;

    [Header("Look Sensitivity")]
    [Tooltip("Degrees per pixel of mouse movement.")]
    [SerializeField] float mouseSensitivity = 0.05f;
    [Tooltip("Degrees per second at full stick deflection.")]
    [SerializeField] float gamepadSensitivity = 160f;
    [Tooltip("Vertical look speed relative to horizontal")]
    [SerializeField] float verticalSensitivityScale = 0.75f;
    [SerializeField] bool invertVertical = false;

    [Header("Vehicle Follow")]
    [Tooltip("How fast the camera swings behind a vehicle's heading.")]
    [SerializeField] float headingFollowSpeed = 2.5f;
    [Tooltip("Seconds after the last manual look input before auto-follow kicks in")]
    [SerializeField] float headingFollowDelay = 0.75f;

    bool followHeading;
    float lastLookTime = -999f;

    public void SetFollowHeading(bool value) => followHeading = value;

    [SerializeField] float smoothTime = 0.3f;

    [Header("Collision")]
    [Tooltip("Keeps the camera outside of buildings, props and the other player")]
    [SerializeField] CameraCollisionProbe collisionProbe = new CameraCollisionProbe();

    float rotationX = 20f;
    float rotationY;
    float defaultMaxPitch;
    float viewDistance;
    Vector2 viewFraming;
    float viewLookAhead;
    bool peeView;
    Vector3 currentVelocity;
    Vector3 desiredPosition;
    

    Vector3 lookPoint;
    Vector3 lookVelocity;
    [SerializeField] float lookSmoothTime = 0.05f;
    [SerializeField] float rotationSharpness = 20f;

    [Header("FOV")]
    [Tooltip("Drag the Boy/Witch root here - the object with PlayerMovement")]
    [SerializeField] PlayerMovement movement;
    [SerializeField] float baseFov = 68f;
    [SerializeField] float sprintFovBoost = 7f;
    [SerializeField] float aimFovPull = -5f;
    [SerializeField] float fovSharpness = 6f;

    [Header("Shake")]
    [SerializeField] float shakeMaxOffset = 0.25f;
    [SerializeField] float shakeMaxRoll = 2.5f;
    [SerializeField] float shakeFrequency = 22f;
    [SerializeField] float traumaDecay = 1.6f;

    float currentFov;
    float trauma;
    float shakeSeed;

    Vector3 pivot;
    Camera cam;
    Transform ownerRoot;

    static readonly Dictionary<OwnerType, PlayersCameraController> rigs =
        new Dictionary<OwnerType, PlayersCameraController>();

    public OwnerType Owner => owner;
    public float Pitch => rotationX;
    public float MinPitch => minVerticalAngle;
    public float MaxPitch => maxVerticalAngle;

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
        defaultMaxPitch = maxVerticalAngle;
        viewDistance = distanceToTarget;
        viewFraming = framingOffset;

        cam = GetComponent<Camera>();
        if (cam == null) 
            cam = GetComponentInChildren<Camera>();

        ownerRoot = ResolveOwnerRoot();

        shakeSeed = Random.value * 1000f;
        currentFov = baseFov;

        if (cam != null)
            cam.fieldOfView = baseFov;
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
        {
            pivot = FramedTarget(Quaternion.Euler(rotationX, rotationY, 0));
            lookPoint = pivot;
        }
        else
        {
            pivot = transform.position;
            lookPoint = transform.position;
        }
    }

    void LateUpdate()
    {
        if (followTarget == null) return;

        BlendView();
        ApplyLook();

        if (followHeading && headingFollowSpeed > 0f && Time.time - lastLookTime > headingFollowDelay)
        {
            float t = 1f - Mathf.Exp(-headingFollowSpeed * Time.deltaTime);
            rotationY = Mathf.LerpAngle(rotationY, followTarget.eulerAngles.y, t);
        }

        Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0);
        Vector3 framedTarget = FramedTarget(rotation);

        pivot = Vector3.SmoothDamp(pivot, framedTarget, ref currentVelocity, smoothTime);

        desiredPosition = pivot + rotation * new Vector3(0, 0, -viewDistance);

        transform.position = collisionProbe.Resolve(
            pivot,
            desiredPosition,
            cam,
            Time.deltaTime,
            followTarget,
            ownerRoot
        );

        lookPoint = Vector3.SmoothDamp(lookPoint, framedTarget, ref lookVelocity, lookSmoothTime);

        Vector3 toTarget = lookPoint - transform.position;

        Quaternion targetRotation = toTarget.sqrMagnitude > 0.04f
            ? Quaternion.LookRotation(toTarget)
            : rotation;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            1f - Mathf.Exp(-rotationSharpness * Time.deltaTime)
        );

        ApplyFov();
        ApplyShake();
    }

    private Vector3 FramedTarget(Quaternion rotation)
    {

        Vector3 flatForward = Quaternion.Euler(0f, rotationY, 0f) * Vector3.forward;

        return followTarget.position
            + Vector3.up * viewFraming.y
            + rotation * Vector3.right * viewFraming.x
            + flatForward * viewLookAhead;
    }

    private Transform ResolveOwnerRoot()
    {
        PlayerInputHandler handler = GetComponentInParent<PlayerInputHandler>();
        return handler != null ? handler.transform : null;
    }

    private void ApplyLook()
    {
        if (Time.timeScale == 0f) return;

        PlayerInputContext input = TwoPlayerInputManager.GetPlayer(owner);

        if (input == null)
            return;

        Vector2 look = input.Look;

        Vector2 degrees = input.LookIsDelta
            ? look * mouseSensitivity
            : look * gamepadSensitivity * Time.deltaTime;

        if (degrees.sqrMagnitude > 0.0001f)
            lastLookTime = Time.time;

        rotationY += degrees.x;
        rotationX += (invertVertical ? degrees.y : -degrees.y) * verticalSensitivityScale;
        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);
    }

    public void SetFollowTarget(Transform newTarget)
    {
        followTarget = newTarget;
        ownerRoot = ResolveOwnerRoot();

        if (newTarget != null)
            lookPoint = newTarget.position;
    }

    public void SnapToTarget()
    {
        viewDistance = peeView ? peeDistance : distanceToTarget;
        viewFraming = peeView ? peeFramingOffset : framingOffset;
        viewLookAhead = peeView ? peeLookAhead : 0f;

        if (followTarget == null) return;

        rotationY = followTarget.eulerAngles.y;

        Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0f);

        pivot = FramedTarget(rotation);
        lookPoint = pivot;
        currentVelocity = Vector2.zero;
        lookVelocity = Vector2.zero;

        transform.position = pivot + rotation * new Vector3(0f, 0f, -viewDistance);

        Vector3 toTarget = pivot - transform.position;

        if (toTarget.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(toTarget);
    }

    public void SetOffset(float newDistance, Vector2 newFramingOffset)
    {
        distanceToTarget = newDistance;
        framingOffset = newFramingOffset;
    }

    public void SetPeeView(bool value)
    {
        if (peeView == value) return;

        peeView = value;

        maxVerticalAngle = value ? peeMaxPitch : defaultMaxPitch;

        if (value)
            rotationX = Mathf.Max(rotationX, peeEntryPitch);

        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);
    }

    void BlendView()
    {
        float targetDistance = peeView ? peeDistance : distanceToTarget;
        Vector2 targetFraming = peeView ? peeFramingOffset : framingOffset;
        float t = 1f - Mathf.Exp(-viewBlendSpeed * Time.deltaTime);

        viewDistance = Mathf.Lerp(viewDistance, targetDistance, t);
        viewFraming = Vector2.Lerp(viewFraming, targetFraming, t);
        viewLookAhead = Mathf.Lerp(viewLookAhead, peeView ? peeLookAhead : 0f, t);
    }

    #region FOV and shake
    private void ApplyFov()
    {
        if (cam == null) return;

        float target = baseFov;

        if (movement != null)
        {
            if (movement.IsSprinting) target += sprintFovBoost;
            if (movement.IsAiming)    target += aimFovPull;
        }

        currentFov = Mathf.Lerp(
            currentFov, target, 1f - Mathf.Exp(-fovSharpness * Time.deltaTime));

        cam.fieldOfView = currentFov;
    }

    private void ApplyShake()
    {
        if (trauma <= 0f) return;

        trauma = Mathf.Max(0f, trauma - traumaDecay * Time.deltaTime);

        float strength = trauma * trauma;
        float t = Time.time * shakeFrequency;

        float nx = Mathf.PerlinNoise(shakeSeed,       t) * 2f - 1f;
        float ny = Mathf.PerlinNoise(shakeSeed + 17f, t) * 2f - 1f;
        float nz = Mathf.PerlinNoise(shakeSeed + 31f, t) * 2f - 1f;

        transform.position += transform.right * (nx * shakeMaxOffset * strength)
                            + transform.up    * (ny * shakeMaxOffset * strength);

        transform.rotation *= Quaternion.Euler(0f, 0f, nz * shakeMaxRoll * strength);
    }

    public void AddShake(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount);
    }

    public static void Shake(OwnerType owner, float amount)
    {
        PlayersCameraController rig = ForOwner(owner);

        if (rig != null)
            rig.AddShake(amount);
    }
    #endregion
}
