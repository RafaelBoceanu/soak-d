using System;
using System.Collections.Generic;
using UnityEngine;

public class ProceduralMapManager : MonoBehaviour
{
    [Flags]
    private enum RoadLinks
    {
        None = 0,
        North = 1,
        East = 2,
        South = 4,
        West = 8
    }

    private static readonly RoadLinks[] linkOrder =
    {
        RoadLinks.North, RoadLinks.East, RoadLinks.South, RoadLinks.West
    };

    private const RoadLinks straightLinks = RoadLinks.North | RoadLinks.South;
    private const RoadLinks cornerLeftLinks = RoadLinks.South | RoadLinks.West;
    private const RoadLinks cornerRightLinks = RoadLinks.South | RoadLinks.East;
    private const RoadLinks tJunctionLinks = RoadLinks.South | RoadLinks.East | RoadLinks.West;
    private const RoadLinks crossroadsLinks = RoadLinks.North | RoadLinks.East | RoadLinks.South | RoadLinks.West;

    private const float roadThickness = 0.3f;
    [Tooltip("How far the road surface sits above the ground plane.")]
    private const float roadSurfaceLift = 0.03f;

    private static readonly Vector2Int[] neighbourSteps =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };

    [Header("Grid Settings")]
    public int width = 50;
    public int height = 50;
    public float cellSize = 8f;

    [Header("House Settings")]
    [Tooltip("World Y the houses stand on. Each house is lifted by its own bounds so it " +
             "rests on this height whatever its pivot is.")]
    public float groundY = 0f;

    [Tooltip("Distance from the centre of the road to the front wall of a house. Houses are " +
             "pushed back by their own depth so shallow and deep models still line up along " +
             "the street. Keep it above half a road tile or houses will sit on the tarmac.")]
    public float houseSetback = 5f;

    [Header("Town Shape")]
    [Tooltip("How many primary roads branch from the town centre")]
    [Range(2, 6)]
    public int primaryRoads = 4;

    [Tooltip("How many secondary roads branch off primary roads")]
    [Range(0, 8)]
    public int secondaryRoads = 5;

    [Tooltip("How many short side streets branch off secondary roads")]
    [Range(0, 12)]
    public int sideStreets = 8;

    [Tooltip("Cells a street runs before it may turn. Larger values give straighter streets.")]
    public int minSegmentLength = 6;
    public int maxSegmentLength = 14;

    [Tooltip("Chance a street turns at the end of a segment instead of carrying straight on.")]
    public float turnChance = 0.35f;

    [Tooltip("Smallest gap between two branches, so blocks are never one cell deep")]
    public int minBlockSize = 4;

    [Header("Street Connections")]
    [Tooltip("How far a street that dead ends may be pushed on to meet another street")]
    public int maxConnectDistance = 12;

    [Tooltip("Delete streets that still dead end after there was no way to connect them")]
    public bool trimUnconnectedStreets = true;

    [Header("House density")]
    [Tooltip("Chance to place a house next to a road near the town centre")]
    [Range(0f, 1f)]
    public float centreDensity = 0.95f;

    [Tooltip("Chance to place a house next to a road at the map edge")]
    [Range(0f, 1f)]
    public float edgeDensity = 0.3f;

    [Header("Road Prefabs")]
    [Tooltip("Tile laid where a road runs through, dead ends or is left unmatched")]
    public GameObject roadStraightPrefab;

    [Tooltip("Tick if the straight tile is modelled running east to west. Leave " +
           "clear if it runs north to south, which is how the kerbs and centre " +
           "line are laid out in RoadStraightPrefab.")]
    public bool straightPrefabRunsEastWest = false;

    [Tooltip("Bend whose open sides are south and west before rotation")]
    public GameObject roadCornerLeftPrefab;

    [Tooltip("Bend whose open sides are south and east before rotation")]
    public GameObject roadCornerRightPrefab;

    [Tooltip("Three way junction, closed to the north before rotation")]
    public GameObject roadTJunctionPrefab;

    [Tooltip("Crossroads, open on all four sides")]
    public GameObject roadCrossroadsPrefab;

    [Header("House Prefabs")]
    public GameObject[] housePrefabs;

    [Header("Parents")]
    public Transform roadsParent;
    public Transform housesParent;

    public event Action<IReadOnlyList<Transform>> OnHousesGenerated;

    private int[,] grid;
    private int[,] occupancyGrid;
    private List<Transform> spawnedHouses = new List<Transform>();
    private List<Vector2Int> roadNetwork = new List<Vector2Int>();
    private List<Vector2Int> branchOrigins = new List<Vector2Int>();

    private Dictionary<Vector2Int, Vector2Int> roadDirections = new Dictionary<Vector2Int, Vector2Int>();
    private List<HousePrefab> validHousePrefabs = new List<HousePrefab>();

    private readonly struct HousePrefab
    {
        public readonly GameObject prefab;

        // Bounds of the prefab
        public readonly Bounds bounds;

        public HousePrefab(GameObject prefab, Bounds bounds)
        {
            this.prefab = prefab;
            this.bounds = bounds;
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CollectValidHousePrefabs();

        GenerateGrid();
        GenerateRoads();
        SpawnRoads();
        SpawnHouses();

        Debug.Log("Total houses generated: " + spawnedHouses.Count);

        OnHousesGenerated?.Invoke(spawnedHouses);
    }

    void CollectValidHousePrefabs()
    {
        validHousePrefabs.Clear();

        if (housePrefabs != null)
        {
            foreach (GameObject prefab in housePrefabs)
            {
                if (prefab == null) continue;

                if (!TryMeasurePrefab(prefab, out Bounds bounds))
                {
                    Debug.LogWarning(
                        $"'{prefab.name}' has no meshes to measure; it is skipped so it cannot " +
                        $"be dropped through the ground or into the road.", prefab);

                    continue;
                }

                WarnIfHouseOutgrowsCell(prefab, bounds);

                validHousePrefabs.Add(new HousePrefab(prefab, bounds));
            }
        }

        if (validHousePrefabs.Count == 0)
            Debug.LogError("No house prefabs assigned; no houses will be spawned.", this);
    }

    bool TryMeasurePrefab(GameObject prefab, out Bounds bounds)
    {
        bounds = new Bounds();

        Transform root = prefab.transform;

        bool measured = false;

        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;

            Bounds mesh = filter.sharedMesh.bounds;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = mesh.center + Vector3.Scale(
                    mesh.extents,
                    new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f
                    )
                );

                Vector3 point = filter.transform.TransformPoint(local) - root.localPosition;

                if (measured) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); measured = true; }
            }
        }

        return measured;
    }

    void WarnIfHouseOutgrowsCell(GameObject prefab, Bounds bounds)
    {
        float widest = Mathf.Max(bounds.size.x, bounds.size.z);

        if (widest <= cellSize) return;

        Debug.LogWarning(
            $"'{prefab.name}' is {widest:0.0} units across but a grid cell is only {cellSize:0.0}, " +
            $"so it will overlap its neighbours. Scale the prefab down or raise Cell Size.", prefab);
    }

    void GenerateGrid()
    {
        grid = new int[width, height];
        occupancyGrid = new int[width, height];
        roadNetwork.Clear();
        branchOrigins.Clear();
        roadDirections.Clear();
        spawnedHouses.Clear();
    }

    void GenerateRoads()
    {
        Vector2Int centre = new Vector2Int(width / 2, height / 2);

        SetRoad(centre, Vector2Int.up);

        List<Vector2Int> directions = new List<Vector2Int>()
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        ShuffleDirections(directions);

        int primaries = Mathf.Min(primaryRoads, directions.Count);

        for (int i = 0; i < primaries; i++)
        {
            int length = UnityEngine.Random.Range(height / 3, height);
            GrowRoad(centre, directions[i], length, minSegmentLength * 2, maxSegmentLength * 2);
        }

        BranchStreets(secondaryRoads, height / 6, height / 3);
        BranchStreets(sideStreets, 3, Mathf.Max(4, height / 6));

        ConnectDanglingStreets();
    }

    void ConnectDanglingStreets()
    {
        Vector2Int centre = new Vector2Int(width / 2, height / 2);

        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;

            foreach (Vector2Int end in FindDeadEnds())
            {
                if (end == centre) continue;

                if (CountRoadNeighbours(end) != 1) continue;

                if (IsOnGridEdge(end)) continue;

                if (TryExtendToNetwork(end)) changed = true;
                else if (trimUnconnectedStreets) { TrimCulDeSac(end, centre); changed = true; }
            }

            if (!changed) break;
        }
    }

    List<Vector2Int> FindDeadEnds()
    {
        List<Vector2Int> ends = new List<Vector2Int>();

        foreach (Vector2Int cell in roadNetwork)
        {
            if (CountRoadNeighbours(cell) == 1) ends.Add(cell);
        }

        return ends;
    }

    bool TryExtendToNetwork(Vector2Int end)
    {
        Vector2Int heading = HeadingAwayFromRoad(end);

        Vector2Int[] options =
        {
            heading, Perpendicular(heading), -Perpendicular(heading)
        };

        Vector2Int bestDir = Vector2Int.zero;
        int bestSteps = 0;

        foreach (Vector2Int dir in options)
        {
            int steps = MeasureConnection(end, dir);

            if (steps == 0) continue;
            if (bestSteps != 0 && steps >= bestSteps) continue;

            bestSteps = steps;
            bestDir = dir;
        }

        if (bestSteps == 0) return false;

        Vector2Int pos = end;

        for (int i = 0; i < bestSteps; i++)
        {
            pos += bestDir;
            SetRoad(pos, bestDir);
        }

        return true;
    }

    int MeasureConnection(Vector2Int from, Vector2Int dir)
    {
        Vector2Int cell = from;

        for (int step = 1; step <= maxConnectDistance; step++)
        {
            cell += dir;

            if (!IsInsideGrid(cell)) return 0;

            if (IsRoad(cell)) return step;

            if (!CanPlaceRoad(cell, dir)) return 0;
        }

        return 0;
    }

    void TrimCulDeSac(Vector2Int end, Vector2Int centre)
    {
        Vector2Int cell = end;

        while (IsRoad(cell) && cell != centre && CountRoadNeighbours(cell) <= 1)
        {
            Vector2Int next = FirstRoadNeighbour(cell);

            ClearRoad(cell);

            if (next == cell) break;

            cell = next;
        }
    }

    void ClearRoad(Vector2Int cell)
    {
        if (!IsInsideGrid(cell)) return;

        grid[cell.x, cell.y] = 0;
        roadNetwork.Remove(cell);
        roadDirections.Remove(cell);
    }

    Vector2Int HeadingAwayFromRoad(Vector2Int cell)
    {
        Vector2Int neighbour = FirstRoadNeighbour(cell);

        return cell - neighbour;
    }

    Vector2Int FirstRoadNeighbour(Vector2Int cell)
    {
        foreach (Vector2Int step in neighbourSteps)
        {
            if (IsRoad(cell + step)) return cell + step;
        }

        return cell;
    }

    int CountRoadNeighbours(Vector2Int cell)
    {
        int count = 0;

        foreach (Vector2Int step in neighbourSteps)
        {
            if (IsRoad(cell + step)) count++;
        }

        return count;
    }

    bool IsOnGridEdge(Vector2Int cell)
    {
        return cell.x == 0 || cell.y == 0 || cell.x == width - 1 || cell.y == height - 1;
    }

    void BranchStreets(int count, int minLength, int maxLength)
    {
        for (int i = 0; i < count; i++)
        {
            if (roadNetwork.Count == 0) break;

            if (!TryFindBranchPoint(out Vector2Int origin, out Vector2Int dir)) continue;

            int length = UnityEngine.Random.Range(minLength, Mathf.Max(minLength + 1, maxLength));

            GrowRoad(origin, dir, length, minSegmentLength, maxSegmentLength);
        }
    }

    bool TryFindBranchPoint(out Vector2Int origin, out Vector2Int dir)
    {
        const int attempts = 32;

        for (int i = 0; i < attempts; i++)
        {
            Vector2Int candidate = roadNetwork[UnityEngine.Random.Range(0, roadNetwork.Count)];

            if (IsTooCloseToAnotherBranch(candidate)) continue;

            if (!roadDirections.TryGetValue(candidate, out Vector2Int along)) continue;

            Vector2Int side = Perpendicular(along);

            if (UnityEngine.Random.value < 0.5f) side = -side;

            if (!CanPlaceRoad(candidate + side, side)) continue;

            branchOrigins.Add(candidate);

            origin = candidate;
            dir = side;

            return true;
        }

        origin = Vector2Int.zero;
        dir = Vector2Int.up;

        return false;
    }

    bool IsTooCloseToAnotherBranch(Vector2Int cell)
    {
        foreach (Vector2Int origin in branchOrigins)
        {
            int distance = Mathf.Abs(origin.x - cell.x) + Mathf.Abs(origin.y - cell.y);

            if (distance < minBlockSize) return true;
        }

        return false;
    }

    Vector2Int GrowRoad(Vector2Int start, Vector2Int dir, int length, int minSegment, int maxSegment)
    {
        Vector2Int pos = start;

        int untilTurn = UnityEngine.Random.Range(minSegment, maxSegment + 1);

        for (int i = 0; i < length; i++)
        {
            Vector2Int next = pos + dir;

            if (!CanPlaceRoad(next, dir)) break;

            SetRoad(next, dir);
            pos = next;

            untilTurn--;

            if (untilTurn > 0) continue;

            if (UnityEngine.Random.value < turnChance)
                dir = UnityEngine.Random.value < 0.5f ? Perpendicular(dir) : -Perpendicular(dir);

            untilTurn = UnityEngine.Random.Range(minSegment, maxSegment + 1);
        }

        return pos;
    }

    bool CanPlaceRoad(Vector2Int cell, Vector2Int dir)
    {
        if (!IsInsideGrid(cell)) return false;

        if (grid[cell.x, cell.y] == 1) return true;

        Vector2Int side = Perpendicular(dir);

        return !IsRoad(cell + side) && !IsRoad(cell - side);
    }

    bool IsRoad(Vector2Int cell)
    {
        return IsInsideGrid(cell) && grid[cell.x, cell.y] == 1;
    }

    void SetRoad(Vector2Int pos, Vector2Int dir)
    {
        if (!IsInsideGrid(pos)) return;

        if (grid[pos.x, pos.y] == 1) return;

        grid[pos.x, pos.y] = 1;
        roadNetwork.Add(pos);
        roadDirections[pos] = dir;
    }

    Vector2Int Perpendicular(Vector2Int dir)
    {
        return new Vector2Int(-dir.y, dir.x);
    }

    void ShuffleDirections(List<Vector2Int> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int rand = UnityEngine.Random.Range(i, list.Count);

            Vector2Int temp = list[i];
            list[i] = list[rand];
            list[rand] = temp;
        }
    }

    void SpawnRoads()
    {
        if (roadStraightPrefab == null)
        {
            Debug.LogError("No road prefab assigned; no roads will be spawned.", this);
            return;
        }

        WarnAboutMissingRoadPrefabs();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (grid[x, z] != 1) continue;

                Vector3 pos = new Vector3(x * cellSize, groundY - roadThickness * 0.5f + roadSurfaceLift, z * cellSize);

                GameObject prefab = PickRoadPrefab(GetRoadLinks(x, z), out float yaw);

                GameObject road = Instantiate(
                    prefab,
                    pos,
                    Quaternion.Euler(0f, yaw, 0f),
                    roadsParent
                );

                road.transform.localScale = new Vector3(cellSize, roadThickness, cellSize);
                road.name = prefab.name + "_" + x + "_" + z;
            }
        }
    }

    void WarnAboutMissingRoadPrefabs()
    {
        bool anyMissing =
            roadCornerLeftPrefab == null ||
            roadCornerRightPrefab == null ||
            roadTJunctionPrefab == null ||
            roadCrossroadsPrefab == null;

        if (!anyMissing) return;

        Debug.LogWarning(
            "Some road prefabs are unassigned; those tiles fall back to the straight road.",
            this
        );
    }

    RoadLinks GetRoadLinks(int x, int z)
    {
        RoadLinks links = RoadLinks.None;

        if (IsRoad(new Vector2Int(x, z + 1))) links |= RoadLinks.North;
        if (IsRoad(new Vector2Int(x + 1, z))) links |= RoadLinks.East;
        if (IsRoad(new Vector2Int(x, z - 1))) links |= RoadLinks.South;
        if (IsRoad(new Vector2Int(x - 1, z))) links |= RoadLinks.West;

        return links;
    }

    GameObject PickRoadPrefab(RoadLinks links, out float yaw)
    {
        switch (CountLinks(links))
        {
            case 4:
                return MatchRoadPrefab(roadCrossroadsPrefab, crossroadsLinks, links, out yaw);

            case 3:
                return MatchRoadPrefab(roadTJunctionPrefab, tJunctionLinks, links, out yaw);

            case 2 when links != straightLinks && links != (RoadLinks.East | RoadLinks.West):
                bool mirrored = UseLeftHandCorner();

                return MatchRoadPrefab(
                    mirrored ? roadCornerLeftPrefab : roadCornerRightPrefab,
                    mirrored ? cornerLeftLinks : cornerRightLinks,
                    links,
                    out yaw
                );

            default:
                yaw = StraightYaw(links);
                return roadStraightPrefab;
        }
    }

    bool UseLeftHandCorner()
    {
        if (roadCornerLeftPrefab == null) return false;
        if (roadCornerRightPrefab == null) return true;

        return UnityEngine.Random.value < 0.5f;
    }

    GameObject MatchRoadPrefab(GameObject prefab, RoadLinks prefabLinks, RoadLinks links, out float yaw)
    {
        if (prefab != null)
        {
            for (int turns = 0; turns < linkOrder.Length; turns++)
            {
                if (RotateLinks(prefabLinks, turns) != links) continue;

                yaw = turns * 90f;

                return prefab;
            }
        }

        yaw = StraightYaw(links);

        return roadStraightPrefab;
    }

    float StraightYaw(RoadLinks links)
    {
        bool roadRunsEastWest =
            (links & (RoadLinks.East | RoadLinks.West)) != RoadLinks.None &&
            (links & (RoadLinks.North | RoadLinks.South)) == RoadLinks.None;

        return roadRunsEastWest != straightPrefabRunsEastWest ? 90f : 0f;
    }

    static RoadLinks RotateLinks(RoadLinks links, int quarterTurns)
    {
        RoadLinks rotated = RoadLinks.None;

        for (int i = 0; i < linkOrder.Length; i++)
        {
            if ((links & linkOrder[i]) == RoadLinks.None) continue;

            rotated |= linkOrder[(i + quarterTurns) % linkOrder.Length];
        }

        return rotated;
    }

    static int CountLinks(RoadLinks links)
    {
        int count = 0;
        
        foreach (RoadLinks link in linkOrder)
        {
            if ((links & link) != RoadLinks.None) count++;
        }

        return count;
    }

    void SpawnHouses()
    {
        if (validHousePrefabs.Count == 0) return;

        Vector2 centre = new Vector2(width / 2f, height / 2f);
        float maxDist = Mathf.Sqrt(centre.x * centre.x + centre.y * centre.y);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (grid[x, z] != 1) continue;

                float dist = Vector2.Distance(new Vector2(x, z), centre);
                float t = Mathf.Clamp01(dist / maxDist);
                float spawnChance = Mathf.Lerp(centreDensity, edgeDensity, t);

                Vector3 roadPos = new Vector3(x * cellSize, groundY, z * cellSize);

                TryPlaceHouse(x, z, Vector3.left, roadPos, spawnChance);
                TryPlaceHouse(x, z, Vector3.right, roadPos, spawnChance);
                TryPlaceHouse(x, z, Vector3.forward, roadPos, spawnChance);
                TryPlaceHouse(x, z, Vector3.back, roadPos, spawnChance);
            }
        }
    }

    void TryPlaceHouse(int x, int z, Vector3 dir, Vector3 roadPos, float spawnChance)
    {
        if (UnityEngine.Random.value > spawnChance) return;

        int nx = x + (int)dir.x;
        int nz = z + (int)dir.z;

        if (!IsInsideGrid(new Vector2Int(nx, nz))) return;
        if (grid[nx, nz] != 0) return;
        if (occupancyGrid[nx, nz] == 1) return;

        HousePrefab choice = validHousePrefabs[
            UnityEngine.Random.Range(0, validHousePrefabs.Count)
        ];

        Quaternion rot = Quaternion.LookRotation(-dir);

        Vector3 housePos = roadPos
            + dir * (houseSetback + choice.bounds.max.z)
            + Vector3.up * -choice.bounds.min.y;

        GameObject house = Instantiate(choice.prefab, housePos, rot, housesParent);

        spawnedHouses.Add(house.transform);

        occupancyGrid[nx, nz] = 1;
    }

    bool IsInsideGrid(Vector2Int pos)
    {
        return pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height;
    }

    public IReadOnlyList<Transform> GetSpawnedHouses()
    {
        return spawnedHouses;
    }
}
