using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TrafficCar : MonoBehaviour
{
    [Header("Driving")]
    [SerializeField, Min(0.5f)] private float cruiseSpeedMin = 7f;
    [SerializeField, Min(0.5f)] private float cruiseSpeedMax = 10f;
    [SerializeField, Min(0.1f)] private float acceleration = 4f;
    [SerializeField, Min(0.1f)] private float braking = 9f;

    [Tooltip("Speed through a bend or a turn at a junction")]
    [SerializeField, Min(0.5f)] private float turnSpeed = 4.5f;

    [SerializeField, Min(1f)] private float steerSpeed = 360f;

    [Header("Size")]
    [Tooltip("Distance from the pivot to the front bumper")]
    [SerializeField, Min(0f)] private float frontOffset = 2f;

    [Tooltip("Gap left betweern the front bumper and the stop line")]
    [SerializeField, Min(0f)] private float stopLineGap = 0.5f;

    [Tooltip("Gap left between the front bumper and whatever is ahead")]
    [SerializeField, Min(0f)] private float followGap = 2f;

    [Header("Looking ahead")]
    [Tooltip("Cars, players and pedestrians. Include the car layer itself so queues form")]
    [SerializeField] private LayerMask obstacleLayers = 0;

    [SerializeField, Min(0.1f)] private float castRadius = 0.8f;
    [SerializeField, Min(0f)] private float castHeight = 0.8f;
    [SerializeField, Min(0f)] private float extraLookAhead = 4f;

    private TrafficManager traffic;

    private readonly List<Vector3> path = new List<Vector3>();
    private readonly List<Vector2Int> reserved = new List<Vector2Int>();
    private readonly RaycastHit[] hits = new RaycastHit[8];

    private int pathIndex;

    private Vector2Int cell;
    private Vector2Int heading;
    private Vector2Int exitDir;
    private Vector2Int nextExitDir;

    private float cruiseSpeed;
    private float speed;
    private bool blockedAtLine;

    public float Speed => speed;
    public bool IsStopped => speed < 0.3f;
    public Vector2Int Cell => cell;

    public void Bind(TrafficManager manager, Vector2Int startCell, Vector2Int startHeading)
    {
        ReleaseAll();

        traffic = manager;
        cell = startCell;
        heading = startHeading;

        exitDir = traffic.ChooseExit(cell, heading);
        nextExitDir = traffic.ChooseExit(cell + exitDir, exitDir);

        cruiseSpeed = Random.Range(
            Mathf.Min(cruiseSpeedMin, cruiseSpeedMax),
            Mathf.Max(cruiseSpeedMin, cruiseSpeedMax));

        speed = cruiseSpeed * 0.5f;

        traffic.BuildCellPath(cell, heading, exitDir, path);
        pathIndex = 0;

        transform.SetPositionAndRotation(
            path[0],
            Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y)));
    }

    void OnDisable()
    {
        ReleaseAll();        
    }

    // Update is called once per frame
    void Update()
    {
        if (traffic == null || path.Count < 2) return;

        float remaining = RemainingInCell();
        float target = cruiseSpeed;

        if (exitDir != heading)
            target = Mathf.Min(target, turnSpeed);

        if (nextExitDir != exitDir)
            target = Mathf.Min(target, ApproachSpeed(turnSpeed, remaining));

        blockedAtLine = !MayEnter(cell + exitDir, remaining);

        if (blockedAtLine)
            target = Mathf.Min(target, ApproachSpeed(0f, remaining - frontOffset - stopLineGap));

        if (TryFindObstacle(out float gap))
            target = Mathf.Min(target, ApproachSpeed(0f, gap - followGap));

        float rate = target < speed ? braking : acceleration;

        speed = Mathf.MoveTowards(speed, target, rate * Time.deltaTime);

        Drive(speed * Time.deltaTime);
        Face();
    }

    float ApproachSpeed(float endSpeed, float distance)
    {
        return Mathf.Sqrt(endSpeed * endSpeed + 2f * braking * Mathf.Max(distance, 0f));
    }

    bool MayEnter(Vector2Int next, float remaining)
    {
        if (!traffic.NeedsReservation(next)) return true;
        if (reserved.Contains(next)) return true;

        float toLine = remaining - frontOffset - stopLineGap;
        float stoppingDistance = speed * speed / (2f * braking);

        if (toLine > stoppingDistance + 1f) return true;

        if (traffic.TryGetLight(next, out TrafficLight light) &&
            light.SignalFor(TrafficManager.SideOf(exitDir)) != TrafficLight.Signal.Green)
            return false;

        if (!traffic.TryReserve(next, this)) return false;

        reserved.Add(next);
        return true;
    }

    bool TryFindObstacle(out float gap)
    {
        gap = float.MaxValue;

        if (obstacleLayers.value == 0) return false;

        Vector3 forward = transform.forward;
        Vector3 origin = transform.position + Vector3.up * castHeight;

        float range = frontOffset + followGap + speed * speed / (2f * braking) + extraLookAhead;

        int count = Physics.SphereCastNonAlloc(
            origin, castRadius, forward, hits, range, obstacleLayers, QueryTriggerInteraction.Ignore);

        bool found = false;

        for (int i = 0; i < count; i++)
        {
            Collider other = hits[i].collider;

            if (other == null || other.transform.IsChildOf(transform)) continue;
            
            TrafficCar car = other.GetComponentInParent<TrafficCar>();

            if (car != null)
            {
                Transform body = car.transform;

                if (Vector3.Dot(body.forward, forward) < 0.3f) continue;

                if (Vector3.Dot(body.position - transform.position, forward) <= 0f) continue;
            }

            float distance = hits[i].distance + castRadius - frontOffset;

            if (distance < gap) gap = distance;

            found = true;
        }

        return found;
    }

    void Drive(float distance)
    {
        while (distance > 0f)
        {
            if (pathIndex >= path.Count - 1)
            {
                if (blockedAtLine)
                {
                    speed = 0f;
                    return;
                }

                EnterNextCell();
            }

            Vector3 position = transform.position;
            Vector3 to = path[pathIndex + 1];

            float leg = Vector3.Distance(position, to);

            if (leg > distance)
            {
                transform.position = Vector3.MoveTowards(position, to, distance);
                return;
            }

            transform.position = to;
            distance -= leg;
            pathIndex++;
        }
    }

    void EnterNextCell()
    {
        cell += exitDir;
        heading = exitDir;
        exitDir = nextExitDir;
        nextExitDir = traffic.ChooseExit(cell + exitDir, exitDir);

        ReleaseAllBut(cell);

        traffic.BuildCellPath(cell, heading, exitDir, path);
        pathIndex = 0;
    }

    float RemainingInCell()
    {
        if (pathIndex >= path.Count - 1) return 0f;

        float total = Vector3.Distance(transform.position, path[pathIndex + 1]);

        for (int i = pathIndex + 1; i < path.Count - 1; i++)
            total += Vector3.Distance(path[i], path[i + 1]);

        return total;
    }

    void Face()
    {
        Vector3 ahead = path[Mathf.Min(pathIndex + 1, path.Count - 1)] - transform.position;
        ahead.y = 0f;

        if (ahead.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(ahead.normalized, Vector3.up),
            steerSpeed * Time.deltaTime);
    }

    void ReleaseAllBut(Vector2Int keep)
    {
        for (int i = reserved.Count - 1; i >= 0; i--)
        {
            if (reserved[i] == keep) continue;

            if (traffic != null) traffic.Release(reserved[i], this);

            reserved.RemoveAt(i);
        }
    }

    void ReleaseAll()
    {
        if (traffic != null)
        {
            foreach (Vector2Int held in reserved)
                traffic.Release(held, this);
        }

        reserved.Clear();
    }
}
