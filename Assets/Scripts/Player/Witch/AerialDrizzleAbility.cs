using UnityEngine;

[RequireComponent(typeof(MountableVehicle))]
public class AerialDrizzleAbility : MonoBehaviour
{
    [Header("Stream")]
    [Tooltip("The same Pee prefab used by the PeeSystem, carrying the same tag and collision messages")]
    [SerializeField] private GameObject peePrefab;

    [Tooltip("Empty child under the rider's seat where the stream spawns")]
    [SerializeField] private Transform emitPoint;

    [Tooltip("Looping pee sound on the broom")]
    [SerializeField] private AudioSource drizzleSound;

    [Tooltip("Share of the bar below which the drizzle gives out")]
    [SerializeField, Range(0f, 1f)] private float minPeeValue = 0.02f;

    [Tooltip("Speed the drops leave the broom at, straight down")]
    [SerializeField, Min(0f)] private float dropSpeed = 1.5f;
    [SerializeField, Min(0.1f)] private float dropGravity = 1f;

    [Tooltip("Soaking strength as a share of a ground stream")]
    [SerializeField, Range(0.05f, 1f)] private float soakMultiplier = 0.6f;

    [Header("Reach")]
    [Tooltip("What the drizzle lands on. Matches the Witch's PeePuddle ground mask")]
    [SerializeField] private LayerMask groundMask = Physics.DefaultRaycastLayers;

    [Tooltip("Highest the broom can fly and still drizzle")]
    [SerializeField, Min(1f)] private float maxHeight = 12f;

    [Tooltip("Its own PeeAimRing instance")]
    [SerializeField] private PeeAimRing landingRing;

    [Header("Soaking Pedestrians")]
    [SerializeField] private LayerMask pedestrianMask = 0;
    [SerializeField, Min(0.05f)] private float pedestrianRadius = 0.5f;
    [SerializeField, Range(0.01f, 1f)] private float weakestSoakingStream = 0.25f;

    private MountableVehicle vehicle;
    private ParticleSystem stream;
    private ParticleSystem.EmissionModule emission;
    private ParticleSystem.MainModule main;

    private PlayerInputHandler rider;
    private PlayerNeeds riderNeeds;
    private PeePuddle riderPuddle;

    private bool isDrizzling;
    private float currentFlow;

    public float SoakStrength => isDrizzling ? currentFlow * soakMultiplier : 0f;

    void Awake()
    {
        vehicle = GetComponent<MountableVehicle>();

        if (pedestrianMask.value == 0)
            pedestrianMask = LayerMask.GetMask("Pedestrian");
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (peePrefab == null || emitPoint == null)
        {
            Debug.LogError($"[AerialDrizzle] {name} needs a Pee Prefab and an Emit Point.", this);
            enabled = false;
            return;
        }

        GameObject instance = Instantiate(peePrefab, emitPoint.position, emitPoint.rotation, emitPoint);
        stream = instance.GetComponent<ParticleSystem>();
        stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        emission = stream.emission;
        main = stream.main;
    }

    void OnDisable()
    {
        StopDrizzle();
        HideRing();
    }

    // Update is called once per frame
    void Update()
    {
        if (!ResolveRider() || !MatchRunning())
        {
            StopDrizzle();
            HideRing();
            return;
        }

        bool hasPee = riderNeeds.maxPee > 0f && riderNeeds.pee > riderNeeds.maxPee * minPeeValue;

        RaycastHit ground = default;
        bool inReach = hasPee && !riderNeeds.IsHavingAccident && FindGround(out ground);

        if (rider.Controls != null && rider.Controls.Pee && !inReach)
        {
            bool anyHit = Physics.Raycast(emitPoint.position, Vector3.down, out RaycastHit below, 100f,
                                          ~0, QueryTriggerInteraction.Ignore);
        }

        if (!inReach)
        {
            StopDrizzle();
            HideRing();
            return;
        }

        PlayerInputContext input = rider.Controls;

        if (input != null && input.Pee)
        {
            riderNeeds.Relieve(Time.deltaTime);
            currentFlow = riderNeeds.pee / riderNeeds.maxPee * riderNeeds.FlowScale;

            StartDrizzle();
            UpdateVisuals(currentFlow, ground.distance);

            if (riderPuddle != null)
                riderPuddle.GrowAt(currentFlow, ground);

            SoakPedestrians(ground.distance);
        }
        else
        {
            StopDrizzle();
        }

        ShowRing(ground);
    }

    void LateUpdate()
    {
        if (emitPoint == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        emitPoint.rotation = Quaternion.LookRotation(Vector3.down, forward);
    }

    bool ResolveRider()
    {
        PlayerInputHandler current = vehicle.Rider;

        if (current != rider)
        {
            StopDrizzle();

            rider = current;
            riderNeeds = rider != null ? rider.GetComponent<PlayerNeeds>() : null;
            riderPuddle = rider != null ? rider.GetComponent<PeePuddle>() : null;
        }

        return rider != null && riderNeeds != null;
    }

    static bool MatchRunning()
    {
        if (MatchManager.instance != null && !MatchManager.instance.IsRunning) return false;

        return GameManager.instance == null || GameManager.instance.gameState != GameState.Pause;
    }

    bool FindGround(out RaycastHit ground)
    {
        Vector3 origin = emitPoint.position;
        float remaining = maxHeight;

        for (int i = 0; i < 4 && remaining > 0f; i++)
        {
            if (!Physics.Raycast(origin, Vector3.down, out ground, remaining, groundMask,
                                 QueryTriggerInteraction.Ignore))
                return false;

            if (!ground.collider.transform.IsChildOf(transform))
                return true;

            float step = ground.distance + 0.01f;
            origin += Vector3.down * step;
            remaining -= step;
        }

        ground = default;
        return false;
    }

    void UpdateVisuals(float flow, float height)
    {
        emission.rateOverTime = Mathf.Lerp(30f, 220f, flow);
        main.startSpeed = dropSpeed;
        main.gravityModifier = dropGravity;
        main.startSize = Mathf.Lerp(0.02f, 0.05f, flow);

        float g = Mathf.Abs(Physics.gravity.y) * dropGravity;
        float fall = g > 0f
            ? (-dropSpeed + Mathf.Sqrt(dropSpeed * dropSpeed + 2f * g * height)) / g
            : height / Mathf.Max(dropSpeed, 0.1f);

        main.startLifetime = fall + 0.25f;

        if (drizzleSound != null)
            drizzleSound.volume = Mathf.Lerp(0.2f, 1f, flow);
    }

    void StartDrizzle()
    {
        if (isDrizzling) return;

        isDrizzling = true;
        stream.Play();

        if (drizzleSound != null) drizzleSound.Play();
    }

    void StopDrizzle()
    {
        if (!isDrizzling) return;

        isDrizzling = false;
        currentFlow = 0f;

        if (stream != null) stream.Stop();
        if (drizzleSound != null) drizzleSound.Stop();
        if (riderPuddle != null) riderPuddle.EndPuddle();
    }

    void SoakPedestrians(float height)
    {
        if (pedestrianMask.value == 0) return;

        if (!Physics.SphereCast(emitPoint.position, pedestrianRadius, Vector3.down, out RaycastHit hit,
                                height, pedestrianMask, QueryTriggerInteraction.Collide))
            return;

        PedestrianReaction reaction = hit.collider.GetComponentInParent<PedestrianReaction>();

        if (reaction == null)
        {
            PedestrianAgent agent = hit.collider.GetComponentInParent<PedestrianAgent>();

            if (agent != null)
                reaction = agent.GetComponentInChildren<PedestrianReaction>();
        }

        if (reaction != null)
            reaction.SoakWithPee(rider.Owner, Mathf.Max(SoakStrength, weakestSoakingStream));
    }

    void ShowRing(RaycastHit ground)
    {
        if (landingRing == null) return;

        PlayersCameraController rig = rider.CameraController;
        Vector3 viewer = rig != null ? rig.transform.position : emitPoint.position;

        landingRing.Place(ground.point, ground.normal, riderNeeds.pee / riderNeeds.maxPee, viewer, isDrizzling);
    }

    void HideRing()
    {
        if (landingRing != null)
            landingRing.Hide();
    }
}
