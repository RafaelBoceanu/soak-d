using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class InventoryPickup : MonoBehaviour
{
    [SerializeField] private InventoryItemType item = InventoryItemType.WaterBottle;
    [SerializeField, Min(1)] private int amount = 1;

    [Tooltip("Off, the pickup hides and comes back after the respawn delay.")]
    [SerializeField] private bool destroyOnPickup = true;
    [SerializeField, Min(0f)] private float respawnDelay = 10f;

    [Tooltip("Model to hide while the pickup is on cooldown.")]
    [SerializeField] private GameObject visuals;
    [SerializeField] private AudioSource pickupSound;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();

        if (inventory == null || inventory.Add(item, amount) <= 0)
            return;

        if (destroyOnPickup)
        {
            if (pickupSound != null && pickupSound.clip != null)
                AudioSource.PlayClipAtPoint(pickupSound.clip, transform.position, pickupSound.volume);

            Destroy(gameObject);
            return;
        }

        if (pickupSound != null)
            pickupSound.Play();

        StartCoroutine(Respawn());
    }

    IEnumerator Respawn()
    {
        SetPickupActive(false);
        yield return new WaitForSeconds(respawnDelay);
        SetPickupActive(true);
    }

    void SetPickupActive(bool active)
    {
        GetComponent<Collider>().enabled = active;

        if (visuals != null)
            visuals.SetActive(active);
    }
}
