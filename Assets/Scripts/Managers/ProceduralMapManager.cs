using System;
using System.Collections.Generic;
using UnityEngine;

public class ProceduralMapManager : MonoBehaviour
{
    [Header("Grid Settings")]
    public int width = 50;
    public int height = 50;
    public float cellSize = 8f;

    [Header("House Settings")]
    [Tooltip("World Y position at which houses are spawned")]
    public float houseSpawnY = 3f;

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

    [Header("House density")]
    [Tooltip("Chance to place a house next to a road near the town centre")]
    [Range(0f, 1f)]
    public float centreDensity = 0.95f;

    [Tooltip("Chance to place a house next to a road at the map edge")]
    [Range(0f, 1f)]
    public float edgeDensity = 0.3f;

    [Header("Prefabs")]
    public GameObject roadStraightPrefab;
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
    private List<GameObject> validHousePrefabs = new List<GameObject>();

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
                if (prefab != null)
                    validHousePrefabs.Add(prefab);
            }
        }

        if (validHousePrefabs.Count == 0)
            Debug.LogError("No house prefabs assigned; no houses will be spawned.", this);
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

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (grid[x, z] != 1) continue;

                Vector3 pos = new Vector3(x * cellSize, 0f, z * cellSize);

                GameObject road = Instantiate(
                    roadStraightPrefab,
                    pos,
                    RoadTileRotation(new Vector2Int(x, z)),
                    roadsParent
                );

                road.transform.localScale = new Vector3(cellSize, 0.3f, cellSize);
            }
        }
    }

    Quaternion RoadTileRotation(Vector2Int cell)
    {
        if (!roadDirections.TryGetValue(cell, out Vector2Int dir))
            return Quaternion.identity;

        bool runsEastWest = dir.x != 0;

        return runsEastWest ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
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

                Vector3 roadPos = new Vector3(x * cellSize, houseSpawnY, z * cellSize);

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

        Vector3 housePos = roadPos + dir * cellSize;

        GameObject prefab = validHousePrefabs[
            UnityEngine.Random.Range(0, validHousePrefabs.Count)
        ];
        Quaternion rot = Quaternion.LookRotation(-dir);

        GameObject house = Instantiate(prefab, housePos, rot, housesParent);

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
