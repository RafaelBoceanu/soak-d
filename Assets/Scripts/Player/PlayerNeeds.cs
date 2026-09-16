using System;
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

    public enum NeedFailure { Dehydrated, BladderFull }

    public static event Action<OwnerType, NeedFailure> OnNeedCritical;

    public OwnerType Owner =>
        characterType == CharacterType.Boy ? OwnerType.Boy : OwnerType.Witch;

    private bool bladderReported;
    private bool dehydrationReported;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => OnNeedCritical = null;

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
        if (MatchManager.instance == null || MatchManager.instance.IsRunning)
        {
            hydration = Mathf.Clamp(hydration - (hydrationDecreaseRate / 60f) * Time.deltaTime, 0f, maxHydration);
            pee = Mathf.Clamp(pee + (peeIncreaseRate / 60f) * Time.deltaTime, 0f, maxPee);

            CheckCritical();
        }

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

    private void CheckCritical()
    {
        if (pee >= maxPee)
        {
            if (!bladderReported)
            {
                bladderReported = true;
                OnNeedCritical?.Invoke(Owner, NeedFailure.BladderFull);
            }
        }
        else if (pee < maxPee * 0.95f)
        {
            bladderReported = false;
        }

        if (hydration <= 0f)
        {
            if (!dehydrationReported)
            {
                dehydrationReported = true;
                OnNeedCritical?.Invoke(Owner, NeedFailure.Dehydrated);
            }
        }
        else if (hydration > maxHydration * 0.05f)
        {
            dehydrationReported = false;
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
