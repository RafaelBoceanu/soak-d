using UnityEngine;
using UnityEngine.Serialization;

public class GroundVehicleController : MonoBehaviour, IRideable
{
    RaycastHit hit;
    float moveInput, steerInput, currentVelocityOffset;

    float wheelRadius;

    bool isGrounded;
    Vector3 groundNormal = Vector3.up;

    float leanAngle;
    float yawRate, corneringLoad;
    bool stepping;
    string stepReason = "idle";
    float groundAhead;
    bool initialised;

    [HideInInspector] public Vector3 velocity;
    public Rigidbody sphereRB;
    [FormerlySerializedAs("bicycleBody")] public Rigidbody body;
    public TrailRenderer skidTrail;

    public float maxSpeed = 20f, acceleration = 3f, gravity = 25f, skidWidth = 0.062f;

    [Tooltip("How far past the tyres' grip at the steering has to reach before the bike marks the road")]
    public float minSkidLoad = 2f;

    [Header("Ground Detection")]
    [Tooltip("Extra clearance under the wheel that still counts as grounded. A large value makes the bike hover over kerbs")]
    public float groundProbeSkin = 0.05f;
    [Tooltip("Steepest surface the bike is allowed to stand on")]
    public float maxGroundAngle = 50f;
    [Range(0f, 1f)]
    [Tooltip("How much drive and steering survives while the wheel is off the ground. At zero the bike stalls against every kerb")]
    public float airControl = 0.35f;

    [Range(0f, 1f)]
    [Tooltip("Top reverse speed as a fraction of the forward top speed")]
    public float reverseFraction = 0.25f;

    [Header("Grip")]
    [Tooltip("How quickly sideways sliding is scrubbed off. Low values drift, high values carve")]
    public float gripStrength = 20f;

    [Header("Kerbs")]
    [Tooltip("Tallest step the bike will climb. Anything higher is treatead as a wall")]
    public float maxStepHeight = 0.2f;
    [Tooltip("Smallest step worth lifting over")]
    public float minStepHeight = 0.03f;
    [Tooltip("Fastest the wheel may rise while on flat ground")]
    public float maxGroundedRise = 0.5f;
    [Tooltip("How fast the wheel is lifted over a step, in units per second")]
    public float stepClimbSpeed = 2f;
    [Tooltip("How far in front of the wheel the ground height is sampled")]
    public float stepLookAhead = 0.15f;

    public LayerMask groundLayer;
    [Tooltip("Hold an unridden bike in place instead of letting it roll off or sink. Turn it off if the parked bikes must settle on their own")]
    public bool freezeWhenUnridden = true;

    [Header("Tilt and Lean")]
    [Tooltip("How quickly the frame settles onto the ground. Higher number is snappier")]
    public float tiltSharpness = 10f;
    [Tooltip("How quickly the surface the frame aligns to is allowed to change. Keeps kerbs from popping the bike")]
    public float groundNormalSharpness = 8f;
    [Tooltip("Roll angle into a turn at full steering and full speed")]
    public float zTiltAngle = 30f;
    [Tooltip("How quickly the bike leans into and out of a turn")]
    public float leanSharpness = 6f;

    [Header("Steering")]
    [Tooltip("Tightest radius the bike can turn, in units")]
    public float minTurnRadius = 2.5f;
    [Tooltip("Most sideways acceleration the tyres hold")]
    public float maxCorneringAccel = 8f;

    [Header("Braking")]
    [Range(1, 10)]
    public float brakingStrength = 5f;
    [Tooltip("Deceleration per point of braking strength, in units per second squared")]
    public float brakeDeceleration = 25f;

    [Header("Bunny Hop")]
    [Tooltip("Lets the rider make the bike jump by pressing the Jump button")]
    public bool canHop = true;
    [Tooltip("How high the wheel rises off the ground")]
    public float hopHeight = 0.9f;
    [Tooltip("Seconds after landing before the bike can jump again")]
    public float hopCooldown = 0.35f;
    [Tooltip("Seconds a press is remembered while the wheel is still in the air")]
    public float hopBufferSeconds = 0.15f;
    [Tooltip("Seconds after take-off during which the ground probe is ignored")]
    public float hopLiftoffSeconds = 0.12f;
    public AudioSource hopSound;

    private bool isControlled = false;
    private bool isBraking = false;
    private float hopRequestedAt = float.NegativeInfinity;
    private float liftoffUntil;
    private float nextHopTime;
    private bool hopping;

    public bool IsGrounded => isGrounded;
    public event System.Action OnHop;
    public event System.Action OnParked;
    public bool IsControlled => isControlled;
    public float SteerInput => steerInput;

    void Awake()
    {
        Initialise();
    }

    void Initialise()
    {
        if (initialised) return;
        initialised = true;

        sphereRB.transform.parent = null;
        body.transform.parent = null;

        SphereCollider wheel = sphereRB.GetComponent<SphereCollider>();
        Vector3 wheelScale = sphereRB.transform.localScale;
        float uniformScale = Mathf.Max(Mathf.Abs(wheelScale.x), Mathf.Abs(wheelScale.y), Mathf.Abs(wheelScale.z));

        wheelRadius = wheel.radius * uniformScale;

        if (skidTrail != null)
        {
            skidTrail.startWidth = skidWidth;
            skidTrail.emitting = false;
        }

        if (freezeWhenUnridden)
            SetPhysicsActive(false);
    }

    public void SetControl(bool active)
    {
        Initialise();

        isControlled = active;
        enabled = active;

        if (active)
        {
            groundNormal = Vector3.up;
            leanAngle = 0f;

            SetPhysicsActive(true);
        }
        else
        {
            Park();
        }
    }

    public void Park()
    {
        moveInput = 0f;
        steerInput = 0f;
        isBraking = false;
        velocity = Vector3.zero;
        currentVelocityOffset = 0f;
        leanAngle = 0f;
        hopRequestedAt = float.NegativeInfinity;
        liftoffUntil = 0f;
        hopping = false;

        if (sphereRB != null)
        {
            sphereRB.linearVelocity = Vector3.zero;
            sphereRB.angularVelocity = Vector3.zero;
        }

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (freezeWhenUnridden)
            SetPhysicsActive(false);

        if (skidTrail != null)
            skidTrail.emitting = false;

        OnParked?.Invoke();
    }

    void SetPhysicsActive(bool active)
    {
        if (sphereRB != null)
        {
            sphereRB.isKinematic = !active;
            if (active) sphereRB.WakeUp();
        }

        if (body != null)
        {
            body.isKinematic = !active;
            if (active) body.WakeUp();
        }
    }

    public void SetInput(float move, float steer, bool brake)
    {
        moveInput = Mathf.Clamp(move, -1f, 1f);
        steerInput = Mathf.Clamp(steer, -1f, 1f);
        isBraking = brake;
    }
    public void Hop()
    {
        if (canHop && isControlled)
            hopRequestedAt = Time.time;
    }

    public Transform RiderAnchor => body != null ? body.transform : transform;

    public bool FollowHeading => false;

    public void ReadInput(PlayerInputContext input) =>
        SetInput(input.Move.y, input.Move.x, input.Brake);

    public bool OwnsCollider(Collider col)
    {
        Transform t = col.transform;

        return (sphereRB != null && t.IsChildOf(sphereRB.transform))
            || (body != null && t.IsChildOf(body.transform));
    }

    // Update is called once per frame
    void Update()
    {
        if(!isControlled) return;

        transform.position = sphereRB.transform.position;
    }

    private void FixedUpdate()
    {
        if(!isControlled) return;

        velocity = transform.InverseTransformDirection(sphereRB.linearVelocity);
        currentVelocityOffset = velocity.z / maxSpeed;

        Movement();
        SkidTrails();
    }

    void Movement()
    {
        isGrounded = Time.time >= liftoffUntil && Grounded();

        if (isGrounded && hopping)
        {
            hopping = false;
            nextHopTime = Time.time + hopCooldown;
        }

        TryHop();

        float authority = isGrounded ? 1f : airControl;

        if (!isBraking)
            Acceleration(authority);

        Steer(authority);
 
        if (isGrounded)
        {
            Brake();
            Grip();
            StepAssist();
            Settle();
        }
        else
            Gravity();

        BodyTilt();
    }

    void TryHop()
    {
        if (!isGrounded || hopping || Time.time < nextHopTime)
            return;

        if (Time.time - hopRequestedAt > hopBufferSeconds)
            return;

        hopRequestedAt = float.NegativeInfinity;

        float fall = gravity + (sphereRB.useGravity ? -Physics.gravity.y : 0f);
        float launch = Mathf.Sqrt(2f * Mathf.Max(fall, 0.01f) * Mathf.Max(hopHeight, 0f));

        Vector3 current = sphereRB.linearVelocity;
        sphereRB.linearVelocity = new Vector3(current.x, Mathf.Max(current.y, 0f) + launch, current.z);

        hopping = true;
        isGrounded = false;
        liftoffUntil = Time.time + hopLiftoffSeconds;

        if (hopSound != null)
            hopSound.Play();

        OnHop?.Invoke();
    }

    void Acceleration(float authority)
    {
        Vector3 current = sphereRB.linearVelocity;

        Vector3 planar = new Vector3(current.x, 0f, current.z);
        float commanded = moveInput < 0f ? moveInput * reverseFraction : moveInput;
        Vector3 target = maxSpeed * commanded * transform.forward;

        planar = Vector3.Lerp(planar, target, Time.fixedDeltaTime * acceleration * authority);

        sphereRB.linearVelocity = new Vector3(planar.x, current.y, planar.z);
    }

    void Steer(float authority)
    {
        float speed = Mathf.Abs(velocity.z);

        float geometricRate = speed / Mathf.Max(minTurnRadius, 0.01f);
        float gripRate = speed > 0.01f ? maxCorneringAccel / speed : 0f;
        float demand = Mathf.Abs(steerInput) * geometricRate;
        corneringLoad = gripRate > 0.01f ? demand / gripRate : 0f;

        float turnAmount = steerInput
            * Mathf.Sign(velocity.z)
            * Mathf.Min(geometricRate, gripRate) * Mathf.Rad2Deg
            * authority
            * Time.fixedDeltaTime;

        transform.Rotate(0f, turnAmount, 0f, Space.World);

        yawRate = turnAmount / Time.fixedDeltaTime;
    }

    void Brake()
    {
        if (!isBraking) return;

        Vector3 current = sphereRB.linearVelocity;
        Vector3 planar = new Vector3(current.x, 0f, current.z);

        planar = Vector3.MoveTowards(planar, Vector3.zero, brakingStrength * brakeDeceleration * Time.fixedDeltaTime);

        sphereRB.linearVelocity = new Vector3(planar.x, current.y, planar.z);
        
    }

    void Grip()
    {
        Vector3 current = sphereRB.linearVelocity;

        Vector3 lateral = Vector3.Project(new Vector3(current.x, 0f, current.z), transform.right);

        float scrubbed = 1f - Mathf.Exp(-gripStrength * Time.fixedDeltaTime);

        sphereRB.linearVelocity = current - lateral * scrubbed;
    }

    void Settle()
    {
        if (Vector3.Angle(groundNormal, Vector3.up) > 5f) return;

        Vector3 current = sphereRB.linearVelocity;
        if (current.y <= maxGroundedRise) return;

        sphereRB.linearVelocity = new Vector3(current.x, maxGroundedRise, current.z);
    }

    void StepAssist()
    {
        stepping = false;

        if (moveInput <= 0.01f)
        { 
            stepReason = "no throttle";
            return;
        }

        Vector3 centre = sphereRB.position;
        float feet = centre.y - wheelRadius;

        Vector3 ahead = centre + transform.forward * (wheelRadius + stepLookAhead);
        Vector3 probeTop = new Vector3(ahead.x, feet + maxStepHeight + 0.05f, ahead.z);
        float probeLength = maxStepHeight + 0.05f + wheelRadius;

        if (!Physics.Raycast(probeTop, Vector3.down, out RaycastHit ahead_hit, probeLength, groundLayer, QueryTriggerInteraction.Ignore))
        {
            groundAhead = float.NaN;
            stepReason = "nothing ahead";
            return;
        }

        groundAhead = ahead_hit.point.y;
        float step = groundAhead - feet;

        if (step <= minStepHeight)
        {
            stepReason = $"flat or down ({step:0.000})";
            return;
        }

        if (step > maxStepHeight)
        {
            stepReason = $"too tall ({step:0.000})";
            return;
        }    

        stepping = true;
        stepReason = $"climbing {step:0.000}";
        sphereRB.position += Vector3.up * Mathf.Min(step, stepClimbSpeed * Time.fixedDeltaTime);
    }

    bool Grounded()
    {
        Vector3 origin = sphereRB.transform.position;
        float probeDistance = wheelRadius + groundProbeSkin;

        bool foundGround = Physics.Raycast(
            origin,
            Vector3.down,
            out hit,
            probeDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore
        );

        if (!foundGround)
        {
            float castRadius = wheelRadius * 0.25f;

            foundGround = Physics.SphereCast(
                origin,
                castRadius,
                Vector3.down,
                out hit,
                probeDistance - castRadius,
                groundLayer,
                QueryTriggerInteraction.Ignore
            );
        }

        if (foundGround && (hit.normal.sqrMagnitude < 0.5f || Vector3.Angle(hit.normal, Vector3.up) > maxGroundAngle))
            foundGround = false;

        Vector3 targetNormal = foundGround ? hit.normal : Vector3.up;
        groundNormal = Vector3.Slerp(groundNormal, targetNormal, Sharpness(groundNormalSharpness, Time.fixedDeltaTime));

        return foundGround;
    }

    void Gravity()
    {
        sphereRB.AddForce(gravity * Vector3.down, ForceMode.Acceleration);
    }

    void SkidTrails()
    {
        if (skidTrail == null) return;

        bool sliding = corneringLoad > minSkidLoad;
        bool brakeLock = isBraking && Mathf.Abs(velocity.z) > 1.5f;

        skidTrail.emitting = isGrounded && (sliding || brakeLock);
    }

    void BodyTilt()
    {
        float corneringAccel = velocity.z * yawRate * Mathf.Deg2Rad;
        float leanTarget = -Mathf.Atan2(corneringAccel, Mathf.Abs(Physics.gravity.y)) * Mathf.Rad2Deg;

        leanTarget = Mathf.Clamp(leanTarget, -zTiltAngle, zTiltAngle);
        leanAngle = Mathf.Lerp(leanAngle, leanTarget, Sharpness(leanSharpness, Time.fixedDeltaTime));

        Quaternion yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Quaternion alignToGround = Quaternion.FromToRotation(Vector3.up, groundNormal);

        Quaternion target = alignToGround * yaw * Quaternion.Euler(0f, 0f, leanAngle);

        body.MoveRotation(
            Quaternion.Slerp(body.rotation, target, Sharpness(tiltSharpness, Time.fixedDeltaTime))
        );
    }

    static float Sharpness(float sharpness, float deltaTime) => 1f - Mathf.Exp(-sharpness * deltaTime);
}
