using UnityEngine;

public class NewspaperDelivery : MonoBehaviour
{
    [SerializeField] GameObject newspaperModel;
    [SerializeField] GameObject newspaperDestroyedModel;
    [SerializeField] AudioSource deliveredSound;

    [Tooltip("When on, a paper destroyed by pee frees the zone so its owner can deliver again.")]
    [SerializeField] bool canRedeliverAfterDestroyed = false;

    [Tooltip("Log every projectile that enters this zone with the wrong owner.")]
    [SerializeField] bool logRejectedDeliveries = false;

    public OwnerType allowedOwner;

    public bool wasDelivered = false;

    bool registered;

    public OwnerType AllowedOwner => allowedOwner;

    public void Configure(OwnerType owner)
    {
        allowedOwner = owner;
        Register();
    }

    void Start()
    {
        Register();
    }

    private void OnDestroy()
    {
        if (!registered) return;

        registered = false;
        DeliveryScoreManager.UnregisterZone(allowedOwner);
    }

    void Register()
    {
        if (registered) return;

        registered = true;
        DeliveryScoreManager.RegisterZone(allowedOwner);
    }

    private void OnTriggerEnter(Collider other)
    {
        ThrownProjectile projectile = other.GetComponent<ThrownProjectile>();

        if (projectile == null)
            projectile = other.GetComponentInParent<ThrownProjectile>();

        if (projectile == null)
            return;

        if (wasDelivered || projectile.owner != allowedOwner)
        {
            if (logRejectedDeliveries)
                Debug.Log($"Wrong delivery for this zone! ({projectile.owner} projectile on a {allowedOwner} zone)", this);

            return;
        }

        Deliver();

        Destroy(other.gameObject);
    }

    void Deliver()
    {
        wasDelivered = true;

        if (deliveredSound != null)
            deliveredSound.Play();
        else
            Debug.LogWarning($"No delivered sound assigned to {name}", this);

        if (newspaperModel != null)
            newspaperModel.SetActive(true);

        if (newspaperDestroyedModel != null)
            newspaperDestroyedModel.SetActive(false);

        DeliveryScoreManager.ReportDelivered(allowedOwner);
    }

    public void NotifyNewspaperDestroyed()
    {
        if (!wasDelivered) return;

        DeliveryScoreManager.ReportDeliveryLost(allowedOwner);

        if (canRedeliverAfterDestroyed)
            wasDelivered = false;
    }
}
