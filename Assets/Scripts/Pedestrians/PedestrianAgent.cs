using UnityEngine;

[DisallowMultipleComponent]
public class PedestrianAgent : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Left empty, the first Animator in children is used")]
    [SerializeField] private Animator animator;

    [Header("Animator")]
    [Tooltip("Speed is fed in metres per second, the same as the players' animators use")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string movingParameter = "isMoving";

    [Header("Walking")]
    [SerializeField, Min(0.1f)] private float walkSpeedMin = 1.1f;
    [SerializeField, Min(0.1f)] private float walkSpeedMax = 1.7f;
    [SerializeField, Min(0.1f)] private float acceleration = 6f;
    [SerializeField, Min(1f)] private float turnSpeed = 540f;

    [Tooltip("How fast a pedestrian settles on to the height of the pavement ahead")]
    [SerializeField, Min(0.1f)] private float heightCatchUp = 3f;

    [Tooltip("How far off the middle of the pavement band a pedestrian walks, so they do not" +
             "all tread the same line")]
    [SerializeField, Min(0f)] private float laneJitter = 0.4f;

    [SerializeField, Min(0.05f)] private float arriveDistance = 0.3f;

    [Header("Idling")]
    [SerializeField, Range(0f, 1f)] private float idleChanceAtNode = 0.08f;
    [SerializeField] private Vector2 idleSeconds = new Vector2(1.5f, 4f);

    [Header("Route choice")]
    [Tooltip("How much less likely stepping off the ker is than staying on the pavement")]
    [SerializeField, Range(0.01f, 1f)] private float crossingWeight = 0.15f;

    [Tooltip("How much less likely turning back the way they came is.")]
    [SerializeField, Range(0.01f, 1f)] private float backtrackWeight = 0.1f;

    [Header("Kerb check")]
    [Tooltip("Players and vehicles. A pedestrian waits rather than walking into one")]
    [SerializeField] private LayerMask trafficLayers = 0;
    [SerializeField, Min(0f)] private float kerbCheckRadius = 3.5f;
    [SerializeField] private Vector2 kerbWaitSeconds = new Vector2(0.4f, 1.2f);

    [Header("Puddles")]
    [Tooltip("Hurry out of a puddle instead of strolling through it")]
    [SerializeField] private bool hurryThroughPuddles = true;
    [SerializeField, Min(0.05f)] private float puddleCheckInterval = 0.4f;
    [SerializeField, Min(1f)] private float puddleSpeedBoost = 1.6f;

    private PedestrianGraph graph;

    private int currentNode = -1;
    private int previousNode = -1;
    private int targetNode = -1;
    private int heldAtKerb = -1;

    private Vector3 targetPosition;
    private Vector3 laneOffset;

    private float walkSpeed;
    private float currentSpeed;
    private float speedBoostUntil;
    private float waitUntil;
    private float nextPuddleCheck;

    private readonly Collider[] trafficHits = new Collider[8];

    private int speedHash;
    private int movingHash;

    public int CurrentNode => currentNode;
    public bool IsWalking => currentSpeed > 0.05f;
    public bool IsCrossing { get; private set; }

    void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        speedHash = Animator.StringToHash(speedParameter);
        movingHash = Animator.StringToHash(movingParameter);
    }

    public void Bind(PedestrianGraph pedestrianGraph, int startNode)
    {
        graph = pedestrianGraph;

        currentNode = startNode;
        previousNode = -1;
        targetNode = -1;
        heldAtKerb = -1;

        walkSpeed = Random.Range(
            Mathf.Min(walkSpeedMin, walkSpeedMax),
            Mathf.Max(walkSpeedMin, walkSpeedMax));

        laneOffset = NewLaneOffset();

        transform.position = graph.NodePosition(startNode) + laneOffset;

        PickNextTarget();
    }

    void Update()
    {
        if (graph == null || !graph.IsBuilt) return;

        if (Time.time < waitUntil)
        {
            Brake();
            return;
        }

        if (targetNode < 0)
        {
            PickNextTarget();

            if (targetNode < 0)
            {
                Brake();
                return;
            }    
        }

        Vector3 flatTarget = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);
        Vector3 toTarget = flatTarget - transform.position;

        if (toTarget.sqrMagnitude <= arriveDistance * arriveDistance)
        {
            ArriveAtTarget();
            return;
        }

        float top = Time.time < speedBoostUntil ? walkSpeed * puddleSpeedBoost : walkSpeed;

        currentSpeed = Mathf.MoveTowards(currentSpeed, top, acceleration * Time.deltaTime);

        Vector3 moved = Vector3.MoveTowards(transform.position, flatTarget, currentSpeed * Time.deltaTime);

        moved.y = Mathf.MoveTowards(transform.position.y, targetPosition.y, heightCatchUp * Time.deltaTime);

        transform.position = moved;

        Vector3 facing = toTarget;
        facing.y = 0f;

        if (facing.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(facing.normalized, Vector3.up),
                turnSpeed * Time.deltaTime);
        }

        UpdateAnimator();
        CheckPuddle();
    }

    void ArriveAtTarget()
    {
        previousNode = currentNode;
        currentNode = targetNode;
        targetNode = -1;
        IsCrossing = false;

        laneOffset = NewLaneOffset();

        if (Random.value < idleChanceAtNode)
        {
            waitUntil = Mathf.Max(waitUntil, Time.time + Random.Range(
                Mathf.Min(idleSeconds.x, idleSeconds.y),
                Mathf.Max(idleSeconds.x, idleSeconds.y)));
        }

        PickNextTarget();
    }

    void PickNextTarget()
    {
        if (graph == null || currentNode < 0) return;

        int next;
        bool crossing;

        if (heldAtKerb >= 0)
        {
            next = heldAtKerb;
            crossing = true;
        }
        else if (!graph.TryPickNext(currentNode, previousNode, crossingWeight, backtrackWeight,
                               out next, out crossing))
        {
            targetNode = -1;
            return;
        }

        if (crossing && (!LightAllowsCrossing(next) || TrafficNearby()))
        {
            heldAtKerb = next;

            waitUntil = Mathf.Max(waitUntil, Time.time + Random.Range(
                Mathf.Min(kerbWaitSeconds.x, kerbWaitSeconds.y),
                Mathf.Max(kerbWaitSeconds.x, kerbWaitSeconds.y)));

            targetNode = -1;
            return;
        }

        heldAtKerb = -1;
        targetNode = next;
        IsCrossing = crossing;

        targetPosition = graph.NodePosition(next) + (crossing ? Vector3.zero : laneOffset);
    }

    bool LightAllowsCrossing(int next)
    {
        TrafficManager traffic = TrafficManager.Instance;

        if (traffic == null) return true;
        if (!graph.TryGetEdge(currentNode, next, out PedestrianGraph.Edge edge) || edge.side < 0)
            return true;

        return traffic.PedestriansMayCross(graph.GetNode(currentNode).cell, edge.side);
    }

    bool TrafficNearby()
    {
        if (trafficLayers.value == 0 || kerbCheckRadius <= 0f) return false;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position + Vector3.up,
            kerbCheckRadius,
            trafficHits,
            trafficLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            TrafficCar car = trafficHits[i].GetComponentInParent<TrafficCar>();

            if (car != null && car.IsStopped) continue;

            return true;
        }

        return false;
    }

    void CheckPuddle()
    {
        if (!hurryThroughPuddles) return;
        if (Time.time < nextPuddleCheck) return;

        nextPuddleCheck = Time.time + puddleCheckInterval;

        if (PeePuddle.IsInsideAnyPuddle(transform.position))
            speedBoostUntil = Time.time + puddleCheckInterval * 2f;
    }

    void Brake()
    {
        currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, acceleration * 2f * Time.deltaTime);

        UpdateAnimator();
    }

    void UpdateAnimator()
    {
        if (animator == null) return;

        animator.SetFloat(speedHash, currentSpeed);
        animator.SetBool(movingHash, currentSpeed > 0.05f);
    }

    Vector3 NewLaneOffset()
    {
        if (laneJitter <= 0f) return Vector3.zero;

        return new Vector3(
            Random.Range(-laneJitter, laneJitter),
            0f,
            Random.Range(-laneJitter, laneJitter));
    }

    public void Stun(float seconds)
    {
        if (seconds <= 0f) return;

        waitUntil = Mathf.Max(waitUntil, Time.time + seconds);
        currentSpeed = 0f;

        UpdateAnimator();
    }

    public void TurnBack()
    {
        if (targetNode >= 0)
            previousNode = targetNode;

        targetNode = -1;
        heldAtKerb = -1;
        IsCrossing = false;
    }

    public void Resnap()
    {
        if (graph == null || !graph.TryNearestNode(transform.position, out int node)) return;

        currentNode = node;
        previousNode = -1;
        targetNode = -1;
        heldAtKerb = -1;

        PickNextTarget();
    }
}
