using UnityEngine;

public class PlayerNeeds : MonoBehaviour
{
    [Header("Player Needs")]
    public float maxHydration = 100f;
    public float maxPee = 100f;
    public float hydrationDecreaseRate = 5f;
    public float peeIncreaseRate = 25f;

    [HideInInspector] public float hydration;
    public float pee;

    public enum CharacterType { Boy, Witch }
    public CharacterType characterType;

    [SerializeField] private CanvasManager canvasManager;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        hydration = maxHydration;
        pee = 0f;
    }

    // Update is called once per frame
    void Update()
    {
        hydration = Mathf.Clamp(hydration - (hydrationDecreaseRate / 60f) * Time.deltaTime, 0f, maxHydration);
        pee = Mathf.Clamp(pee + (peeIncreaseRate / 60f) * Time.deltaTime, 0f, maxPee);

        if (canvasManager != null)
        {
            float hydrationNormalized = hydration / maxHydration;
            float peeNormalized = pee / maxPee;

            if (characterType == CharacterType.Boy)
            {
                canvasManager.SetBoyHydration(hydrationNormalized);
                canvasManager.SetBoyPee(peeNormalized);
            }
            else if (characterType == CharacterType.Witch)
            {
                canvasManager.SetWitchHydration(hydrationNormalized);
                canvasManager.SetWitchPee(peeNormalized);
            }
        }
    }

    public void Drink (float amount)
    {
        hydration = Mathf.Clamp(hydration + amount, 0f, maxHydration);
    }

    public void Pee (float amount)
    {
        pee = Mathf.Clamp(pee - amount, 0f, maxPee);
    }
}
