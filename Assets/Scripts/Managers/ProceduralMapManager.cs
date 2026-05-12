using System;
using System.Collections.Generic;
using UnityEngine;

public class ProceduralMapManager : MonoBehaviour
{
    [Header("Grid Settings")]
    public int width = 100;
    public int height = 100;
    public float cellSize = 5f;

    [Header("Prefabs")]
    public GameObject roadStraightPrefab;
    public GameObject[] housePrefabs;

    [Header("Parents")]
    public Transform roadsParent;
    public Transform housesParent;

    public event Action<List<Transform>> OnHousesGenerated;

    private int[,] grid;
    private int[,] occupancyGrid;

    private List<Transform> spawnedHouses = new List<Transform>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        GenerateGrid();
        GenerateRoads();
        SpawnRoads();
        SpawnHouses();

        Debug.Log("Total houses generated: " + spawnedHouses.Count);

        OnHousesGenerated?.Invoke(spawnedHouses);
    }

    void GenerateGrid()
    {
        grid = new int[width, height];
        occupancyGrid = new int[width, height];
    }

    void GenerateRoads()
    {
        int centerX = width / 2;
        int centerZ = height / 2;

        for (int z = 0; z < height; z++)
            grid[centerX, z] = 1;

        for (int x = 0; x < width; x++)
            grid[x, centerZ] = 1;

        int roadCount = Mathf.Max(10, (width * height) / 50);

        for (int i = 0; i < roadCount; i++)
        {
            Vector2Int pos = new Vector2Int(
                UnityEngine.Random.Range(0, width),
                UnityEngine.Random.Range(0, height)
            );

            Vector2Int dir = GetRandomDirection();
            int length = UnityEngine.Random.Range(height / 2, height);

            for (int j = 0; j < length; j++)
            {
                if (!IsInsideGrid(pos)) break;

                grid[pos.x, pos.y] = 1;

                if (UnityEngine.Random.value < 0.3f)
                    dir = GetRandomDirection();

                pos += dir;
            }
        }
    }

    void SpawnRoads()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (grid[x, z] != 1) continue;

                Vector3 pos = new Vector3(x * cellSize, 0, z * cellSize);

                GameObject road = Instantiate(
                    roadStraightPrefab, 
                    pos, 
                    Quaternion.identity, 
                    roadsParent
                );

                road.transform.localScale = new Vector3(cellSize, 0.3f, cellSize);

                Debug.DrawLine(pos, pos + Vector3.up * 2f, Color.red, 100f);
            }
        }
    }

    void SpawnHouses()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (grid[x, z] != 1) continue;

                Vector3 roadPos = new Vector3(x * cellSize, 0, z * cellSize);

                TryPlaceHouse(x, z, Vector3.left, roadPos);
                TryPlaceHouse(x, z, Vector3.right, roadPos);
                TryPlaceHouse(x, z, Vector3.forward, roadPos);
                TryPlaceHouse(x, z, Vector3.back, roadPos);
            }    
        }
    }

    void TryPlaceHouse(int x, int z, Vector3 dir,  Vector3 roadPos)
    {
        int nx = x + (int)dir.x;
        int nz = z + (int)dir.z;

        if (!IsInsideGrid(new Vector2Int(nx, nz))) return;
        if (grid[nx, nz] != 0) return;
        if (occupancyGrid[nx, nz] == 1) return;

        Vector3 housePos = roadPos + dir * cellSize;

        GameObject prefab = housePrefabs[
            UnityEngine.Random.Range(0, housePrefabs.Length)
        ];

        Quaternion rot = Quaternion.LookRotation(-dir);

        GameObject house = Instantiate(prefab, housePos, rot, housesParent);

        spawnedHouses.Add(house.transform);

        occupancyGrid[nx, nz] = 1;    
    }

    Vector2Int GetRandomDirection()
    {
        int r = UnityEngine.Random.Range(0, 4);

        switch (r)
        {
            case 0: return Vector2Int.up;
            case 1: return Vector2Int.down;
            case 2: return Vector2Int.left;
            default: return Vector2Int.right;
        }
    }

    bool IsInsideGrid(Vector2Int pos)
    {
        return pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height;
    }

    public List<Transform> GetSpawnedHouses()
    {
        return spawnedHouses;
    }
}
