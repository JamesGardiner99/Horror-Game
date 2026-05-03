using System.Collections.Generic;
using UnityEngine;

public class ProceduralMapGraphGenerator : MonoBehaviour
{
    public enum RoomShape
    {
        Small,
        Large,
        Corridor,
        Corner,
        TJunction,
        Cross,
        DeadEnd
    }

    public enum RoomTag
    {
        Spawn,
        Escape,
        Activation,
        ObjectiveCapable,
        LootCapable,
        DangerCapable,
        SafeRoom,
        MultiLevelCapable
    }

    public enum ConnectionType
    {
        Door,
        Stairs,
        Drop
    }

    [System.Serializable]
    public class Room
    {
        public int id;
        public Vector3Int position;
        public RoomShape shape;
        public List<Vector3Int> cells = new();
        public List<RoomTag> tags = new();
        public List<RoomConnection> connections = new();

        public int DistanceFromSpawn =>
            Mathf.Abs(position.x) + Mathf.Abs(position.y) + Mathf.Abs(position.z);
    }

    [System.Serializable]
    public class RoomConnection
    {
        public int fromRoomId;
        public int toRoomId;
        public ConnectionType type;
        public bool entityCanUse;
    }

    [Header("Generation")]
    [SerializeField] private int roomCount = 18;
    [SerializeField] private int maxFloors = 2;
    [SerializeField] private int stairsCount = 2;
    [SerializeField] private int dropCount = 2;

    [Header("Blockout")]
    [SerializeField] private bool buildBlockout = true;
    [SerializeField] private float roomCellSize = 4f;
    [SerializeField] private float wallHeight = 3f;
    [SerializeField] private float wallThickness = 0.25f;
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material ceilingMaterial;

    private Transform blockoutParent;

    [Header("Room Cell Shapes")]
    [SerializeField] private int smallRoomSize = 3;
    [SerializeField] private int largeRoomSize = 5;
    [SerializeField] private int corridorLength = 3;

    private readonly HashSet<Vector3Int> occupiedCells = new();

    [Header("Player Spawn")]
    [SerializeField] private Transform playerSpawnTarget;
    [SerializeField] private float playerSpawnHeight = 1f;

    [Header("Torch Spawn")]
    [SerializeField] private GameObject torchPrefab;
    [SerializeField] private float torchMinSpawnRadius = 1f;
    [SerializeField] private float torchMaxSpawnRadius = 5f;
    [SerializeField] private float torchSpawnHeight = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private float debugRoomSpacing = 6f;

    private readonly List<Room> rooms = new();
    private readonly Dictionary<Vector3Int, Room> roomLookup = new();

    private static readonly Vector3Int[] horizontalDirections =
    {
        Vector3Int.forward,
        Vector3Int.back,
        Vector3Int.left,
        Vector3Int.right
    };

    private void Start()
    {
        Debug.Log($"[MAP] Start called on {gameObject.name}. Active: {gameObject.activeInHierarchy}");

        if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            Debug.Log($"[MAP] Netcode found. IsServer: {Unity.Netcode.NetworkManager.Singleton.IsServer}, IsClient: {Unity.Netcode.NetworkManager.Singleton.IsClient}, IsHost: {Unity.Netcode.NetworkManager.Singleton.IsHost}");
        }
        else
        {
            Debug.Log("[MAP] No NetworkManager found.");
        }

        if (!generateOnStart)
        {
            Debug.LogWarning("[MAP] generateOnStart is false. Map will not generate automatically.");
            return;
        }

        Debug.Log("[MAP] Calling Generate()");
        Generate();
    }

    [ContextMenu("Generate Map Graph")]
    public void Generate()
    {
        Debug.Log("[MAP] Generate() started");

        rooms.Clear();
        roomLookup.Clear();

        CreateBaseLayout();
        AddExtraRoomConnections();
        AddVerticalConnections();
        AssignSpecialRooms();
        AssignRoomShapesAndTags();
        AddDropConnections();
        ValidateMap();

        GenerateRoomCells();

        if (buildBlockout)
            BuildBlockout();
        else
            Debug.LogWarning("[MAP] buildBlockout is false, so no geometry will be created.");

        MovePlayerToSpawnRoom();
        SpawnTorchNearPlayer();
    }

    private void CreateBaseLayout()
    {
        CreateRoom(Vector3Int.zero);

        while (rooms.Count < roomCount)
        {
            Room existingRoom = rooms[Random.Range(0, rooms.Count)];
            Vector3Int direction = horizontalDirections[Random.Range(0, horizontalDirections.Length)];
            Vector3Int newPosition = existingRoom.position + direction;

            if (roomLookup.ContainsKey(newPosition))
                continue;

            Room newRoom = CreateRoom(newPosition);

            ConnectRooms(existingRoom, newRoom, ConnectionType.Door, true);
        }
    }

    private void AddExtraRoomConnections()
    {
        float extraConnectionChance = 0.3f;
        int maxConnectionsPerRoom = 4;

        foreach (Room room in rooms)
        {
            foreach (Vector3Int direction in horizontalDirections)
            {
                Vector3Int neighbourPosition = room.position + direction;

                if (!roomLookup.TryGetValue(neighbourPosition, out Room neighbour))
                    continue;

                if (HasConnection(room, neighbour))
                    continue;

                if (room.connections.Count >= maxConnectionsPerRoom)
                    continue;

                if (neighbour.connections.Count >= maxConnectionsPerRoom)
                    continue;

                if (Random.value > extraConnectionChance)
                    continue;

                ConnectRooms(room, neighbour, ConnectionType.Door, true);
            }
        }
    }

    private void AddVerticalConnections()
    {
        int placed = 0;
        int attempts = 0;

        while (placed < stairsCount && attempts < 100)
        {
            attempts++;

            Room lowerRoom = rooms[Random.Range(0, rooms.Count)];

            if (lowerRoom.position.y >= maxFloors - 1)
                continue;

            Vector3Int upperPosition = lowerRoom.position + Vector3Int.up;

            if (roomLookup.ContainsKey(upperPosition))
                continue;

            Room upperRoom = CreateRoom(upperPosition);
            ConnectRooms(lowerRoom, upperRoom, ConnectionType.Stairs, true);

            placed++;
        }
    }

    private void AssignSpecialRooms()
    {
        Room spawnRoom = roomLookup[Vector3Int.zero];
        spawnRoom.tags.Add(RoomTag.Spawn);

        Room escapeRoom = GetFarthestRoomFrom(spawnRoom);
        escapeRoom.tags.Add(RoomTag.Escape);

        Room activationRoom = GetBestActivationRoom(spawnRoom, escapeRoom);
        activationRoom.tags.Add(RoomTag.Activation);
        activationRoom.tags.Add(RoomTag.ObjectiveCapable);
    }

    private void AssignRoomShapesAndTags()
    {
        foreach (Room room in rooms)
        {
            if (room.tags.Contains(RoomTag.Spawn) || room.tags.Contains(RoomTag.Escape))
            {
                room.shape = RoomShape.Large;
                room.tags.Add(RoomTag.SafeRoom);
                continue;
            }

            int connectionCount = room.connections.Count;

            if (connectionCount <= 1)
                room.shape = RoomShape.DeadEnd;
            else if (connectionCount == 2)
                room.shape = Random.value < 0.3f ? RoomShape.Corridor : RoomShape.Small;
            else if (connectionCount == 3)
                room.shape = RoomShape.TJunction;
            else
                room.shape = RoomShape.Cross;

            if (Random.value < 0.25f)
                room.tags.Add(RoomTag.LootCapable);

            if (Random.value < 0.25f)
                room.tags.Add(RoomTag.DangerCapable);

            if (room.shape == RoomShape.Cross || Random.value < 0.15f)
                room.tags.Add(RoomTag.MultiLevelCapable);

            if (Random.value < 0.2f)
                room.tags.Add(RoomTag.ObjectiveCapable);
        }
    }

    private void AddDropConnections()
    {
        int placed = 0;
        int attempts = 0;

        while (placed < dropCount && attempts < 100)
        {
            attempts++;

            Room upperRoom = rooms[Random.Range(0, rooms.Count)];

            if (upperRoom.position.y <= 0)
                continue;

            Vector3Int lowerPosition = upperRoom.position + Vector3Int.down;

            if (!roomLookup.TryGetValue(lowerPosition, out Room lowerRoom))
                continue;

            if (HasConnection(upperRoom, lowerRoom))
                continue;

            ConnectRoomsOneWay(upperRoom, lowerRoom, ConnectionType.Drop, false);
            placed++;
        }
    }

    private bool ValidateMap()
    {
        Room spawn = rooms.Find(r => r.tags.Contains(RoomTag.Spawn));
        Room activation = rooms.Find(r => r.tags.Contains(RoomTag.Activation));
        Room escape = rooms.Find(r => r.tags.Contains(RoomTag.Escape));

        bool spawnToActivation = CanReach(spawn, activation, allowDrops: false);
        bool activationToEscape = CanReach(activation, escape, allowDrops: false);

        if (!spawnToActivation || !activationToEscape)
        {
            Debug.LogWarning("Map validation failed. Critical path requires a drop or is disconnected.");
            return false;
        }

        Debug.Log("Map validation passed.");
        return true;
    }

    private void BuildBlockout()
    {
        Debug.Log($"[MAP] BuildBlockout() started. Room count: {rooms.Count}");

        if (blockoutParent != null)
            DestroyImmediate(blockoutParent.gameObject);

        blockoutParent = new GameObject("Generated Blockout").transform;
        blockoutParent.SetParent(transform);

        foreach (Room room in rooms)
        {
            foreach (Vector3Int cell in room.cells)
            {
                BuildCellFloor(cell);
                BuildCellCeiling(cell);
                BuildCellWalls(cell);
            }
        }

        Debug.Log($"[MAP] BuildBlockout() finished. Child count: {blockoutParent.childCount}");
    }

    private void GenerateRoomCells()
    {
        occupiedCells.Clear();

        foreach (Room room in rooms)
        {
            room.cells.Clear();

            switch (room.shape)
            {
                case RoomShape.Large:
                    AddSquareRoomCells(room, largeRoomSize);
                    break;

                case RoomShape.Corridor:
                    AddCorridorRoomCells(room, corridorLength);
                    break;

                case RoomShape.TJunction:
                    AddTJunctionCells(room);
                    break;

                case RoomShape.Cross:
                    AddCrossRoomCells(room);
                    break;

                case RoomShape.DeadEnd:
                case RoomShape.Small:
                case RoomShape.Corner:
                default:
                    AddSquareRoomCells(room, smallRoomSize);
                    break;
            }
        }

        Debug.Log($"[MAP] Generated room cells. Total occupied cells: {occupiedCells.Count}");
    }

    private void BuildRoomFloor(Room room)
    {
        Vector3 position = GetRoomWorldPosition(room);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = $"Floor_Room_{room.id}";
        floor.transform.SetParent(blockoutParent);
        floor.transform.position = position;

        floor.transform.localScale = new Vector3(roomCellSize, 0.2f, roomCellSize);

        ApplyMaterial(floor, floorMaterial);
    }

    private void BuildRoomCeiling(Room room)
    {
        Vector3 position = GetRoomWorldPosition(room);
        position.y += wallHeight;

        // Underside ceiling - visible from inside the room
        GameObject underside = GameObject.CreatePrimitive(PrimitiveType.Quad);
        underside.name = $"Ceiling_Underside_Room_{room.id}";
        underside.transform.SetParent(blockoutParent);
        underside.transform.position = position - Vector3.up * 0.01f;
        underside.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        underside.transform.localScale = new Vector3(roomCellSize, roomCellSize, 1f);
        ApplyMaterial(underside, ceilingMaterial);

        // Top side ceiling - visible from above
        GameObject topside = GameObject.CreatePrimitive(PrimitiveType.Quad);
        topside.name = $"Ceiling_Topside_Room_{room.id}";
        topside.transform.SetParent(blockoutParent);
        topside.transform.position = position + Vector3.up * 0.01f;
        topside.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        topside.transform.localScale = new Vector3(roomCellSize, roomCellSize, 1f);
        ApplyMaterial(topside, ceilingMaterial);
    }

    private void BuildRoomWalls(Room room)
    {
        foreach (Vector3Int direction in horizontalDirections)
        {
            Vector3Int neighbourPosition = room.position + direction;

            bool hasNeighbour = roomLookup.TryGetValue(neighbourPosition, out Room neighbour);
            bool hasDoorConnection = hasNeighbour && HasConnection(room, neighbour);

            if (hasDoorConnection)
                continue;

            CreateWall(room, direction);
        }
    }

    private void BuildCellFloor(Vector3Int cell)
    {
        Vector3 position = GetCellWorldPosition(cell);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = $"Floor_Cell_{cell.x}_{cell.y}_{cell.z}";
        floor.transform.SetParent(blockoutParent);
        floor.transform.position = position;
        floor.transform.localScale = new Vector3(roomCellSize, 0.2f, roomCellSize);

        ApplyMaterial(floor, floorMaterial);
    }

    private void BuildCellCeiling(Vector3Int cell)
    {
        Vector3 position = GetCellWorldPosition(cell);
        position.y += wallHeight;

        GameObject underside = GameObject.CreatePrimitive(PrimitiveType.Quad);
        underside.name = $"Ceiling_Underside_Cell_{cell.x}_{cell.y}_{cell.z}";
        underside.transform.SetParent(blockoutParent);
        underside.transform.position = position - Vector3.up * 0.01f;
        underside.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        underside.transform.localScale = new Vector3(roomCellSize, roomCellSize, 1f);
        ApplyMaterial(underside, ceilingMaterial);

        GameObject topside = GameObject.CreatePrimitive(PrimitiveType.Quad);
        topside.name = $"Ceiling_Topside_Cell_{cell.x}_{cell.y}_{cell.z}";
        topside.transform.SetParent(blockoutParent);
        topside.transform.position = position + Vector3.up * 0.01f;
        topside.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        topside.transform.localScale = new Vector3(roomCellSize, roomCellSize, 1f);
        ApplyMaterial(topside, ceilingMaterial);
    }

    private void BuildCellWalls(Vector3Int cell)
    {
        foreach (Vector3Int direction in horizontalDirections)
        {
            Vector3Int neighbourCell = cell + direction;

            if (occupiedCells.Contains(neighbourCell))
                continue;

            CreateCellWall(cell, direction);
        }
    }

    private void CreateWall(Room room, Vector3Int direction)
    {
        Vector3 roomPosition = GetRoomWorldPosition(room);

        Vector3 wallPosition = roomPosition;
        Vector3 wallScale;

        if (direction == Vector3Int.forward)
        {
            wallPosition += new Vector3(0f, wallHeight / 2f, roomCellSize / 2f);
            wallScale = new Vector3(roomCellSize, wallHeight, wallThickness);
        }
        else if (direction == Vector3Int.back)
        {
            wallPosition += new Vector3(0f, wallHeight / 2f, -roomCellSize / 2f);
            wallScale = new Vector3(roomCellSize, wallHeight, wallThickness);
        }
        else if (direction == Vector3Int.right)
        {
            wallPosition += new Vector3(roomCellSize / 2f, wallHeight / 2f, 0f);
            wallScale = new Vector3(wallThickness, wallHeight, roomCellSize);
        }
        else
        {
            wallPosition += new Vector3(-roomCellSize / 2f, wallHeight / 2f, 0f);
            wallScale = new Vector3(wallThickness, wallHeight, roomCellSize);
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = $"Wall_Room_{room.id}_{direction}";
        wall.transform.SetParent(blockoutParent);
        wall.transform.position = wallPosition;
        wall.transform.localScale = wallScale;

        ApplyMaterial(wall, wallMaterial);
    }

    private void CreateCellWall(Vector3Int cell, Vector3Int direction)
    {
        Vector3 cellPosition = GetCellWorldPosition(cell);

        Vector3 wallPosition = cellPosition;
        Vector3 wallScale;

        if (direction == Vector3Int.forward)
        {
            wallPosition += new Vector3(0f, wallHeight / 2f, roomCellSize / 2f);
            wallScale = new Vector3(roomCellSize, wallHeight, wallThickness);
        }
        else if (direction == Vector3Int.back)
        {
            wallPosition += new Vector3(0f, wallHeight / 2f, -roomCellSize / 2f);
            wallScale = new Vector3(roomCellSize, wallHeight, wallThickness);
        }
        else if (direction == Vector3Int.right)
        {
            wallPosition += new Vector3(roomCellSize / 2f, wallHeight / 2f, 0f);
            wallScale = new Vector3(wallThickness, wallHeight, roomCellSize);
        }
        else
        {
            wallPosition += new Vector3(-roomCellSize / 2f, wallHeight / 2f, 0f);
            wallScale = new Vector3(wallThickness, wallHeight, roomCellSize);
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = $"Wall_Cell_{cell.x}_{cell.y}_{cell.z}_{direction}";
        wall.transform.SetParent(blockoutParent);
        wall.transform.position = wallPosition;
        wall.transform.localScale = wallScale;

        ApplyMaterial(wall, wallMaterial);
    }

    private Room CreateRoom(Vector3Int position)
    {
        Room room = new Room
        {
            id = rooms.Count,
            position = position,
            shape = RoomShape.Small
        };

        rooms.Add(room);
        roomLookup.Add(position, room);

        Debug.Log($"[MAP] Created room {room.id} at {room.position}");

        return room;
    }

    private void ConnectRooms(Room a, Room b, ConnectionType type, bool entityCanUse)
    {
        ConnectRoomsOneWay(a, b, type, entityCanUse);
        ConnectRoomsOneWay(b, a, type, entityCanUse);
    }

    private void ConnectRoomsOneWay(Room from, Room to, ConnectionType type, bool entityCanUse)
    {
        from.connections.Add(new RoomConnection
        {
            fromRoomId = from.id,
            toRoomId = to.id,
            type = type,
            entityCanUse = entityCanUse
        });
    }

    private bool HasConnection(Room a, Room b)
    {
        foreach (RoomConnection connection in a.connections)
        {
            if (connection.toRoomId == b.id)
                return true;
        }

        return false;
    }

    private Room GetFarthestRoomFrom(Room origin)
    {
        Room farthest = origin;
        int farthestDistance = -1;

        foreach (Room room in rooms)
        {
            int distance = Mathf.Abs(room.position.x - origin.position.x)
                         + Mathf.Abs(room.position.y - origin.position.y)
                         + Mathf.Abs(room.position.z - origin.position.z);

            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthest = room;
            }
        }

        return farthest;
    }

    private Room GetBestActivationRoom(Room spawn, Room escape)
    {
        Room bestRoom = spawn;
        float bestScore = float.MinValue;

        foreach (Room room in rooms)
        {
            if (room == spawn || room == escape)
                continue;

            int distanceFromSpawn = Mathf.Abs(room.position.x - spawn.position.x)
                                  + Mathf.Abs(room.position.y - spawn.position.y)
                                  + Mathf.Abs(room.position.z - spawn.position.z);

            int distanceFromEscape = Mathf.Abs(room.position.x - escape.position.x)
                                   + Mathf.Abs(room.position.y - escape.position.y)
                                   + Mathf.Abs(room.position.z - escape.position.z);

            float score = distanceFromSpawn - Mathf.Abs(distanceFromSpawn - distanceFromEscape) * 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                bestRoom = room;
            }
        }

        return bestRoom;
    }

    private bool CanReach(Room start, Room target, bool allowDrops)
    {
        HashSet<int> visited = new();
        Queue<Room> queue = new();

        visited.Add(start.id);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Room current = queue.Dequeue();

            if (current == target)
                return true;

            foreach (RoomConnection connection in current.connections)
            {
                if (!allowDrops && connection.type == ConnectionType.Drop)
                    continue;

                if (visited.Contains(connection.toRoomId))
                    continue;

                visited.Add(connection.toRoomId);
                queue.Enqueue(rooms[connection.toRoomId]);
            }
        }

        return false;
    }

    private void OnDrawGizmos()
    {
        if (rooms == null)
            return;

        foreach (Room room in rooms)
        {
            Vector3 worldPosition = new Vector3(
                room.position.x * debugRoomSpacing,
                room.position.y * debugRoomSpacing,
                room.position.z * debugRoomSpacing
            );

            Gizmos.color = GetRoomColour(room);
            Gizmos.DrawCube(worldPosition, Vector3.one * 1.5f);

            foreach (RoomConnection connection in room.connections)
            {
                Room target = rooms[connection.toRoomId];

                Vector3 targetPosition = new Vector3(
                    target.position.x * debugRoomSpacing,
                    target.position.y * debugRoomSpacing,
                    target.position.z * debugRoomSpacing
                );

                Gizmos.color = GetConnectionColour(connection.type);
                Gizmos.DrawLine(worldPosition, targetPosition);
            }
        }
    }

    private Color GetRoomColour(Room room)
    {
        if (room.tags.Contains(RoomTag.Spawn))
            return Color.green;

        if (room.tags.Contains(RoomTag.Escape))
            return Color.red;

        if (room.tags.Contains(RoomTag.Activation))
            return Color.yellow;

        if (room.tags.Contains(RoomTag.MultiLevelCapable))
            return Color.cyan;

        return Color.gray;
    }

    private Color GetConnectionColour(ConnectionType type)
    {
        return type switch
        {
            ConnectionType.Stairs => Color.blue,
            ConnectionType.Drop => Color.magenta,
            _ => Color.white
        };
    }

    private Vector3 GetRoomWorldPosition(Room room)
    {
        return new Vector3(
            room.position.x * roomCellSize,
            room.position.y * wallHeight,
            room.position.z * roomCellSize
        );
    }

    private void ApplyMaterial(GameObject obj, Material material)
    {
        if (material == null)
            return;

        Renderer renderer = obj.GetComponent<Renderer>();

        if (renderer != null)
            renderer.sharedMaterial = material;
    }

    private void MovePlayerToSpawnRoom()
    {
        if (playerSpawnTarget == null)
        {
            Debug.LogWarning("[MAP] No playerSpawnTarget assigned.");
            return;
        }

        Room spawnRoom = rooms.Find(r => r.tags.Contains(RoomTag.Spawn));

        if (spawnRoom == null)
        {
            Debug.LogWarning("[MAP] No spawn room found.");
            return;
        }

        Vector3 spawnPosition = GetRoomWorldPosition(spawnRoom);
        spawnPosition.y += playerSpawnHeight;

        playerSpawnTarget.position = spawnPosition;

        Debug.Log($"[MAP] Moved player to spawn room at {spawnPosition}");
    }

    private void SpawnTorchNearPlayer()
    {
        if (torchPrefab == null)
        {
            Debug.LogWarning("[MAP] No torchPrefab assigned.");
            return;
        }

        if (playerSpawnTarget == null)
        {
            Debug.LogWarning("[MAP] No playerSpawnTarget assigned, cannot spawn torch.");
            return;
        }

        Vector2 randomCircle = Random.insideUnitCircle.normalized;
        float distance = Random.Range(torchMinSpawnRadius, torchMaxSpawnRadius);

        Vector3 offset = new Vector3(
            randomCircle.x * distance,
            0f,
            randomCircle.y * distance
        );

        Vector3 spawnPosition = playerSpawnTarget.position + offset;
        spawnPosition.y = GetFloorYForPlayer() + torchSpawnHeight;

        Instantiate(torchPrefab, spawnPosition, Quaternion.identity);

        Debug.Log($"[MAP] Spawned torch near player at {spawnPosition}");
    }

    private float GetFloorYForPlayer()
    {
        Room spawnRoom = rooms.Find(r => r.tags.Contains(RoomTag.Spawn));

        if (spawnRoom == null)
            return 0f;

        return spawnRoom.position.y * wallHeight;
    }

    private void AddSquareRoomCells(Room room, int size)
    {
        int half = size / 2;

        for (int x = -half; x <= half; x++)
        {
            for (int z = -half; z <= half; z++)
            {
                AddCellToRoom(room, new Vector3Int(
                    room.position.x * largeRoomSize + x,
                    room.position.y,
                    room.position.z * largeRoomSize + z
                ));
            }
        }
    }

    private void AddCorridorRoomCells(Room room, int length)
    {
        Vector3Int direction = GetPrimaryRoomDirection(room);

        int half = length / 2;

        for (int i = -half; i <= half; i++)
        {
            AddCellToRoom(room, new Vector3Int(
                room.position.x * largeRoomSize + direction.x * i,
                room.position.y,
                room.position.z * largeRoomSize + direction.z * i
            ));
        }
    }

    private void AddCrossRoomCells(Room room)
    {
        Vector3Int center = new Vector3Int(
            room.position.x * largeRoomSize,
            room.position.y,
            room.position.z * largeRoomSize
        );

        AddCellToRoom(room, center);

        foreach (Vector3Int direction in horizontalDirections)
        {
            AddCellToRoom(room, center + direction);
            AddCellToRoom(room, center + direction * 2);
        }
    }

    private void AddTJunctionCells(Room room)
    {
        Vector3Int center = new Vector3Int(
            room.position.x * largeRoomSize,
            room.position.y,
            room.position.z * largeRoomSize
        );

        AddCellToRoom(room, center);

        int addedDirections = 0;

        foreach (RoomConnection connection in room.connections)
        {
            if (connection.type != ConnectionType.Door)
                continue;

            Room target = rooms[connection.toRoomId];
            Vector3Int direction = GetDirectionBetweenRooms(room, target);

            AddCellToRoom(room, center + direction);
            AddCellToRoom(room, center + direction * 2);

            addedDirections++;

            if (addedDirections >= 3)
                break;
        }
    }

    private void AddCellToRoom(Room room, Vector3Int cell)
    {
        if (occupiedCells.Contains(cell))
            return;

        room.cells.Add(cell);
        occupiedCells.Add(cell);
    }

    private Vector3Int GetPrimaryRoomDirection(Room room)
    {
        foreach (RoomConnection connection in room.connections)
        {
            if (connection.type != ConnectionType.Door)
                continue;

            Room target = rooms[connection.toRoomId];
            return GetDirectionBetweenRooms(room, target);
        }

        return Vector3Int.forward;
    }

    private Vector3Int GetDirectionBetweenRooms(Room from, Room to)
    {
        Vector3Int delta = to.position - from.position;

        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.z))
            return delta.x > 0 ? Vector3Int.right : Vector3Int.left;

        return delta.z > 0 ? Vector3Int.forward : Vector3Int.back;
    }

    private Vector3 GetCellWorldPosition(Vector3Int cell)
    {
        return new Vector3(
            cell.x * roomCellSize,
            cell.y * wallHeight,
            cell.z * roomCellSize
        );
    }
}