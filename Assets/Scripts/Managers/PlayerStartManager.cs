using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-150)]
public class PlayerStartManager : MonoBehaviour
{
    private struct Spot
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    private static readonly Vector2Int[] neighbourSteps =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };

    [Header("References")]
    [SerializeField] private ProceduralMapManager mapManager;

    [Tooltip("If left empty, every player in the scene is lined up.")]
    [SerializeField] private PlayerInputHandler[] playersOverride;

    [Tooltip("If left empty, every mountable vehicle in the scene is parked next to its player.")]
    [SerializeField] private MountableVehicle[] vehiclesOverride;

    [Header("When")]
    [Tooltip("Hold the start until the delivery zones are down, so the spot can be picked to " +
             "sit fairly between them.")]
    [SerializeField] private bool waitForZones = true;

    [Tooltip("Place everyone anyway if no zone has registered after this long.")]
    [SerializeField, Min(0f)] private float zoneWaitTimeout = 10f;

    [Header("Choosing the spot")]
    [Tooltip("How hard the start is pushed towards the point where both rounds are the same " +
             "length. At 0 the start only tries to sit close to the zones.")]
    [SerializeField, Min(0f)] private float fairnessWeight = 3f;

    [Tooltip("How hard the start is pulled in towards the zones.")]
    [SerializeField, Min(0f)] private float closenessWeight = 1f;

    [Tooltip("Prefernce for starting on a junction, so there is more than one street to set " +
             "off down. Counts in metres of travel saved.")]
    [SerializeField, Min(0f)] private float junctionBonus = 20f;

    [Tooltip("Spots tried, best first, before the start gives up and leaves everyone where the " +
             "scene put them.")]
    [SerializeField, Min(1)] private int spotsToTry = 40;

    [Header("Layout")]
    [Tooltip("Gap between two players on the start line.")]
    [SerializeField, Min(0f)] private float playerSpacing = 3f;

    [Tooltip("Where a player's vehicle is parked relative to them.")]
    [SerializeField] private Vector2 vehicleParkingOffset = new Vector2(0f, 2.5f);

    [Tooltip("Gap between two vehicles belonging to the same player.")]
    [SerializeField, Min(0f)] private float vehicleSpacing = 2f;

    [Header("Ground")]
    [Tooltip("What the start line is allowed to stand on: the road, the pavement, the ground.")]
    [SerializeField] private LayerMask groundLayers = 0;

    [Tooltip("How far above the spot the search for that surface starts.")]
    [SerializeField, Min(0f)] private float groundProbeHeight = 6f;

    [Tooltip("How high a vehicle is parked above the ground when the probe cannot tell how high it is.")]
    [SerializeField, Min(0f)] private float fallbackVehicleLift = 0.25f;

    [Header("Clearance")]
    [Tooltip("Anything on these layers blocks a spot: houses, fences, props.")]
    [SerializeField] private LayerMask obstacleLayers = 0;

    [SerializeField, Min(0f)] private float clearanceRadius = 0.6f;

    [Header("Debug")]
    [SerializeField] private bool logStart = true;
    [SerializeField] private bool drawStartGizmo = true;

    private readonly List<PlayerInputHandler> players = new List<PlayerInputHandler>();
    private readonly List<MountableVehicle> vehicles = new List<MountableVehicle>();
    private readonly List<Spot> playerSpots = new List<Spot>();
    private readonly List<Spot> vehicleSpots = new List<Spot>();
    private readonly List<Vector3> zoneCentres = new List<Vector3>();
    private readonly List<Vector2Int> rankedCells = new List<Vector2Int>();
    private readonly Dictionary<Vector2Int, float> cellScores = new Dictionary<Vector2Int, float>();
    private readonly List<int> riderOf = new List<int>();
    private readonly List<int> vehiclesPerRider = new List<int>();

    private Vector3 zonesMiddle;

    public static bool HasStarted { get; private set; }

    public static Vector3 StartPoint { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        HasStarted = false;
        StartPoint = Vector3.zero;
    }

    void OnEnable()
    {
        if (mapManager == null)
            mapManager = FindFirstObjectByType<ProceduralMapManager>(FindObjectsInactive.Include);

        if (mapManager == null)
        {
            Debug.LogError("[PlayerStartManager] No map manager found, so there is no city to " +
                          "start in. Everyone is left where the scene put them.", this);

            HasStarted = true;
            return;
        }

        mapManager.OnHousesGenerated += HandleCityGenerated;
    }

    void OnDisable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated -= HandleCityGenerated;
    }

    private void HandleCityGenerated(IReadOnlyList<Transform> houses)
    {
        StartCoroutine(LineUpOnceTheCityIsSettled());
    }

    private IEnumerator LineUpOnceTheCityIsSettled()
    {
        yield return new WaitForFixedUpdate();

        if (waitForZones)
        {
            float waited = 0f;

            while (ZonesPlaced() == 0 && waited < zoneWaitTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        Physics.SyncTransforms();

        LineUp();

        HasStarted = true;
    }

    #region Choosing
    private void LineUp()
    {
        CollectActors();

        if (players.Count == 0)
        {
            Debug.LogWarning("[PlayerStartManager] No players found to line up.", this);
            return;
        }

        CollectZones();

        RankCells();

        if (rankedCells.Count == 0)
        {
            Debug.LogError("[PlayerStartManager] The city has no roads, so there is nowhere to " +
                           "start. Everyone is left where the scene put them.", this);
            return;
        }

        int tried = Mathf.Min(spotsToTry, rankedCells.Count);

        for (int i = 0; i < tried; i++)
        {
            Vector2Int cell = rankedCells[i];

            if (!TryLayoutStart(cell)) continue;

            ApplyLayout();

            StartPoint = mapManager.RoadCellToWorld(cell);

            if (logStart)
            {
                Debug.Log($"[PlayerStartManager] Round starts at cell {cell} " +
                          $"({StartPoint.x:0}, {StartPoint.z:0}), {players.Count} player(s) and " +
                          $"{vehicleSpots.Count} vehicle(s) placed, {i + 1} spot(s) tried.", this);
            }

            return;
        }

        Debug.LogWarning(
            $"[PlayerStartManager] None of the best {tried} road cells had room for the start " +
            $"line. Lower Player Spacing or Clearance Radius, or check Ground Layers covers the " +
            $"roads. Everyone is left where the scene put them.", this);
    }

    private void CollectActors()
    {
        players.Clear();

        if (playersOverride != null)
        {
            foreach (PlayerInputHandler player in playersOverride)
            {
                if (player != null && !players.Contains(player))
                    players.Add(player);
            }
        }

        if (players.Count == 0)
        {
            players.AddRange(FindObjectsByType<PlayerInputHandler>(
                FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        players.Sort((a, b) => a.Owner.CompareTo(b.Owner));

        vehicles.Clear();

        if (vehiclesOverride != null)
        {
            foreach (MountableVehicle vehicle in vehiclesOverride)
            {
                if (vehicle != null && !vehicles.Contains(vehicle))
                    vehicles.Add(vehicle);
            }
        }

        if (vehicles.Count == 0)
        {
            vehicles.AddRange(FindObjectsByType<MountableVehicle>(
                FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        PairVehiclesToRiders();
    }

    private void PairVehiclesToRiders()
    {
        riderOf.Clear();

        foreach (MountableVehicle vehicle in vehicles)
        {
            int rider = -1;
            string tag = vehicle.AllowedTag;

            if (!string.IsNullOrEmpty(tag))
            {
                for (int i = 0; i < players.Count; i++)
                {
                    if (!players[i].CompareTag(tag)) continue;

                    rider = i;
                    break;
                }    
            }

            if (rider < 0)
            {
                Debug.LogWarning(
                    $"[PlayerStartManager] '{vehicle.name}' only lets tag '{tag}' ride it, which " +
                    $"is not a tag any player carries, so it is left where the scene put it.",
                    vehicle);
            }

            riderOf.Add(rider);
        }
    }

    private void CollectZones()
    {
        zoneCentres.Clear();

        Vector3 total = Vector3.zero;

        foreach (NewspaperDelivery zone in NewspaperDelivery.All)
        {
            if (zone == null) continue;

            zoneCentres.Add(zone.transform.position);
            total += zone.transform.position;
        }

        zonesMiddle = zoneCentres.Count > 0 ? total / zoneCentres.Count : Vector3.zero;
    }

    private void RankCells()
    {
        rankedCells.Clear();
        cellScores.Clear();

        List<Vector2Int> roads = mapManager.GetRoadCells();

        foreach (Vector2Int cell in roads)
        {
            if (RoadLinks(cell) == 0) continue;

            cellScores[cell] = ScoreCell(cell);
            rankedCells.Add(cell);
        }

        rankedCells.Sort((a, b) => cellScores[a].CompareTo(cellScores[b]));
    }

    private float ScoreCell(Vector2Int cell)
    {
        Vector3 centre = mapManager.RoadCellToWorld(cell);

        float score;

        if (zoneCentres.Count == 0)
        {
            Vector3 town = mapManager.RoadCellToWorld(
                new Vector2Int(mapManager.width / 2, mapManager.height / 2));

            score = StreetDistance(centre, town);
        }
        else
        {
            float boy = MeanStreetDistance(centre, OwnerType.Boy);
            float witch = MeanStreetDistance(centre, OwnerType.Witch);

            score = Mathf.Abs(boy - witch) * fairnessWeight
                  + (boy + witch) * 0.5f * closenessWeight;
        }

        if (RoadLinks(cell) >= 3)
            score -= junctionBonus;

        return score;
    }

    private float MeanStreetDistance(Vector3 from, OwnerType owner)
    {
        float total = 0f;
        int counted = 0;

        foreach (NewspaperDelivery zone in NewspaperDelivery.All)
        {
            if (zone == null || zone.AllowedOwner != owner) continue;

            total += StreetDistance(from, zone.transform.position);
            counted++;
        }

        return counted == 0 ? 0f : total / counted;
    }

    private static float StreetDistance(Vector3 a, Vector3 b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    }

    private int RoadLinks(Vector2Int cell)
    {
        int links = 0;

        foreach (Vector2Int step in neighbourSteps)
        {
            if (mapManager.IsRoad(cell + step)) links++;
        }

        return links;
    }

    private Vector3 FacingFrom(Vector2Int cell)
    {
        Vector3 centre = mapManager.RoadCellToWorld(cell);

        Vector3 towards = Vector3.forward;

        if (zoneCentres.Count > 0)
        {
            Vector3 flat = Vector3.ProjectOnPlane(zonesMiddle - centre, Vector3.up);

            if (flat.sqrMagnitude > 0.001f)
                towards = flat.normalized;
        }

        Vector3 best = Vector3.zero;
        float bestDot = float.NegativeInfinity;

        foreach (Vector2Int step in neighbourSteps)
        {
            if (!mapManager.IsRoad(cell + step)) continue;

            Vector3 along = new Vector3(step.x, 0f, step.y);
            float dot = Vector3.Dot(along, towards);

            if (dot <= bestDot) continue;

            bestDot = dot;
            best = along;
        }

        return best == Vector3.zero ? towards : best;
    }
    #endregion

    #region Laying out
    private bool TryLayoutStart(Vector2Int cell)
    {
        playerSpots.Clear();
        vehicleSpots.Clear();

        vehiclesPerRider.Clear();

        for (int i = 0; i < players.Count; i++)
            vehiclesPerRider.Add(0);

        Vector3 centre = mapManager.RoadCellToWorld(cell);
        Vector3 forward = FacingFrom(cell);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);

        float halfLine = playerSpacing * (players.Count - 1) * 0.5f;

        for (int i = 0; i < players.Count; i++)
        {
            Vector3 guess = centre + right * (i * playerSpacing - halfLine);

            if (!TryGroundPoint(guess, out Vector3 ground)) return false;
            if (IsBlocked(ground)) return false;

            playerSpots.Add(new Spot
            {
                position = ground + Vector3.up * StandRise(players[i]),
                rotation = facing
            });
        }

        for (int v = 0; v < vehicles.Count; v++)
        {
            MountableVehicle vehicle = vehicles[v];
            int rider = riderOf[v];

            if (rider < 0)
            {
                vehicleSpots.Add(new Spot()
                {
                    position = vehicle.transform.position,
                    rotation = vehicle.transform.rotation
                });

                continue;
            }

            int slot = vehiclesPerRider[rider]++;

            Vector3 guess = playerSpots[rider].position
                          + forward * vehicleParkingOffset.y
                          + right * (vehicleParkingOffset.x + slot * vehicleSpacing);

            if (!TryGroundPoint(guess, out Vector3 ground)) return false;
            if (IsBlocked(ground)) return false;

            vehicleSpots.Add(new Spot
            {
                position = ground + Vector3.up * ParkedLift(vehicle),
                rotation = facing
            });
        }

        return true;
    }

    private static float StandRise(PlayerInputHandler player)
    {
        CharacterController controller = player.GetComponent<CharacterController>();

        if (controller == null) return 0f;

        return controller.height * 0.5f - controller.center.y + controller.skinWidth;
    }

    private float ParkedLift(MountableVehicle vehicle)
    {
        Vector3 here = vehicle.transform.position;

        if (TryGroundPoint(here, out Vector3 ground))
            return Mathf.Max(0f, here.y - ground.y);

        return fallbackVehicleLift;
    }

    private bool TryGroundPoint(Vector3 guess, out Vector3 ground)
    {
        float probe = groundProbeHeight > 0f ? groundProbeHeight : 6f;
        int layers = groundLayers.value != 0 ? groundLayers.value : Physics.DefaultRaycastLayers;

        Vector3 origin = guess + Vector3.up * probe;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probe * 2f,
                            layers, QueryTriggerInteraction.Ignore))
        {
            ground = hit.point;
            return true;
        }

        ground = guess;
        return false;
    }

    private bool IsBlocked(Vector3 ground)
    {
        if (obstacleLayers.value == 0 || clearanceRadius <= 0f) return false;

        Vector3 centre = ground + Vector3.up * (clearanceRadius + 0.05f);

        return Physics.CheckSphere(centre, clearanceRadius, obstacleLayers,
                                   QueryTriggerInteraction.Ignore);
    }
    #endregion

    #region Placing
    private void ApplyLayout()
    {
        for (int i = 0; i < vehicleSpots.Count; i++)
            PlaceVehicle(vehicles[i], vehicleSpots[i]);

        for (int i = 0; i < playerSpots.Count; i++)
            PlacePlayer(players[i], playerSpots[i]);

        Physics.SyncTransforms();
    }

    private void PlacePlayer(PlayerInputHandler player, Spot spot)
    {
        player.ForceDismount();

        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;

        if (wasEnabled) controller.enabled = false;

        player.transform.SetPositionAndRotation(spot.position, spot.rotation);

        if (wasEnabled) controller.enabled = true;

        Rigidbody body = player.GetComponent<Rigidbody>();

        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        PlayerMovement movement = player.GetComponent<PlayerMovement>();

        if (movement != null)
            movement.ResetFall();

        PlayersCameraController rig = player.CameraController;

        if (rig != null)
            rig.SnapToTarget();
    }

    private void PlaceVehicle(MountableVehicle vehicle, Spot spot)
    {
        Transform root = vehicle.transform;

        Vector3 from = root.position;
        Quaternion turn = spot.rotation * Quaternion.Inverse(root.rotation);

        root.SetPositionAndRotation(spot.position, spot.rotation);

        BicycleController bike = vehicle.GetComponent<BicycleController>();

        if (bike != null)
        {
            CarryOver(bike.sphereRB, from, spot.position, turn);
            CarryOver(bike.bicycleBody, from, spot.position, turn);
        }

        Physics.SyncTransforms();

        if (bike != null)
            bike.Park();

        FlyingBroomController broom = vehicle.GetComponent<FlyingBroomController>();

        if (broom != null)
            broom.Park();
    }

    private static void CarryOver(Rigidbody body, Vector3 from, Vector3 to, Quaternion turn)
    {
        if (body == null) return;

        Transform moved = body.transform;

        moved.SetPositionAndRotation(to + turn * (moved.position - from), turn * moved.rotation);

        if (body.isKinematic) return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }
    #endregion

    private static int ZonesPlaced()
    {
        int total = 0;

        foreach (OwnerType owner in TwoPlayerInputManager.Owners)
            total += DeliveryScoreManager.GetZoneCount(owner);

        return total;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawStartGizmo || !HasStarted) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(StartPoint, 2f);
    }
}
