using System;
using UnityEngine;

public class DeliveryScoreManager : MonoBehaviour
{
    static readonly int ownerCount = Enum.GetValues(typeof(OwnerType)).Length;

    static int[] zoneCounts = new int[ownerCount];
    static int[] scores = new int[ownerCount];
    static bool[] completed = new bool[ownerCount];

    public static event Action<OwnerType, int> OnScoreChanged;
    public static event Action<OwnerType> onOwnerCompleted;
    public static event Action<OwnerType, int> onZoneCountChanged;

    public static int GetScore(OwnerType owner) => scores[(int)owner];
    public static int GetZoneCount(OwnerType owner) => zoneCounts[(int)owner];
    public static void RegisterZone(OwnerType owner)
    {
        zoneCounts[(int)owner]++;
        onZoneCountChanged?.Invoke(owner, zoneCounts[(int)owner]);
    }

    public static void ReportDelivered(OwnerType owner)
    {
        scores[(int)owner]++;
        OnScoreChanged?.Invoke(owner, scores[(int)owner]);

        int index = (int)owner;

        if (!completed[index] && zoneCounts[index] > 0 && scores[index] >= zoneCounts[index])
        {
            completed[index] = true;
            onOwnerCompleted?.Invoke(owner);
        }
    }

    public static void ReportDeliveryLost(OwnerType owner)
    {
        scores[(int)owner] = Mathf.Max(0, scores[(int)owner] - 1);
        OnScoreChanged?.Invoke(owner, scores[(int)owner]);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetAll()
    {
        zoneCounts = new int[ownerCount];
        scores = new int[ownerCount];
        completed = new bool[ownerCount];
    }
}
