using System.Linq;
using UnityEngine;

public class NewspaperDelivery : MonoBehaviour
{
    [SerializeField] GameObject newspaperModel;
    [SerializeField] GameObject newspaperDestroyedModel;
    [SerializeField] AudioSource deliveredSound;

    public OwnerType allowedOwner;

    int deliveryScore;

    public bool wasDelivered = false;

    private void OnTriggerEnter(Collider other)
    {
        ThrownProjectile projectile = other.GetComponent<ThrownProjectile>();

        if (projectile == null)
            projectile = other.GetComponentInParent<ThrownProjectile>();

        if (!wasDelivered && projectile != null && projectile.owner == allowedOwner)
        {
            deliveredSound.Play();
            newspaperModel.SetActive(true);
            wasDelivered = true;

            Destroy(other.gameObject);
        }
        else
        {
            Debug.Log("Wrong delivery for this zone!");
        }

    }
}
