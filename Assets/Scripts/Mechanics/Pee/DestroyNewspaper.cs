using UnityEngine;

public class DestroyNewspaper : MonoBehaviour
{
    NewspaperDelivery delivery;

    public void Bind(NewspaperDelivery owner)
    {
        delivery = GetComponentInParent<NewspaperDelivery>();
    }

    private void OnParticleCollision(GameObject other)
    {
        if (!other.CompareTag("Pee"))
            return;

        if (delivery != null)
            delivery.NotifyNewspaperDestroyed();
    }
}
