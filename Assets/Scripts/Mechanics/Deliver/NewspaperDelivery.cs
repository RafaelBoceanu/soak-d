using System.Collections;
using UnityEngine;

public class NewspaperDelivery : MonoBehaviour
{
    [SerializeField] GameObject newspaperModel;
    [SerializeField] GameObject newspaperDestroyedModel;
    [SerializeField] AudioSource deliveredSound;

    [Tooltip("When on, a paper destroyed by pee frees the zone so its owner can deliver again.")]
    [SerializeField] bool canRedeliverAfterDestroyed = false;

    [Tooltip("Seconds the soaked newspaper stays visible before the zone goes back to looking empty")]
    [SerializeField] float destroyedModelVisibleSeconds = 3f;

    [Tooltip("Log every projectile that enters this zone with the wrong owner.")]
    [SerializeField] bool logRejectedDeliveries = false;

    [Header("Feedback")]
    [SerializeField] ParticleSystem deliveredEffect;
    [SerializeField] ParticleSystem destroyedEffect;

    [Header("Zone marker")]
    [SerializeField] GameObject zoneMarker;

    public OwnerType allowedOwner;

    public bool wasDelivered = false;

    bool registered;

    Coroutine hideDestroyedRoutine;

    public OwnerType AllowedOwner => allowedOwner;

    public void Configure(OwnerType owner)
    {
        allowedOwner = owner;
        Register();
    }

    void Awake()
    {
        if (newspaperModel == null) return;

        DestroyNewspaper destroyer = newspaperModel.GetComponent<DestroyNewspaper>();

        if (destroyer != null)
            destroyer.Bind(this);
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

        if (deliveredEffect != null)
            deliveredEffect.Play();

        PlayersCameraController.Shake(allowedOwner, 0.30f);

        if (zoneMarker != null)
            zoneMarker.SetActive(false);

        if (newspaperModel != null)
            newspaperModel.SetActive(true);

        StopHideDestroyedRoutine();

        if (newspaperDestroyedModel != null)
            newspaperDestroyedModel.SetActive(false);

        DeliveryScoreManager.ReportDelivered(allowedOwner);
    }

    public void NotifyNewspaperDestroyed()
    {
        if (!wasDelivered) return;

        DeliveryScoreManager.ReportDeliveryLost(allowedOwner);
        PlayersCameraController.Shake(allowedOwner, 0.45f);

        if (destroyedEffect != null)
            destroyedEffect.Play();

        if (newspaperModel != null)
            newspaperModel.SetActive(false);

        if (newspaperDestroyedModel != null)
            newspaperDestroyedModel.SetActive(true);

        if (canRedeliverAfterDestroyed)
        {
            wasDelivered = false;

            if (zoneMarker != null)
                zoneMarker.SetActive(true);

            StopHideDestroyedRoutine();
            hideDestroyedRoutine = StartCoroutine(HideDestroyedModelAfterDelay());
        }
    }

    void StopHideDestroyedRoutine()
    {
        if (hideDestroyedRoutine == null) return;

        StopCoroutine(hideDestroyedRoutine);
        hideDestroyedRoutine = null;
    }

    IEnumerator HideDestroyedModelAfterDelay()
    {
        yield return new WaitForSeconds(destroyedModelVisibleSeconds);

        if (newspaperDestroyedModel != null)
            newspaperDestroyedModel.SetActive(false);

        hideDestroyedRoutine = null;
    }
}
