using UnityEngine;

public class DestroyNewspaper : MonoBehaviour
{
    [SerializeField] GameObject newspaperDestroyedModel;

    NewspaperDelivery delivery;

    void Awake()
    {
        delivery = GetComponentInParent<NewspaperDelivery>();
    }

    private void OnParticleCollision(GameObject other)
    {
        if (!other.CompareTag("Pee"))
            return;

        if (delivery != null)
            delivery.NotifyNewspaperDestroyed();

        this.gameObject.SetActive(false);

        if (newspaperDestroyedModel != null)
            newspaperDestroyedModel.SetActive(true);
    }
}
