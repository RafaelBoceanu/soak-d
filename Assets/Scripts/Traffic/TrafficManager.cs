using System.Collections.Generic;
using UnityEngine;

public class TrafficManager : MonoBehaviour
{
    public static TrafficManager Instance { get; private set; }

    static readonly Vector2Int[] steps =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left,
    };

    [Header("References")]
    [SerializeField] private ProceduralMapManager mapManager;

    [Tooltip("Prefabs with a TrafficCar on the root")]
    [SerializeField] private GameObject[] carPrefabs;

    [Tooltip("Parent the cars are kept under")]
    [SerializeField] private Transform carsParent;

    [Header("Cars")]
    [SerializeField, Min(0)] private int carCount = 25;

    [Tooltip("Shortest gap allowed between two cars at spawn")]
    [SerializeField, Min(0f)] private float minSpawnSpacing = 20f;

    [Tooltip("How much likelier carrying straight on is than turning at a junction")]
    [SerializeField, Min(0.01f)] private float straightOnWeight = 2f;

    [Header("Lanes")]
    [Tooltip("Tick for left side traffic")]
    [SerializeField] private bool driveOnLeft = false;

    [Tooltip("Distance from the centre line to the middle of a lane, calculated as a fraction of a cell")]
    [SerializeField, Range(0.05f, 0.3f)] private float laneOffset = 0.16f;

    [Tooltip("Points a bend is cut into")]
    [SerializeField, Range(2, 16)] private int curveSamples = 8;

    [Header("Traffic lights")]
    [Tooltip("Pole with a TrafficLightHead on the root")]
    [SerializeField] private GameObject trafficLightPrefab;

    [SerializeField] private Transform lightsParent;

    [SerializeField, Range(0f, 1f)] private float crossroadsLightChance = 0.6f;
    [SerializeField, Range(0f, 1f)] private float tJunctionLightChance = 0.25f;

    [SerializeField, Min(1f)] private float greenSeconds = 10f;
    [SerializeField, Min(0f)] private float amberSeconds = 2.5f;

    [Tooltip("Both ways held on red, so the junction clears before the other way goes")]
    [SerializeField, Min(0f)] private float allRedSeconds = 1.5f;

    [Tooltip("How far out from the junction centre a pole stands, as a fraction of a cell")]
    [SerializeField, Range(0.3F, 0.5f)] private float poleCornerOffset = 0.35f;

    private readonly Dictionary<Vector2Int, TrafficLight> lights = new Dictionary<Vector2Int, TrafficLight>();
    private readonly Dictionary<Vector2Int, TrafficCar> reservations = new Dictionary<Vector2Int, TrafficCar>();
    private readonly List<TrafficCar> cars = new List<TrafficCar>();

    public ProceduralMapManager Map => mapManager;
    public int CarCount => cars.Count;
    public int LightCount => lights.Count;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated += HandleCityGenerated;
        else
            Debug.LogError("[TrafficManager] No map manager assigned; the roads will stay empty.", this);
    }

    void OnDisable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated -= HandleCityGenerated;
    }

    void HandleCityGenerated(IReadOnlyList<Transform> houses)
    {
        PlaceTrafficLights();
        SpawnCars();
    }

    #region Traffic lights
    void PlaceTrafficLights()
    {
        foreach (Vector2Int cell in mapManager.GetRoadCells())
        {
            int links = CountLinks(cell);

            float chance = links == 4 ? crossroadsLightChance
                         : links == 3 ? tJunctionLightChance
                         : 0f;

            if (chance <= 0f || Random.value > chance) continue;

            GameObject holder = new GameObject($"TrafficLight_{cell.x}_{cell.y}");
            holder.transform.SetParent(lightsParent, false);
            holder.transform.position = CellCentre(cell);

            TrafficLight light = holder.AddComponent<TrafficLight>();
            light.Configure(greenSeconds, amberSeconds, allRedSeconds);

            for (int side = 0; side < steps.Length; side++)
            {
                if (mapManager.IsRoad(cell + steps[side]))
                    SpawnHead(light, cell, side);
            }

            lights[cell] = light;
        }

        Debug.Log($"[TrafficManager] Junctions with traffic lights: {lights.Count}");
    }

    void SpawnHead(TrafficLight light, Vector2Int cell, int side)
    {
        if (trafficLightPrefab == null) return;

        Vector3 outward = ToWorld(steps[side]);
        Vector3 kerb = KerbSide(-outward);

        Vector3 position = CellCentre(cell) + (outward + kerb) * (poleCornerOffset * mapManager.cellSize);
        position.y = mapManager.PavementTopY;

        GameObject pole = Instantiate(trafficLightPrefab, position, Quaternion.LookRotation(outward), light.transform);

        TrafficLightHead head = pole.GetComponent<TrafficLightHead>();

        if (head == null)
        {
            Debug.LogError($"[TrafficManager] {trafficLightPrefab.name} has no TrafficLightHead component.", pole);

            Destroy(pole);
            return;
        }

        light.AddHead(head, side);
    }

    public bool TryGetLight(Vector2Int cell, out TrafficLight light)
    {
        return lights.TryGetValue(cell, out light);
    }

    public bool PedestriansMayCross(Vector2Int cell, int side)
    {
        return !lights.TryGetValue(cell, out TrafficLight light) || light.PedestrianMayCross(side);
    }
    #endregion

    #region Cars
    void SpawnCars()
    {
        if (carPrefabs == null || carPrefabs.Length == 0)
        {
            Debug.LogError("TrafficManager] No car prefabs assigned.", this);
            return;
        }

        List<Vector2Int> candidates = new List<Vector2Int>();

        foreach (Vector2Int cell in mapManager.GetRoadCells())
        {
            if (IsStraight(cell)) candidates.Add(cell);
        }

        Shuffle(candidates);

        List<Vector3> taken = new List<Vector3>();
        float sqrSpacing = minSpawnSpacing * minSpawnSpacing;

        foreach (Vector2Int cell in candidates)
        {
            if (cars.Count >= carCount) break;

            Vector3 centre = CellCentre(cell);

            if (IsTaken(taken, centre, sqrSpacing)) continue;

            GameObject prefab = carPrefabs[Random.Range(0, carPrefabs.Length)];

            if (prefab == null) continue;

            GameObject instance = Instantiate(prefab, centre, Quaternion.identity, carsParent);
            TrafficCar car = instance.GetComponent<TrafficCar>();

            if (car == null)
            {
                Debug.LogError($"[TrafficManager] {prefab.name} has no TrafficCar component.", instance);

                Destroy(instance);
                continue;
            }

            Vector2Int heading = mapManager.IsRoad(cell + Vector2Int.up) ? Vector2Int.up : Vector2Int.right;

            if (Random.value < 0.5f) heading = -heading;

            car.Bind(this, cell, heading);

            cars.Add(car);
            taken.Add(centre);
        }

        Debug.Log($"[TrafficManager] Cars out driving: {cars.Count}/{carCount}");
    }

    static bool IsTaken(List<Vector3> taken, Vector3 position, float sqrSpacing)
    {
        for (int i = 0; i < taken.Count; i++)
        {
            if ((taken[i] - position).sqrMagnitude < sqrSpacing) return true;
        }

        return false;
    }

    static void Shuffle(List<Vector2Int> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int swap = Random.Range(i, list.Count);

            Vector2Int temp = list[i];
            list[i] = list[swap];
            list[swap] = temp;
        }
    }
    #endregion

    #region Junction reservations
    public bool NeedsReservation(Vector2Int cell)
    {
        return CountLinks(cell) != 2;
    }

    public bool TryReserve(Vector2Int cell, TrafficCar car)
    {
        if (reservations.TryGetValue(cell, out TrafficCar holder) && holder != null && holder != car)
            return false;

        reservations[cell] = car;
        return true;
    }

    public void Release(Vector2Int cell, TrafficCar car)
    {
        if (reservations.TryGetValue(cell, out TrafficCar holder) && holder == car)
            reservations.Remove(cell);
    }
    #endregion

    #region Routes
    public Vector2Int ChooseExit(Vector2Int cell, Vector2Int heading)
    {
        float total = 0f;

        foreach (Vector2Int step in steps)
            total += ExitWeight(cell, heading, step);

        if (total <= 0f) return -heading;

        float roll = Random.value * total;
        Vector2Int last = -heading;

        foreach (Vector2Int step in steps)
        {
            float weight = ExitWeight(cell, heading, step);

            if (weight <= 0f) continue;

            last = step;
            roll -= weight;

            if (roll <= 0f) return step;
        }

        return last;
    }

    float ExitWeight(Vector2Int cell, Vector2Int heading, Vector2Int step)
    {
        if (step == -heading || !mapManager.IsRoad(cell + step)) return 0f;

        return step == heading ? straightOnWeight : 1f;
    }

    public void BuildCellPath(Vector2Int cell, Vector2Int entering, Vector2Int leaving, List<Vector3> path)
    {
        path.Clear();

        Vector3 centre = CellCentre(cell);
        float half = mapManager.cellSize * 0.5f;
        float lane = laneOffset * mapManager.cellSize;

        Vector3 inDir = ToWorld(entering);
        Vector3 outDir = ToWorld(leaving);

        Vector3 start = centre - inDir * half + KerbSide(inDir) * lane;
        Vector3 end = centre + outDir * half + KerbSide(outDir) * lane;

        if (entering == leaving)
        {
            path.Add(start);
            path.Add(end);
            return;
        }

        float handle = entering == -leaving ? half : half * 0.55f;

        Vector3 a = start + inDir * handle;
        Vector3 b = end - outDir * handle;

        for (int i = 0; i <= curveSamples; i++)
            path.Add(Bezier(start, a, b, end, i / (float)curveSamples));
    }

    static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;

        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }
    #endregion

    #region Helpers
    public static int SideOf(Vector2Int direction)
    {
        for (int i = 0; i < steps.Length; i++)
        {
            if (steps[i] == direction) return i;
        }

        return -1;
    }

    public Vector3 CellCentre(Vector2Int cell)
    {
        Vector3 centre = mapManager.RoadCellToWorld(cell);
        centre.y = mapManager.RoadSurfaceY;

        return centre;
    }

    Vector3 KerbSide(Vector3 travel)
    {
        Vector3 right = new Vector3(travel.z, 0f, -travel.x);

        return driveOnLeft ? -right : right;
    }

    static Vector3 ToWorld(Vector2Int direction)
    {
        return new Vector3(direction.x, 0f, direction.y);
    }

    int CountLinks(Vector2Int cell)
    {
        int count = 0;

        foreach (Vector2Int step in steps)
        {
            if (mapManager.IsRoad(cell + step)) count++;
        }

        return count;
    }

    bool IsStraight(Vector2Int cell)
    {
        if (CountLinks(cell) != 2) return false;

        bool northSouth = mapManager.IsRoad(cell + Vector2Int.up) && mapManager.IsRoad(cell + Vector2Int.down);
        bool eastWest = mapManager.IsRoad(cell + Vector2Int.right) && mapManager.IsRoad(cell + Vector2Int.left);

        return northSouth || eastWest;
    }
    #endregion
}
