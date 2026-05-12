using System.Collections.Generic;
using Unity.VisualScripting;
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

    void OnEnable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated += GenerateZones;
    }

    void OnDisable()
    {
        if (mapManager != null)
            mapManager.OnHousesGenerated -= GenerateZones;
    }

    void GenerateZones(List<Transform> houses)
    {
        if (houses == null || houses.Count == 0)
        {
            Debug.LogError("No houses found");
            return;
        }

        Debug.Log("Houses received: " + houses.Count);

        Shuffle(houses);

        int index = 0;

        // Boy Zones
        for (int i = 0; i < boyZonesCount && index < houses.Count; i++, index++)
        {
            SpawnZone(
                houses[index],
                deliveryZoneBoyPrefab,
                OwnerType.Boy
            );
        }

        // Witch Zones
        for (int i = 0; i < witchZonesCount && index < houses.Count; i++, index++)
        {
            SpawnZone(
                houses[index],
                deliveryZoneWitchPrefab,
                OwnerType.Witch
            );
        }
    }

    void SpawnZone(Transform house, GameObject prefab, OwnerType owner)
    {
        Transform front = house.Find("Front");

        if (front == null)
        {
            Debug.LogError($"'Front' not found on {house.name}");
            return;
        }

        GameObject zone = Instantiate(
            prefab,
            front.position + front.up * 0.2f,
            front.rotation
        );

        zone.transform.SetParent(front);

        NewspaperDelivery delivery = zone.GetComponent<NewspaperDelivery>();
        if (delivery != null)
        {
            delivery.allowedOwner = owner;
        }
    }

    void Shuffle(List<Transform> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int rand = Random.Range(i, list.Count);

            var temp = list[i];
            list[i] = list[rand];
            list[rand] = temp;
        }
    }
}
