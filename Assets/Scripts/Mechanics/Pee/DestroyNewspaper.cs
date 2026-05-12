using UnityEngine;

public class DestroyNewspaper : MonoBehaviour
{
    [SerializeField] GameObject newspaperDestroyedModel;

    private void OnParticleCollision(GameObject other)
    {
        if (other.CompareTag("Pee"))
        {
            this.gameObject.SetActive(false);
            newspaperDestroyedModel.SetActive(true);
        }

        /*    if (wasDelivered)
        {
            if (other.CompareTag("Pee"))
            {
            }
            else
                Debug.Log("Nothing to pee on!");
        }*/
    }
}
