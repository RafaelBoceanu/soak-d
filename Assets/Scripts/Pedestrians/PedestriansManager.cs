using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PedestriansManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralMapManager mapManager;

    [Tooltip("Prefabs with a PedestrianAgent on the root")]
    [SerializeField] private GameObject[] pedestrianPrefabs;

    [Tooltip("Parent the pedestrians are kept under")]
    [SerializeField] private Transform pedestriansParent;

    [Header("How many")]
    [SerializeField, Min(0)] private int pedestrianCount = 60;

    [Tooltip("Chance a spot near the town centre is taken")]
    [SerializeField, Range(0f, 1f)] private float centreDensity = 1f;

    [Tooltip("Chance a spot out at the edge of the map is taken")]
    [SerializeField, Range(0f, 1f)] private float edgeDensity = 0.25f;

    [Tooltip("Shortest gap allowed between two pedestrians at spawn to avoid huddles")]
    [SerializeField, Min(0f)] private float minSpacing = 8f;

    [SerializeField, Min(1)] private int attemptsPerPedestrian = 32;

    [Header("Activation")]
    [Tooltip("Drag the Boy and Witch here")]
    [SerializeField] private Transform[] focusTargets;

    [Tooltip("Pedestrians furthet than this from both players stop updating")]
    [SerializeField, Min(5f)] private float activationRadius = 70f;

    [Tooltip("Extra distance before an awake pedestrian is put back to sleep to avoid flickering")]
    [SerializeField, Min(0f)] private float activationHysteresis = 8f;

    [SerializeField, Min(0.05f)] private float activationInterval = 0.25f;

    [Header("Recycling")]
    [Tooltip("Move a sleeping pedestrian to a steet near a player instead of leaving them " +
             "standing where they were.")]
    [SerializeField] private bool recycleToPlayers = true;

    [Tooltip("Closest a recycled pedestrian may be dropped so none appear out of thin air.")]
    [SerializeField, Min(0f)] private float recycleMinDistance = 35f;

    [Tooltip("How many sleeping pedestrians may be moved per check, so a long walk does not " +
             "relocate the whole crowd in one frame.")]
    [SerializeField, Min(1)] private int recyclesPerCheck = 4;

    [Header("Ground")]
    [Tooltip("What the graph is allowed to stand on: the pavement, the road, the ground")]
    [SerializeField] private LayerMask groundLayers = 0;

    [SerializeField, Min(0.1f)] private float groundProbeHeight = 5f;
    [SerializeField] private float groundOffset = 0.02f;

    [Header("Debug")]
    [SerializeField] private bool drawGraphGizmos = false;
    [SerializeField, Min(0f)] private float gizmoRange = 60f;

    private readonly PedestrianGraph graph = new PedestrianGraph();
    private readonly List<PedestrianAgent> pedestrians = new List<PedestrianAgent>();
    private readonly List<Vector3> takenSpots = new List<Vector3>();
    private readonly List<int> nearbyNodes = new List<int>();
    private bool warnedNoNearbyStreets;

    private float nextActivationCheck;

    public PedestrianGraph Graph => graph;
    public int PedestrianCount => pedestrians.Count;

    void OnEnable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated += HandleCityGenerated;
        else
            Debug.LogError("[PedestriansManager] No map manager assigned; nobody will be out walking.", this);
    }

    void OnDisable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated -= HandleCityGenerated;
    }

    void HandleCityGenerated(IReadOnlyList<Transform> houses)
    {
        StartCoroutine(PopulateOncePhysicsHasCaughtUp());
    }

    IEnumerator PopulateOncePhysicsHasCaughtUp()
    {
        yield return new WaitForFixedUpdate();

        Physics.SyncTransforms();

        graph.Build(mapManager, groundLayers, groundProbeHeight, groundOffset);

        if (!graph.IsBuilt)
        {
            Debug.LogError(
                "[PedestriansManager] The pavement graph came out empty. The town has no roads, " +
                "or this ran before the map was generated", this);

            yield break;
        }

        Debug.Log($"[PedestriansManager] Pavement graph: {graph.NodeCount} nodes, {graph.EdgeCount} edges");

        if (graph.GroundProbeMisses > 0)
        {
            Debug.LogWarning(
                $"[PedestriansManager] {graph.GroundProbeMisses} graph node(s) found nothing solid " +
                $"underneath and were stood at the pavement height instead. Check that Ground " +
                $"Layers covers the layer the road tiles and the ground plane are on.", this);
        }

        ResolveFocusTargets();
        Populate(pedestrianCount);
        UpdateActivation(pedestrianCount);
    }

    #region Spawning
    void Populate(int count)
    {
        if (pedestrianPrefabs == null || pedestrianPrefabs.Length == 0)
        {
            Debug.LogError("[PedestriansManager] No pedestrian prefabs assigned.", this);
            return;
        }

        takenSpots.Clear();

        int placed = 0;

        for (int i = 0; i < count; i++)
        {
            if (!TryFindNode(out int node)) break;
            if (!SpawnPedestrian(node)) continue;

            placed++;
        }

        Debug.Log($"[PedestriansManager] Pedestrians out walking: {placed}/{count}");

        if (placed < count)
        {
            Debug.LogWarning(
                $"[PedestriansManager] Only {placed} of {count} pedestrians found a spot. Lower " +
                $"Min Spacing, raise Edge Density, or ask for fewer of them.", this);
        }
    }

    bool TryFindNode(out int node)
    {
        node = -1;

        Vector2 centre = new Vector2(
            mapManager.width * 0.5f * mapManager.cellSize,
            mapManager.height * 0.5f * mapManager.cellSize);

        float maxDistance = centre.magnitude;

        for (int attempt = 0; attempt < attemptsPerPedestrian; attempt++)
        {
            int candidate = graph.RandomNode();

            if (candidate < 0) return false;

            Vector3 position = graph.NodePosition(candidate);

            float t = maxDistance <= 0f
                ? 0f
                : Mathf.Clamp01(Vector2.Distance(new Vector2(position.x, position.z), centre) / maxDistance);

            if (Random.value > Mathf.Lerp(centreDensity, edgeDensity, t)) continue;
            if (IsCrowded(position)) continue;

            node = candidate;
            return true;
        }

        return false;
    }

    bool IsCrowded(Vector3 position)
    {
        if (minSpacing <= 0f) return false;

        float sqrSpacing = minSpacing * minSpacing;

        for (int i = 0; i < takenSpots.Count; i++)
        {
            if ((takenSpots[i] - position).sqrMagnitude < sqrSpacing) return true;
        }

        return false;
    }

    bool SpawnPedestrian(int node)
    {
        GameObject prefab = pedestrianPrefabs[Random.Range(0, pedestrianPrefabs.Length)];

        if (prefab == null) return false;

        Vector3 position = graph.NodePosition(node);

        GameObject instance = Instantiate(
            prefab,
            position,
            Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
            pedestriansParent);

        PedestrianAgent agent = instance.GetComponent<PedestrianAgent>();

        if (agent == null)
        {
            Debug.LogError($"[PedestriansManager] {prefab.name} has no PedestrianAgent component.", instance);

            Destroy(instance);
            return false;
        }

        agent.Bind(graph, node);

        pedestrians.Add(agent);
        takenSpots.Add(position);

        return true;
    }

    void ResolveFocusTargets()
    {
        if (focusTargets != null && focusTargets.Length > 0) return;

        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);

        focusTargets = new Transform[players.Length];

        for (int i = 0; i < players.Length; i++)
            focusTargets[i] = players[i].transform;

        if (focusTargets.Length == 0)
        {
            Debug.LogWarning(
                $"[PedestriansManager] No focus targets found, so every pedestrian stays awake. " +
                "Drag the Boy and the Witch into Focus Targets.", this);
        }
    }
    #endregion

    #region Activation
    // Update is called once per frame
    void Update()
    {
        if (pedestrians.Count == 0) return;

        if (Time.unscaledTime < nextActivationCheck) return;

        nextActivationCheck = Time.unscaledTime + activationInterval;

        UpdateActivation(recyclesPerCheck);
    }

    void UpdateActivation(int recycleBudget)
    { 
        float sqrOn = activationRadius * activationRadius;
        float wake = activationRadius + activationHysteresis;
        float sqrOff = wake * wake;

        int recycled = 0;

        for (int i = pedestrians.Count - 1; i >= 0; i--)
        {
            PedestrianAgent agent = pedestrians[i];

            if (agent == null)
            {
                pedestrians.RemoveAt(i);
                continue;
            }

            float sqrDistance = NearestFocusSqrDistance(agent.transform.position);
            
            if (!agent.gameObject.activeSelf)
            {
                if (sqrDistance <= sqrOn)
                    agent.gameObject.SetActive(true);
                else if (recycled < recycleBudget && TryRecycle(agent))
                    recycled++;

                continue;
            }

            if (sqrDistance > sqrOff)
                agent.gameObject.SetActive(false);
        }
    }

    bool TryRecycle(PedestrianAgent agent)
    {
        if (!recycleToPlayers) return false;
        if (focusTargets == null || focusTargets.Length == 0) return false;

        Transform focus = focusTargets[Random.Range(0, focusTargets.Length)];

        if (focus == null) return false;

        graph.CollectNodesNear(focus.position, activationRadius, nearbyNodes);

        if (nearbyNodes.Count == 0)
        {
            if (!warnedNoNearbyStreets)
            {
                warnedNoNearbyStreets = true;

                Debug.LogWarning(
                    $"[PedestriansManager] No pavement within {activationRadius} of " +
                    $"'{focus.name}', so there is nowhere to put a pedestrian. The players start " +
                    $"outside the procedural grid, which runs 0 to " +
                    $"{mapManager.width * mapManager.cellSize}. Raise Activation Radius, or " +
                    $"accept an empty street until they reach the town.", this);
            }

            return false;
        }

        float sqrMin = recycleMinDistance * recycleMinDistance;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            int node = nearbyNodes[Random.Range(0, nearbyNodes.Count)];
            Vector3 position = graph.NodePosition(node);

            if (TooCloseToAnyFocus(position, sqrMin)) continue;

            agent.gameObject.SetActive(true);
            agent.Bind(graph, node);

            return true;
        }

        return false;
    }

    bool TooCloseToAnyFocus(Vector3 position, float sqrMin)
    {
        foreach (Transform target in focusTargets)
        {
            if (target == null) continue;

            Vector3 offset = target.position - position;
            offset.y = 0f;

            if (offset.sqrMagnitude < sqrMin) return true;
        }

        return false;
    }

    float NearestFocusSqrDistance(Vector3 position)
    {
        if (focusTargets == null || focusTargets.Length == 0) return 0f;

        float best = float.MaxValue;
        
        foreach (Transform target in focusTargets)
        {
            if (target == null) continue;

            float sqrDistance = (target.position - position).sqrMagnitude;

            if (sqrDistance < best) best = sqrDistance;
        }

        return best == float.MaxValue ? 0f : best;
    }
    #endregion

    [ContextMenu("Repopulate")]
    public void Repopulate()
    {
        for (int i = pedestrians.Count - 1; i >= 0; i--)
        {
            if (pedestrians[i] != null)
                Destroy(pedestrians[i].gameObject);
        }

        pedestrians.Clear();

        if (!graph.IsBuilt)
            graph.Build(mapManager, groundLayers, groundProbeHeight, groundOffset);

        ResolveFocusTargets();
        Populate(pedestrianCount);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGraphGizmos || !Application.isPlaying || !graph.IsBuilt) return;

        Vector3 eye = focusTargets != null && focusTargets.Length > 0 && focusTargets[0] != null
            ? focusTargets[0].position
            : transform.position;

        float sqrRange = gizmoRange * gizmoRange;

        for (int i = 0; i < graph.NodeCount; i++)
        {
            PedestrianGraph.Node node = graph.GetNode(i);

            if ((node.position - eye).sqrMagnitude > sqrRange) continue;

            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(node.position, 0.15f);

            for (int e = 0; e < node.edgeCount; e++)
            {
                PedestrianGraph.Edge edge = graph.GetEdge(node.firstEdge + e);

                if (edge.to < i) continue;

                Gizmos.color = edge.isCrossing ? Color.yellow : Color.green;
                Gizmos.DrawLine(node.position, graph.NodePosition(edge.to));
            }
        }
    }
}
