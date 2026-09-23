using System.Collections.Generic;
using UnityEngine;

public sealed class PedestrianGraph
{
    public struct Node
    {
        public Vector3 position;
        public Vector2Int cell;
        public int quadrant;
        public int firstEdge;
        public int edgeCount;
    }

    public struct Edge
    {
        public int from;
        public int to;
        public float length;
        public bool isCrossing;
        public int side;
    }

    static readonly Vector2Int[] directions =
    {
        new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0)
    };

    static readonly Vector2[] quadrantCorners =
    {
        new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f), new Vector2(-1f, 1f)
    };

    static readonly int[,] quadrantSides =
    {
        { 0, 1 }, // NE
        { 2, 1 }, // SE
        { 2, 3 }, // SW
        { 0, 3 }  // NW
    };

    static readonly int[,] sharedSides =
    {
        { 0, 3, 0 }, // NE - NW over the north side
        { 0, 1, 1 }, // NE - SE over the east side
        { 1, 2, 2 }, // SE - SW over the south side
        { 2, 3, 3 }  // SW - NW over the west side
    };

    static readonly int[] flipAlongZ = { 1, 0, 3, 2 };
    static readonly int[] flipAlongX = { 3, 2, 1, 0 };

    readonly List<Node> nodes = new List<Node>();
    readonly List<Edge> pending = new List<Edge>();
    readonly Dictionary<Vector3Int, int> lookup = new Dictionary<Vector3Int, int>();

    Edge[] edges = new Edge[0];
    float cellSize = 10f;
    int groundProbeMisses;

    public int NodeCount => nodes.Count;
    public int EdgeCount => edges.Length;
    public bool IsBuilt => nodes.Count > 0 && edges.Length > 0;
    public int GroundProbeMisses => groundProbeMisses;

    public Node GetNode(int index) => nodes[index];
    public Edge GetEdge(int index) => edges[index];
    public Vector3 NodePosition(int index) => nodes[index].position;

    #region Building
    public void Build(ProceduralMapManager map, LayerMask groundLayers, float probeHeight, float groundOffset)
    {
        nodes.Clear();
        pending.Clear();
        lookup.Clear();

        edges = new Edge[0];
        groundProbeMisses = 0;

        if (map == null)
        {
            Debug.LogError("[PedestrianGraph] No map managed, so there are no pavements to walk.");
            return;
        }

        cellSize = map.cellSize;

        List<Vector2Int> roadCells = map.GetRoadCells();

        foreach (Vector2Int cell in roadCells)
        {
            int links = CountLinks(map, cell);

            for (int quadrant = 0; quadrant < quadrantCorners.Length; quadrant++)
            {
                if (IsInsideOfBend(map, cell, quadrant, links)) continue;

                AddNode(map, cell, quadrant, groundLayers, probeHeight, groundOffset);
            }
        }

        foreach (Vector2Int cell in roadCells)
        {
            LinkWithinCell(map, cell);

            LinkToNeighbour(map, cell, 0);
            LinkToNeighbour(map, cell, 1);
        }

        Flatten();
    }

    void AddNode(ProceduralMapManager map, Vector2Int cell, int quadrant,
                 LayerMask groundLayers, float probeHeight, float groundOffset)
    {
        Vector3 centre = map.CellCentre(cell);
        Vector2 corner = quadrantCorners[quadrant];

        Vector3 position = new Vector3(
            centre.x + corner.x * map.LaneOffset,
            centre.y,
            centre.z + corner.y * map.LaneOffset);

        position.y = GroundHeight(position, map, groundLayers, probeHeight, groundOffset);

        lookup[Key(cell, quadrant)] = nodes.Count;

        nodes.Add(new Node
        {
            position = position,
            cell = cell,
            quadrant = quadrant
        });
    }

    float GroundHeight(Vector3 position, ProceduralMapManager map,
                       LayerMask groundLayers, float probeHeight, float groundOffset)
    {
        float probe = probeHeight > 0f ? probeHeight : 5f;
        int layers = groundLayers.value != 0 ? groundLayers.value : Physics.DefaultRaycastLayers;

        Vector3 origin = position + Vector3.up * probe;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probe * 2f,
                            layers, QueryTriggerInteraction.Ignore))
            return hit.point.y + groundOffset;

        groundProbeMisses++;

        return map.PavementTopY + groundOffset;
    }

    static int CountLinks(ProceduralMapManager map, Vector2Int cell)
    {
        int count = 0;

        for (int side = 0; side < directions.Length; side++)
        {
            if (map.IsRoad(cell + directions[side])) count++;
        }

        return count;
    }

    static bool IsInsideOfBend(ProceduralMapManager map, Vector2Int cell, int quadrant, int links)
    {
        if (links != 2) return false;

        return map.IsRoad(cell + directions[quadrantSides[quadrant, 0]])
            && map.IsRoad(cell + directions[quadrantSides[quadrant, 1]]);
    }

    void LinkWithinCell(ProceduralMapManager map, Vector2Int cell)
    {
        for (int pair = 0; pair < sharedSides.GetLength(0); pair++)
        {
            int a = sharedSides[pair, 0];
            int b = sharedSides[pair, 1];
            int side = sharedSides[pair, 2];

            if (!lookup.TryGetValue(Key(cell, a), out int from)) continue;
            if (!lookup.TryGetValue(Key(cell, b), out int to)) continue;

            bool acrossRoad = map.IsRoad(cell + directions[side]);

            if (acrossRoad && !map.HasCrosswalk(cell, side)) continue;

            AddEdge(from, to, acrossRoad, acrossRoad ? side : -1);
        }
    }

    void LinkToNeighbour(ProceduralMapManager map, Vector2Int cell, int side)
    {
        Vector2Int neighbour = cell + directions[side];

        if (!map.IsRoad(neighbour)) return;

        for (int quadrant = 0; quadrant < quadrantCorners.Length; quadrant++)
        {
            if (!SitsOnSide(quadrant, side)) continue;

            int mirrored = MirrorQuadrant(quadrant, side);

            if (!lookup.TryGetValue(Key(cell, quadrant), out int from)) continue;
            if (!lookup.TryGetValue(Key(neighbour, mirrored), out int to)) continue;

            AddEdge(from, to, false);
        }
    }

    static bool SitsOnSide(int quadrant, int side)
    {
        return quadrantSides[quadrant, 0] == side || quadrantSides[quadrant, 1] == side;
    }

    static int MirrorQuadrant(int quadrant, int side)
    {
        bool northSouth = side == 0 || side == 2;

        return northSouth ? flipAlongZ[quadrant] : flipAlongX[quadrant];
    }

    void AddEdge(int from, int to, bool crossing, int side = -1)
    {
        float length = Vector3.Distance(nodes[from].position, nodes[to].position);

        pending.Add(new Edge { from = from, to = to, length = length, isCrossing = crossing, side = side });
        pending.Add(new Edge { from = to, to = from, length = length, isCrossing = crossing, side = side });
    }

    void Flatten()
    {
        edges = new Edge[pending.Count];

        for (int i = 0; i < pending.Count; i++)
        {
            Node node = nodes[pending[i].from];
            node.edgeCount++;
            nodes[pending[i].from] = node;
        }

        int running = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            Node node = nodes[i];

            node.firstEdge = running;
            running += node.edgeCount;
            node.edgeCount = 0;

            nodes[i] = node;
        }

        for (int i = 0; i < pending.Count; i++)
        {
            Edge edge = pending[i];
            Node node = nodes[edge.from];

            edges[node.firstEdge + node.edgeCount] = edge;

            node.edgeCount++;
            nodes[edge.from] = node;
        }

        pending.Clear();
    }

    static Vector3Int Key(Vector2Int cell, int quadrant)
    {
        return new Vector3Int(cell.x, cell.y, quadrant);
    }
    #endregion

    #region Queries
    public int RandomNode()
    {
        return nodes.Count == 0 ? -1 : Random.Range(0, nodes.Count);
    }

    public bool TryNearestNode(Vector3 world, out int index)
    {
        index = -1;

        if (nodes.Count == 0) return false;

        float best = float.MaxValue;

        Vector2Int centre = new Vector2Int(
            Mathf.RoundToInt(world.x / cellSize),
            Mathf.RoundToInt(world.z / cellSize));

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                Vector2Int cell = new Vector2Int(centre.x + dx, centre.y + dz);

                for (int quadrant = 0; quadrant < quadrantCorners.Length; quadrant++)
                {
                    if (!lookup.TryGetValue(Key(cell, quadrant), out int candidate)) continue;

                    float distance = (nodes[candidate].position - world).sqrMagnitude;

                    if (distance >= best) continue;

                    best = distance;
                    index = candidate;
                }
            }
        }

        if (index >= 0) return true;

        for (int i = 0; i < nodes.Count; i++)
        {
            float distance = (nodes[i].position - world).sqrMagnitude;

            if (distance >= best) continue;

            best = distance;
            index = i;
        }

        return index >= 0;
    }

    public void CollectNodesNear(Vector3 centre, float radius, List<int> results)
    {
        results.Clear();

        if (nodes.Count == 0 || radius <= 0f) return;

        Vector2Int middle = new Vector2Int(
            Mathf.RoundToInt(centre.x / cellSize),
            Mathf.RoundToInt(centre.z / cellSize));

        int reach = Mathf.CeilToInt(radius / cellSize) + 1;
        float sqrRadius = radius * radius;

        for (int dx = -reach; dx <= reach; dx++)
        {
            for (int dz = -reach; dz <= reach; dz++)
            {
                Vector2Int cell = new Vector2Int(middle.x + dx, middle.y + dz);

                for (int quadrant = 0; quadrant < quadrantCorners.Length; quadrant++)
                {
                    if (!lookup.TryGetValue(Key(cell, quadrant), out int index)) continue;

                    Vector3 offset = nodes[index].position - centre;
                    offset.y = 0f;

                    if (offset.sqrMagnitude > sqrRadius) continue;

                    results.Add(index);
                }
            }
        }
    }
    
    public bool TryPickNext(int current, int previous, float crossingWeight, float backtrackWeight,
                            out int next, out bool crossing)
    {
        next = current;
        crossing = false;

        if (current < 0 || current >= nodes.Count) return false;

        Node node = nodes[current];

        if (node.edgeCount == 0) return false;

        float total = 0f;

        for (int i = 0; i < node.edgeCount; i++)
            total += Weight(edges[node.firstEdge + i], previous, crossingWeight, backtrackWeight);

        if (total <= 0f) return false;

        float roll = Random.value * total;

        for (int i = 0; i < node.edgeCount; i++)
        {
            Edge edge = edges[node.firstEdge + i];

            roll -= Weight(edge, previous, crossingWeight, backtrackWeight);

            if (roll > 0f) continue;

            next = edge.to;
            crossing = edge.isCrossing;

            return true;
        }

        Edge last = edges[node.firstEdge + node.edgeCount - 1];

        next = last.to;
        crossing = last.isCrossing;

        return true;
    }

    public bool TryGetEdge(int from, int to, out Edge edge)
    {
        edge = default;

        if (from <= 0 || from >= nodes.Count) return false;

        Node node = nodes[from];

        for (int i = 0; i < node.edgeCount; i++)
        {
            Edge candidate = edges[node.firstEdge + i];

            if (candidate.to != to) continue;

            edge = candidate;
            return true;
        }

        return false;
    }

    static float Weight(Edge edge, int previous, float crossingWeight, float backtrackWeight)
    {
        float weight = 1f;

        if (edge.isCrossing) weight *= crossingWeight;
        if (edge.to == previous) weight *= backtrackWeight;

        return Mathf.Max(weight, 0.0001f);
    }
    #endregion
}
