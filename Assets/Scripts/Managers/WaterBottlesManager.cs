using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaterBottlesManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ProceduralMapManager mapManager;

    [Tooltip("Prefab with an InventoryPickup set to water bottle")]
    [SerializeField] private GameObject bottlePrefab;

    [Tooltip("Parent the scattered bottles are kept under. If left empty they go to the scene root")]
    [SerializeField] private Transform bottlesParent;

    [Header("How many")]
    [Tooltip("Bottles lying around the city at any one time")]
    [SerializeField, Min(0)] private int bottlesInCity = 30;

    [Tooltip("Drop a replacement somewhere else once a bottle is taken. Leave it clear when " +
             "the prefab already comes back on its own, or the city fills up with bottles")]
    [SerializeField] private bool respawnElsewhere = true;

    [SerializeField, Min(0f)] private float respawnDelayMin = 15f;
    [SerializeField, Min(0f)] private float respawnDelayMax = 30f;

    [Header("Where")]
    [Tooltip("Distance from the centre of the road out to the kerb where bottles are dropped " +
             "Must be kept under half a cell or they land in the gardens")]
    [SerializeField] private float kerbOffset = 3f;

    [Tooltip("How far a bottle may wander from its kerb spot, so they do not sit in a neat grid")]
    [SerializeField, Min(0f)] private float spotJitter = 1.5f;

    [Tooltip("Shortest gap allowed between two bottles, so a single street is never a free refill")]
    [SerializeField] private float minSpacing = 12f;

    [Tooltip("Extra spots besides the streets, for the hand built part of the map")]
    [SerializeField] private Transform[] extraSpawnPoints;

    [Header("Ground")]
    [Tooltip("What a bottle is allowed to come to rest on: the road, the pavement, the ground")]
    [SerializeField] private LayerMask groundLayers = 0;

    [Tooltip("How far above the spot the search for that surface starts")]
    [SerializeField] private float groundProbeHeight = 5f;

    [Tooltip("How far above the surface the bottle is placed")]
    [SerializeField] private float groundOffset = 0.1f;

    [Tooltip("Stad the botthe at the map ground height when the probe fins no surface at all")]
    [SerializeField] private bool fallBackToMapGround = true;

    [Tooltip("Height of the top of a road tile above the map ground. Used only when the probe " +
         "finds nothing solid. The tiles are 0.3 thick and stand on the ground, so half of " +
         "that clears them.")]
    [SerializeField] private float fallbackSurfaceHeight = 0.15f;

    [Tooltip("Stand the bottle on the surface using its own size, so a model whose pivot sits " +
             "in its middle does not sink into the road.")]
    [SerializeField] private bool standOnGround = true;

    [Header("Clearance")]
    [Tooltip("Anything on these layers blocks a spawh: houses, fences, props. If left empty the check is skipped")]
    [SerializeField] private LayerMask obstacleLayers = 0;

    [SerializeField, Min(0f)] private float clearanceRadius = 0.5f;

    [Tooltip("Spots tried before one bottle gives up")]
    [SerializeField, Min(1)] private int attemptsPerBottle = 24;

    static readonly Vector3[] kerbDirections =
    {
        Vector3.left, Vector3.right, Vector3.forward, Vector3.back
    };

    readonly List<Vector3> candidates = new List<Vector3>();
    readonly List<InventoryPickup> active = new List<InventoryPickup>();
    int groundProbeMisses;

    public int BottlesInCity => active.Count;

    void OnEnable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated += HandleCityGenerated;
    }

    void HandleCityGenerated(IReadOnlyList<Transform> houses)
    {
        StartCoroutine(ScatterOncePhysicsHasCaughtUp());
    }

    IEnumerator ScatterOncePhysicsHasCaughtUp()
    {
        yield return new WaitForFixedUpdate();

        Physics.SyncTransforms();

        BuildCandidates();
        Scatter(bottlesInCity);
    }

    #region Spots
    void BuildCandidates()
    {
        candidates.Clear();

        if (mapManager != null)
        {
            foreach (Vector2Int cell in mapManager.GetRoadCells())
            {
                Vector3 centre = mapManager.RoadCellToWorld(cell);

                foreach (Vector3 kerb in kerbDirections)
                    candidates.Add(centre + kerb * kerbOffset);
            }
        }

        if (extraSpawnPoints != null)
        {
            foreach (Transform point in extraSpawnPoints)
            {
                if (point != null)
                    candidates.Add(point.position);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogError(
                "[WaterBottlesManager] No road cells and no extra spawn points, so there is " +
                "nowhere to leave a bottle.", this);
        }
    }

    bool TryFindSpot(out Vector3 spot)
    {
        spot = Vector3.zero;

        if (candidates.Count == 0) return false;

        for (int attempt = 0; attempt < attemptsPerBottle; attempt++)
        {
            Vector3 guess = candidates[Random.Range(0, candidates.Count)] + Jitter();

            if (!TryGroundPoint(guess, out Vector3 grounded)) continue;
            if (IsBlocked(grounded)) continue;
            if (IsCrowded(grounded)) continue;

            spot = grounded;
            return true;
        }

        return false;
    }

    Vector3 Jitter()
    {
        if (spotJitter <= 0f) return Vector3.zero;

        return new Vector3(
            Random.Range(-spotJitter, spotJitter),
            0f,
            Random.Range(-spotJitter, spotJitter));
    }

    bool TryGroundPoint(Vector3 guess, out Vector3 grounded)
    {
        float probe = groundProbeHeight > 0f ? groundProbeHeight : 5f;
        int layers = groundLayers.value != 0 ? groundLayers.value : Physics.DefaultRaycastLayers;

        Vector3 origin = guess + Vector3.up * probe;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probe * 2f,
                            layers, QueryTriggerInteraction.Ignore))
        {
            grounded = hit.point + Vector3.up * groundOffset;
            return true;
        }

        groundProbeMisses++;

        if (fallBackToMapGround && mapManager != null)
        {
            grounded = new Vector3(
                guess.x, 
                mapManager.groundY + groundOffset, 
                guess.z);
            return true;
        }

        grounded = guess;
        return false;
    }

    bool IsBlocked(Vector3 point)
    {
        if (obstacleLayers.value == 0 || clearanceRadius <= 0f) return false;

        Vector3 centre = point + Vector3.up * (clearanceRadius + 0.05f);

        return Physics.CheckSphere(centre, clearanceRadius, obstacleLayers, QueryTriggerInteraction.Ignore);
    }

    bool IsCrowded(Vector3 point)
    {
        if (minSpacing <= 0f) return false;

        float sqrSpacing = minSpacing * minSpacing;

        for (int i = active.Count - 1; i >= 0; i--)
        {
            InventoryPickup bottle = active[i];

            if (bottle == null)
            {
                active.RemoveAt(i);
                continue;
            }

            if ((bottle.transform.position - point).sqrMagnitude < sqrSpacing)
                return true;
        }

        return false;
    }
    #endregion

    #region Spawning
    void Scatter(int count)
    {
        if (bottlePrefab == null)
        {
            Debug.LogError("[WaterBottlesManager] No bottle prefab assigned.", this);
            return;
        }

        groundProbeMisses = 0;

        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            if (!TrySpawnOne()) break;

            spawned++;
        }

        Debug.Log($"[WaterBottlesManager] Water bottles left around the city: {spawned}/{count}");

        if (spawned < count)
        {
            Debug.LogWarning(
                $"[WaterBottlesManager] Only {spawned} of {count} bottles found a spot. Lower " +
                $"Min Spacing, widen Ground Layers, or shrink Clearance Radius", this);
        }

        if (groundProbeMisses > 0)
        {
            Debug.LogWarning(
                $"[WaterBottlesManager] {groundProbeMisses} spot(s) has nothing solid underneath," +
                $"so those bottles were stood at fallback height. The road tiles are probably" +
                $"missing a collider, or Ground Layers does not cover the layer they are on", this);
        }
    }

    bool TrySpawnOne()
    {
        if (!TryFindSpot(out Vector3 spot))
            return false;

        return SpawnBottle(spot);
    }

    bool SpawnBottle(Vector3 spot)
    {
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject bottle = Instantiate(bottlePrefab, spot, rotation, bottlesParent);

        InventoryPickup pickup = bottle.GetComponent<InventoryPickup>();

        if (pickup == null)
        {
            Debug.LogError($"[WatterBottlesManager] {bottlePrefab.name} has no InventoryPickup component", bottle);

            Destroy(bottle);
            return false;
        }

        if (pickup.Item != InventoryItemType.WaterBottle)
        {
            Debug.LogWarning(
                $"[WaterBottlesManager] {bottlePrefab.name} hands out {pickup.Item}, not water bottles", bottle);
        }

        if (standOnGround)
            StandOnSurface(bottle, spot.y);

        pickup.OnPickedUp += HandleBottlePickedUp;

        active.Add(pickup);
        
        return true;
    }

    void StandOnSurface(GameObject bottle, float baseHeight)
    {
        Renderer[] renderers = bottle.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        bottle.transform.position += Vector3.up * (baseHeight - bounds.min.y);
    }

    void HandleBottlePickedUp(InventoryPickup bottle, OwnerType collector)
    {
        if (!bottle.DestroysOnPickup) return;

        bottle.OnPickedUp -= HandleBottlePickedUp;

        active.Remove(bottle);

        if (!respawnElsewhere || !isActiveAndEnabled) return;

        StartCoroutine(RespawnLater());
    }

    IEnumerator RespawnLater()
    {
        float min = Mathf.Min(respawnDelayMin, respawnDelayMax);
        float max = Mathf.Max(respawnDelayMin, respawnDelayMax);

        yield return new WaitForSeconds(Random.Range(min, max));

        if (active.Count < bottlesInCity)
            TrySpawnOne();
    }

    [ContextMenu("Rescatter")]
    public void Rescatter()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (active[i] != null)
                Destroy(active[i].gameObject);
        }

        active.Clear();

        if (candidates.Count == 0)
            BuildCandidates();

        Scatter(bottlesInCity);
    }
    #endregion
}
