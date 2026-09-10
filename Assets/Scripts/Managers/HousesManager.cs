using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HousesManager : MonoBehaviour
{
    [SerializeField] private ProceduralMapManager mapManager;

    [Header("Prefabs")]
    [SerializeField] private GameObject deliveryZoneBoyPrefab;
    [SerializeField] private GameObject deliveryZoneWitchPrefab;

    [Header("Zone Settings")]
    [SerializeField] private int boyZonesCount = 5;
    [SerializeField] private int witchZonesCount = 5;

    [Tooltip("Child object on a house that marks the street-facing spot where a zone sits.")]
    [SerializeField] private string frontMarkerName = "Front";

    [Tooltip("How far out from the front marker the delivery zone sits")]
    [SerializeField] private float zoneForwardOffset = 0.8f;

    [Tooltip("How far above the front marker the zone is placed.")]
    [SerializeField] private float zoneHeightOffset = 0.05f;

    [Tooltip("What the zone is allowed t ocome to rest on: the pavement, the road, the ground.")]
    [SerializeField] private LayerMask zoneGroundLayers = -0;

    [Tooltip("How far above the zone the search for that surface starts.")]
    [SerializeField] private float zoneGroundProbeHeight = 5f;

    [Header("Extra Houses")]
    [Tooltip("Optional roots whose active children join the procedural houses as zone candidates.")]
    [SerializeField] private Transform[] additionalHouseParents;

    void OnEnable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated += GenerateZones;
        else
            Debug.LogError("No map manager assigned; delivery zones will not be generated.");
    }

    void OnDisable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated -= GenerateZones;
    }

    void GenerateZones(IReadOnlyList<Transform> houses)
    {
        StartCoroutine(GenerateZonesOncePhysicsHasCaughtUp(houses));
    }

    IEnumerator GenerateZonesOncePhysicsHasCaughtUp(IReadOnlyList<Transform> houses)
    {
        yield return new WaitForFixedUpdate();

        Physics.SyncTransforms();

        PlaceZones(houses);
    }

    void PlaceZones(IReadOnlyList<Transform> houses)
    {
        ValidateZonePrefab(deliveryZoneBoyPrefab, nameof(deliveryZoneBoyPrefab));
        ValidateZonePrefab(deliveryZoneWitchPrefab, nameof(deliveryZoneWitchPrefab));

        List<Transform> candidates = CollectCandidates(houses, out int rejected);

        if (candidates.Count == 0)
        {
            Debug.LogError(
                $"None of the {rejected} house(s) checked have a '{frontMarkerName}' object anywhere in " +
                $"their hierarchy; no delivery zones spawned. Add that child to the house prefabs, or set " +
                $"Front Marker Name to whatever the marker is actually called.", this);
            return;
        }

        if (rejected > 0)
            Debug.LogWarning($"{rejected} house(s) skipped: no '{frontMarkerName}' object found.", this);

        Debug.Log($"Houses eligible for delivery zones: {candidates.Count}");

        Shuffle(candidates);

        int boyTarget = Mathf.Max(0, boyZonesCount);
        int witchTarget = Mathf.Max(0, witchZonesCount);

        int boyPlaced = 0;
        int witchPlaced = 0;
        int index = 0;

        while (index < candidates.Count && (boyPlaced < boyTarget || witchPlaced < witchTarget))
        {
            bool placeBoy = boyPlaced < boyTarget && (witchPlaced >= witchTarget || boyPlaced <= witchPlaced);

            Transform house = candidates[index++];

            if (placeBoy)
            {
                if (SpawnZone(house, deliveryZoneBoyPrefab, OwnerType.Boy))
                    boyPlaced++;
            }
            else
            {
                if (SpawnZone(house, deliveryZoneWitchPrefab, OwnerType.Witch))
                    witchPlaced++;
            }
        }

        if (boyPlaced < boyTarget || witchPlaced < witchTarget)
        {
            Debug.LogWarning(
                $"Spawned fewer delivery zones than requested: " +
                $"Boy {boyPlaced}/{boyTarget}, Witch {witchPlaced}/{witchTarget}. " +
                $"Only {candidates.Count} eligible house(s) were available.", this);
        }
        else
        {
            Debug.Log($"Delivery zones spawned: Boy {boyPlaced}, Witch {witchPlaced}");
        }
    }

    List<Transform> CollectCandidates(IReadOnlyList<Transform> houses, out int rejected)
    {
        rejected = 0;

        List<Transform> candidates = new List<Transform>();

        if (houses != null)
        {
            for (int i = 0; i < houses.Count; i++)
                AddCandidate(houses[i], candidates, ref rejected);
        }

        if (additionalHouseParents != null)
        {
            foreach (Transform parent in additionalHouseParents)
            {
                if (parent == null) continue;

                foreach (Transform child in parent)
                {
                    if (!child.gameObject.activeInHierarchy) continue;

                    AddCandidate(child, candidates, ref rejected);
                }
            }
        }

        return candidates;
    }

    void AddCandidate(Transform house, List<Transform> candidates, ref int rejected)
    {
        if (house == null) return;

        if (FindFrontMarker(house) == null)
        {
            if (rejected == 0)
            {
                Debug.LogWarning(
                    $"Marker '{frontMarkerName}' [{CharCodes(frontMarkerName)}] not found on " +
                    $"'{house.name}'. Its children are: {DescribeChildren(house)}", house);
            }

            rejected++;
            return;
        }

        if (!candidates.Contains(house))
            candidates.Add(house);
    }

    Transform FindFrontMarker(Transform house)
    {
        Transform direct = house.Find(frontMarkerName);

        if (direct != null) return direct;

        foreach (Transform candidate in house.GetComponentsInChildren<Transform>(true))
        {
            if (candidate != house && NameMatchesMarker(candidate.name))
                return candidate;
        }

        return null;
    }

    bool NameMatchesMarker(string name)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(frontMarkerName))
            return false;

        return string.Equals(name.Trim(), frontMarkerName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    string DescribeChildren(Transform house)
    {
        if (house.childCount == 0) return "(no children)";

        List<string> described = new List<string>(house.childCount);

        foreach (Transform child in house)
            described.Add($"'{child.name}' [{CharCodes(child.name)}]");

        return string.Join(", ", described);
    }

    string CharCodes(string value)
    {
        if (string.IsNullOrEmpty(value)) return "empty";

        List<string> codes = new List<string>(value.Length);

        foreach (char c in value)
            codes.Add(((int)c).ToString());

        return string.Join(" ", codes);
    }

    bool SpawnZone(Transform house, GameObject prefab, OwnerType owner)
    {
        if (prefab == null)
        {
            Debug.LogError($"No delivery zone prefab assigned for {owner}.", this);
            return false;
        }

        Transform front = FindFrontMarker(house);

        if (front == null)
        {
            Debug.LogError($"'{frontMarkerName}' not found on {house.name}", house);
            return false;
        }

        Vector3 position = front.position + front.forward * zoneForwardOffset;

        position.y = SurfaceHeightUnder(position, house) + zoneHeightOffset;

        GameObject zone = Instantiate(prefab, position, front.rotation);

        zone.transform.SetParent(front);

        NewspaperDelivery delivery = zone.GetComponent<NewspaperDelivery>();

        if (delivery == null)
        {
            Debug.LogError($"{prefab.name} has no NewspaperDelivery component.", zone);
            return false;
        }

        delivery.Configure(owner);

        return true;
    }

    float SurfaceHeightUnder(Vector3 position, Transform house)
    {
        float probe = zoneGroundProbeHeight > 0f ? zoneGroundProbeHeight : 5f;
        int layers = zoneGroundLayers.value != 0 ? zoneGroundLayers.value : Physics.DefaultRaycastLayers;

        Vector3 origin = position + Vector3.up * probe;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probe * 2f,
                            layers, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        Debug.LogWarning(
            $"Nothing under the delivery zone at {position} to rest it on. Zone Forward Offset " +
            $"is {zoneForwardOffset}: at 0 the zone sits on the Front marker, which is out past " +
            $"the kerb where there is no ground, so set it to 0.8. Standing the zone on " +
            $"'{house.name}' own base for now, which will sink it into the pavement.", house);

        if (TryGetHouseBaseY(house, out float baseY))
            return baseY;

        return position.y;
    }

    bool TryGetHouseBaseY(Transform house, out float baseY)
    {
        baseY = 0f;

        Renderer[] renderers = house.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0) return false;

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        baseY = bounds.min.y;

        return true;
    }

    void ValidateZonePrefab(GameObject prefab, string fieldName)
    {
        if (prefab == null) return;

        if (prefab.scene.IsValid())
        {
            Debug.LogWarning(
                $"{fieldName} points at the scene object '{prefab.name}' instead of a prefab asset. " +
                $"Assign the prefab from the Project window.", this);
        }
    }

    void Shuffle(List<Transform> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int rand = UnityEngine.Random.Range(i, list.Count);

            var temp = list[i];
            list[i] = list[rand];
            list[rand] = temp;
        }
    }
}
