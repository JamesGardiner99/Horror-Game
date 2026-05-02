using System.Collections.Generic;
using UnityEngine;

public class ProceduralMapGenerator : MonoBehaviour
{
    public enum RoomType
    {
        Start,
        Normal,
        Objective,
        Exit
    }

    [Header("Map Settings")]
    [SerializeField] private int roomCount = 12;
    [SerializeField] private float roomSpacing = 12f;
    [SerializeField] private int objectiveRoomCount = 3;

    [Header("Prefabs")]
    [SerializeField] private GameObject startRoomPrefab;
    [SerializeField] private GameObject normalRoomPrefab;
    [SerializeField] private GameObject objectiveRoomPrefab;
    [SerializeField] private GameObject exitRoomPrefab;

    private readonly Dictionary<Vector2Int, RoomType> rooms = new();

    private static readonly Vector2Int[] directions =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private void Start()
    {
        GenerateMap();
        SpawnMap();
    }

    private void GenerateMap()
    {
        rooms.Clear();

        Vector2Int startPosition = Vector2Int.zero;
        rooms.Add(startPosition, RoomType.Start);

        List<Vector2Int> placedRooms = new()
        {
            startPosition
        };

        while (rooms.Count < roomCount)
        {
            Vector2Int randomRoom = placedRooms[Random.Range(0, placedRooms.Count)];
            Vector2Int randomDirection = directions[Random.Range(0, directions.Length)];

            Vector2Int newPosition = randomRoom + randomDirection;

            if (rooms.ContainsKey(newPosition))
                continue;

            rooms.Add(newPosition, RoomType.Normal);
            placedRooms.Add(newPosition);
        }

        AssignExitRoom();
        AssignObjectiveRooms();
    }

    private void AssignExitRoom()
    {
        Vector2Int farthestRoom = Vector2Int.zero;
        int farthestDistance = 0;

        foreach (Vector2Int position in rooms.Keys)
        {
            int distance = Mathf.Abs(position.x) + Mathf.Abs(position.y);

            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthestRoom = position;
            }
        }

        rooms[farthestRoom] = RoomType.Exit;
    }

    private void AssignObjectiveRooms()
    {
        List<Vector2Int> validRooms = new();

        foreach (var room in rooms)
        {
            if (room.Value == RoomType.Normal)
                validRooms.Add(room.Key);
        }

        int amountToPlace = Mathf.Min(objectiveRoomCount, validRooms.Count);

        for (int i = 0; i < amountToPlace; i++)
        {
            int randomIndex = Random.Range(0, validRooms.Count);
            Vector2Int roomPosition = validRooms[randomIndex];

            rooms[roomPosition] = RoomType.Objective;
            validRooms.RemoveAt(randomIndex);
        }
    }

    private void SpawnMap()
    {
        foreach (var room in rooms)
        {
            Vector3 worldPosition = new Vector3(
                room.Key.x * roomSpacing,
                0f,
                room.Key.y * roomSpacing
            );

            GameObject prefab = GetPrefabForRoomType(room.Value);

            if (prefab == null)
            {
                Debug.LogWarning($"Missing prefab for room type: {room.Value}");
                continue;
            }

            GameObject spawnedRoom = Instantiate(prefab, worldPosition, Quaternion.identity, transform);
            spawnedRoom.name = $"{room.Value}_Room_{room.Key.x}_{room.Key.y}";
        }
    }

    private GameObject GetPrefabForRoomType(RoomType roomType)
    {
        return roomType switch
        {
            RoomType.Start => startRoomPrefab,
            RoomType.Objective => objectiveRoomPrefab,
            RoomType.Exit => exitRoomPrefab,
            _ => normalRoomPrefab
        };
    }
}