using Unity.VisualScripting;
using UnityEngine;

public class PeeSystem : MonoBehaviour
{
    public enum AimMode
    {
        [Tooltip("Free aim for the Boy, pitch only for everyone else")]
        Auto,
        [Tooltip("The stream follows the character's facing, the camera only tips it up and down")]
        PitchOnly,
        [Tooltip("The stream is lobbed at whatever the middle of the screen is on, and can soak pedestrians")]
        FreeAim
    }

    [Header("Input")]
    [Tooltip("Which player owns this character. Left at the default it is taken from the PlayerInputHandler on this object.")]
    [SerializeField] private OwnerType owner = OwnerType.Boy;

    [Header("Pee Settings")]
    [Tooltip("How full the bladder has to be before a stream will start, as a share of the bar.")]
    [SerializeField, Range(0f, 1f)] private float peeThreshold = 0.05f;

    [Tooltip("Share of the bar below which the stream gives out.")]
    [SerializeField, Range(0f, 1f)] private float minPeeValue = 0.01f;

    [Header("Aiming")]
    [Tooltip("Camera rig this player aims with. If empty, it is taken from the PlayerInputHandler.")]
    [SerializeField] private PlayersCameraController cameraRig;

    [Tooltip("Stream angle when the camera is looking as far up as it goes. If negative, points the arc upwards.")]
    [SerializeField] private float minPitch = -10f;

    [Tooltip("Stream angle when the camera is looking as far down as it goes. 90 deg is straigh at character's feet")]
    [SerializeField] private float maxPitch = 70f;

    [Tooltip("How quickly the stream swings to the aimed angle.")]
    [SerializeField] private float aimSharpness = 12f;

    [Tooltip("World ring showing where the stream will land.")]
    [SerializeField] private PeeAimRing aimRing;

    [SerializeField] private AimMode aimMode = AimMode.Auto;

    [Header("Free Aim")]
    [Tooltip("What the middle of the screen can pick as a target")]
    [SerializeField] private LayerMask aimMask = Physics.DefaultRaycastLayers;

    [Tooltip("How far the aim looks for something to pee on")]
    [SerializeField, Min(1f)] private float aimDistance = 40f;

    [Tooltip("Least pressure the arc is traced with, so an emptying bladder still has useful range")]
    [SerializeField, Range(0f, 1f)] private float minAimPressure = 0.45f;

    [Tooltip("Steepest the stream can point up")]
    [SerializeField, Range(0f, 80f)] private float maxLobAngle = 55f;

    [Tooltip("Steepest the stream can point down")]
    [SerializeField, Range(0f, 90f)] private float maxDropAngle = 80f;

    [Tooltip("How far the stream can swing left or right of where the character is facing")]
    [SerializeField, Range(0f, 90f)] private float maxYawOffset = 60f;

    [Header("Soaking Pedestrians")]
    [Tooltip("Layers pedestrians are on")]
    [SerializeField] private LayerMask pedestrianMask = 0;

    [Tooltip("Least a stream counts for when soaking someone")]
    [SerializeField, Range(0.01f, 1f)] private float weakestSoakingStream = 0.25f;

    private bool isPeeing = false;
    private bool zipperClosed = true;
    private float currentFlow;
    private float currentPitch;
    private Quaternion spawnRestRotation;
    private Quaternion currentAim;
    private bool aimApplied;
    private bool freeAim;

    private Collider lastTargetCollider;
    private PedestrianReaction lastTarget;

    private readonly RaycastHit[] aimHits = new RaycastHit[16];

    private PlayerInputHandler inputHandler;
    private PlayerMovement playerMovement;

    private ParticleSystem peeParticleSystem;

    [SerializeField] private GameObject peePrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private AudioSource zipperOpenSound;
    [SerializeField] private AudioSource zipperCloseSound;
    [SerializeField] private AudioSource peeSound;
    [SerializeField] private PlayerNeeds playerNeeds;
    [SerializeField] private ZipperCensor zipperCensor;
    [SerializeField] private PeePuddle peePuddle;
    [SerializeField] private Animator animator;

    private ParticleSystem.EmissionModule emission;
    private ParticleSystem.MainModule main;

    private PlayerInputContext Controls => TwoPlayerInputManager.GetPlayer(owner);

    public float CurrentFlow => isPeeing ? currentFlow : 0f;
    public bool IsZipperOpen => !zipperClosed;
    public bool IsFreeAim => freeAim;

    void Awake()
    {
        inputHandler = GetComponent<PlayerInputHandler>();
        playerMovement = GetComponent<PlayerMovement>();
        
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
            Debug.LogWarning($"[PeeSystem] {name} found no Animator - peeing animation will not play.", this);

        if (inputHandler != null)
            owner = inputHandler.Owner;

        freeAim = aimMode == AimMode.FreeAim || (aimMode == AimMode.Auto && owner == OwnerType.Boy);

        if (pedestrianMask.value == 0)
            pedestrianMask = LayerMask.GetMask("Pedestrian");
    }

    void OnDisable()
    {
        CloseZipper(false);
    }

    private bool CanPee => playerMovement == null || playerMovement.CanPee;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        spawnRestRotation = spawnPoint.localRotation;
        currentPitch = AimPitch();
        currentAim = spawnPoint.rotation;
        GameObject instance = Instantiate(peePrefab, spawnPoint.position, spawnPoint.rotation, spawnPoint);
        peeParticleSystem = instance.GetComponent<ParticleSystem>();

        emission = peeParticleSystem.emission;
        main = peeParticleSystem.main;

        if (zipperCensor == null)
        {
            zipperCensor = GetComponent<ZipperCensor>();
        }

        if (peePuddle == null)
        {
            peePuddle = GetComponent<PeePuddle>();
        }

        if (peePuddle == null)
        {
            Debug.LogWarning($"[PeeSystem] {name} has no PeePuddle assigned - no puddles for this player.", this);
        }

        UpdateCensor();
    }

    // Update is called once per frame
    void Update()
    {
        PlayerInputContext input = Controls;

        if (input == null)
        {
            return;
        }

        if (inputHandler != null && inputHandler.IsRiding)
        {
            CancelPeeing();
            return;
        }

        if (playerNeeds != null && playerNeeds.IsHavingAccident)
        {
            CancelPeeing();
            return;
        }

        if (input.ZipPressed)
        {
            if (zipperClosed)
            {
                if (CanPee)
                {
                    OpenZipper();
                }
            }
            else
            {
                CloseZipper(true);
            }
        }

        if (!zipperClosed && !CanPee)
        {
            CloseZipper(true);
        }

        float normalizedPee = playerNeeds.maxPee > 0f ? playerNeeds.pee / playerNeeds.maxPee : 0f;

        bool canStartPeeing = normalizedPee >= peeThreshold;
        bool hasPeeLeft = normalizedPee > minPeeValue;

        if (!zipperClosed && hasPeeLeft && (canStartPeeing || isPeeing))
        {
            if (input.Pee)
            {
                StartPeeing();
                playerNeeds.Relieve(Time.deltaTime);
                currentFlow = normalizedPee * playerNeeds.FlowScale;
                UpdateVisuals(currentFlow);
                if (freeAim)
                {
                    StreamOnTargets();
                }
                else if (peePuddle != null)
                {
                    peePuddle.Grow(currentFlow);
                }
            }
            else
            {
                StopPeeing();
            }
        }
        else
        {
            StopPeeing();
        }
    }

    void LateUpdate()
    {
        if (spawnPoint == null)
            return;

        if (zipperClosed)
        {
            if (aimApplied)
            {
                spawnPoint.localRotation = spawnRestRotation;
                aimApplied = false;
            }

            UpdateAimMarker();
            return;
        }

        float blend = 1f - Mathf.Exp(-aimSharpness * Time.deltaTime);

        if (freeAim)
        {
            currentAim = Quaternion.Slerp(currentAim, FreeAimRotation(), blend);
            spawnPoint.rotation = currentAim;
        }
        else
        {
            currentPitch = Mathf.Lerp(currentPitch, AimPitch(), blend);
            spawnPoint.rotation = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0f);
        }

        aimApplied = true;

        UpdateAimMarker();
    }

    private PlayersCameraController Rig
    {
        get
        {
            if (cameraRig == null)
                cameraRig = inputHandler != null
                    ? inputHandler.CameraController
                    : PlayersCameraController.ForOwner(owner);

            return cameraRig;
        }
    }

    private float AimPitch()
    {
        PlayersCameraController rig = Rig;

        if (rig == null)
            return Mathf.Lerp(minPitch, maxPitch, 0.5f);

        float t = Mathf.InverseLerp(rig.MinPitch, rig.MaxPitch, rig.Pitch);

        return Mathf.Lerp(minPitch, maxPitch, t);
    }

    private float Pressure(float flow) => freeAim ? Mathf.Max(flow, minAimPressure) : flow;

    private Quaternion FreeAimRotation()
    {
        Vector3 origin = spawnPoint.position;
        Vector3 target = AimTarget();

        float pressure = Pressure(PredictedFlow());
        float speed = peePuddle != null ? peePuddle.StreamSpeed(pressure) : Mathf.Lerp(3f, 9f, pressure);
        float gravity = Mathf.Abs(Physics.gravity.y) *
                        (peePuddle != null ? peePuddle.StreamGravity(pressure) : Mathf.Lerp(3f, 1.5f, pressure));

        Vector3 toTarget = target - origin;
        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        float distance = flat.magnitude;
        float height = toTarget.y;

        Vector3 facing = transform.forward;
        facing.y = 0f;

        if (facing.sqrMagnitude < 0.0001f)
            facing = Vector3.forward;

        facing.Normalize();

        float yaw;

        if (distance < 0.05f)
        {
            yaw = Quaternion.LookRotation(facing).eulerAngles.y;
        }
        else
        {
            float offset = Mathf.Clamp(Vector3.SignedAngle(facing, flat, Vector3.up), -maxYawOffset, maxYawOffset);
            yaw = Quaternion.LookRotation(facing).eulerAngles.y + offset;
        }

        float elevation = LaunchElevation(distance, height, speed, gravity);
        elevation = Mathf.Clamp(elevation, -maxDropAngle, maxLobAngle);

        return Quaternion.Euler(-elevation, yaw, 0f);
    }

    private static float LaunchElevation(float distance, float height, float speed, float gravity)
    {
        if (distance < 0.05f)
            return -90f;

        float v2 = speed * speed;
        float discriminant = v2 * v2 - gravity * (gravity * distance * distance + 2f * height * v2);

        if (discriminant >= 0f)
            return Mathf.Atan2(v2 - Mathf.Sqrt(discriminant), gravity * distance) * Mathf.Rad2Deg;

        float drop = Mathf.Max(0f, -height);
        return Mathf.Atan(speed / Mathf.Sqrt(v2 + 2f * gravity * drop)) * Mathf.Rad2Deg;
    }

    private Vector3 AimTarget()
    {
        PlayersCameraController rig = Rig;
        Camera view = rig != null ? rig.ViewCamera : null;

        Ray ray;

        if (view != null)
            ray = view.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        else
            ray = new Ray(spawnPoint.position, transform.forward);

        float skip = Mathf.Max(0f, Vector3.Dot(spawnPoint.position - ray.origin, ray.direction));
        ray.origin += ray.direction * skip;

        int count = Physics.RaycastNonAlloc(ray, aimHits, aimDistance, aimMask, QueryTriggerInteraction.Collide);

        float nearest = float.MaxValue;
        Vector3 point = ray.origin + ray.direction * aimDistance;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = aimHits[i];

            if (hit.distance >= nearest)
                continue;

            if (hit.collider.transform.IsChildOf(transform))
                continue;

            if (hit.collider.isTrigger && ReactionOf(hit.collider) == null)
                continue;

            nearest = hit.distance;
            point = hit.point;
        }

        return point;
    }

    private PedestrianReaction ReactionOf(Collider collider)
    {
        if (collider == lastTargetCollider)
            return lastTarget;

        PedestrianReaction reaction = collider.GetComponentInParent<PedestrianReaction>();

        if (reaction == null)
        {
            PedestrianAgent agent = collider.GetComponentInParent<PedestrianAgent>();

            if (agent != null)
                reaction = agent.GetComponentInChildren<PedestrianReaction>();
        }

        lastTargetCollider = collider;
        lastTarget = reaction;

        return reaction;
    }

    private bool TraceStream(float pressure, out RaycastHit landing, out PedestrianReaction target)
    {
        target = null;

        if (peePuddle == null)
        {
            landing = default;
            return false;
        }

        bool landed = peePuddle.TraceStream(pressure, pedestrianMask, out landing,
                                            out bool hitTarget, out RaycastHit targetHit);

        if (!hitTarget)
            return landed;

        target = ReactionOf(targetHit.collider);

        if (target == null)
            return landed;

        if (peePuddle.DropToGround(targetHit.point, out RaycastHit feet))
        {
            landing = feet;
            return true;
        }

        return landed;
    }

    private void StreamOnTargets()
    {
        bool landed = TraceStream(Pressure(currentFlow), out RaycastHit landing, out PedestrianReaction target);

        if (peePuddle != null)
        {
            if (landed)
                peePuddle.GrowAt(currentFlow, landing);
            else
                peePuddle.EndPuddle();
        }

        if (target != null)
            target.SoakWithPee(owner, Mathf.Max(currentFlow, weakestSoakingStream));
    }

    private float PredictedFlow()
    {
        if (isPeeing)
            return currentFlow;

        if (playerNeeds == null || playerNeeds.maxPee <= 0f)
            return 0f;

        return (playerNeeds.pee / playerNeeds.maxPee) * playerNeeds.FlowScale;
    }

    private void UpdateAimMarker()
    {
        if (aimRing == null)
            return;

        float flow = PredictedFlow();

        RaycastHit hit = default;
        PedestrianReaction target = null;

        bool landed = !zipperClosed && peePuddle != null && (freeAim
            ? TraceStream(Pressure(flow), out hit, out target)
            : peePuddle.TryPredictLanding(flow, out hit));

        if (!landed)
        {
            aimRing.Hide();
            return;
        }

        PlayersCameraController rig = Rig;
        Vector3 viewer = rig != null ? rig.transform.position : transform.position;

        aimRing.Place(hit.point, hit.normal, flow, viewer, isPeeing, target != null);
    }

    void StartPeeing()
    {

        if (!isPeeing)
        {
            isPeeing = true;
            peeParticleSystem.Play();
            peeSound.Play();
        }
    }

    void StopPeeing()
    {
        if (isPeeing)
        {
            isPeeing = false;
            currentFlow = 0f;
            peeParticleSystem.Stop();
            peeSound.Stop();
            if (peePuddle != null)
            {
                peePuddle.EndPuddle();
            }
        }
    }

    public void CancelPeeing()
    {
        CloseZipper(true);
    }

    private void OpenZipper()
    {
        zipperClosed = false;

        if (zipperOpenSound != null)
            zipperOpenSound.Play();

        if (playerMovement != null)
            playerMovement.SetPeeStance(true);

        currentPitch = AimPitch();
        currentAim = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0f);

        PlayersCameraController rig = Rig;
        if (rig != null)
            rig.SetPeeView(true);

        if (animator != null)
            animator.SetBool("isPeeing", true);

        UpdateCensor();
    }

    private void CloseZipper(bool playSound)
    {
        StopPeeing();

        if (zipperClosed) return;

        zipperClosed = true;

        if (playerMovement != null)
            playerMovement.SetPeeStance(false);

        PlayersCameraController rig = Rig;
        if (rig != null)
            rig.SetPeeView(false);

        if (playSound && zipperCloseSound != null)
            zipperCloseSound.Play();

        if (animator != null)
            animator.SetBool("isPeeing", false);

        UpdateCensor();
    }

    void UpdateVisuals(float normalized)
    {
        // Emission (flow strength)
        float rate = Mathf.Lerp(30f, 320f, normalized);
        emission.rateOverTime = rate;

        // Speed and drop follow the pressure
        float pressure = Pressure(normalized);

        // Speed (how far it shoots)
        float speed = peePuddle != null ? peePuddle.StreamSpeed(pressure) : Mathf.Lerp(3f, 9f, pressure);
        main.startSpeed = speed;

        // Size (stream thickness)
        float size = Mathf.Lerp(0.015f, 0.05f, normalized);
        main.startSize = size;

        // Gravity (more drop when weaker)
        float gravity = peePuddle != null ? peePuddle.StreamGravity(pressure) : Mathf.Lerp(3f, 1.5f, pressure);
        main.gravityModifier = gravity;

        // Sound volume scaling
        peeSound.volume = Mathf.Lerp(0.2f, 1f, normalized);

        // Stream instability (more jitter when weaker)
        var noise = peeParticleSystem.noise;
        float jitter = Mathf.Lerp(0.1f, 0.5f, 1f - normalized);
        noise.strength = jitter;

        // Drippy effect when emptier
        main.startLifetime = Mathf.Lerp(0.3f, 1.0f, normalized);

        if (freeAim)
            main.startLifetime = Mathf.Lerp(0.6f, 1.5f, pressure);
    }

    void UpdateCensor()
    {
        if (zipperCensor != null)
        {
            zipperCensor.SetVisible(!zipperClosed);
        }    
    }
}
