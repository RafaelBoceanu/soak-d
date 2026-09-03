using UnityEngine;

public class PeeSystem : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Which player owns this character. Left at the default it is taken from the PlayerInputHandler on this object.")]
    [SerializeField] private OwnerType owner = OwnerType.Boy;

    [Header("Pee Settings")]
    [SerializeField] private float peeThreshold = 0.2f;
    [SerializeField] private float minPeeValue = 0.01f;

    private bool isPeeing = false;
    private bool zipperClosed = true;

    private ParticleSystem peeParticleSystem;

    [SerializeField] private GameObject peePrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private AudioSource zipperOpenSound;
    [SerializeField] private AudioSource zipperCloseSound;
    [SerializeField] private AudioSource peeSound;
    [SerializeField] private PlayerNeeds playerNeeds;
    [SerializeField] private ZipperCensor zipperCensor;
    [SerializeField] private PeePuddle peePuddle;

    private ParticleSystem.EmissionModule emission;
    private ParticleSystem.MainModule main;

    private PlayerInputContext Controls => TwoPlayerInputManager.GetPlayer(owner);

    void Awake()
    {
        PlayerInputHandler inputHandler = GetComponent<PlayerInputHandler>();

        if (inputHandler != null)
            owner = inputHandler.Owner;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        GameObject instance = Instantiate(peePrefab, spawnPoint.position, spawnPoint.rotation, spawnPoint);
        peeParticleSystem = instance.GetComponent<ParticleSystem>();

        emission = peeParticleSystem.emission;
        main = peeParticleSystem.main;

        if (zipperCensor == null)
        {
            zipperCensor = GetComponent<ZipperCensor>();
        }

        if (peePuddle == null)
        {
            peePuddle = GetComponent<PeePuddle>();
        }

        if (peePuddle == null)
        {
            Debug.LogWarning($"[PeeSystem] {name} has no PeePuddle assigned - no puddles for this player.", this);
        }

        UpdateCensor();
    }

    // Update is called once per frame
    void Update()
    {
        PlayerInputContext input = Controls;

        if (input == null)
        {
            return;
        }

        if (input.ZipPressed)
        {
            zipperClosed = !zipperClosed;

            if (!zipperClosed)
            {
                zipperOpenSound.Play();
                this.gameObject.GetComponent<PlayerMovement>().enabled = false;
                this.gameObject.GetComponentInChildren<Animator>().SetBool("isPeeing", true);
            }
            else
            {
                zipperCloseSound.Play();
                this.gameObject.GetComponent<PlayerMovement>().enabled = true;
                this.gameObject.GetComponentInChildren<Animator>().SetBool("isPeeing", false);
                StopPeeing();
            }
            UpdateCensor();

            Debug.Log($"{owner} zipper closed: {zipperClosed}");
        }

        float pee = playerNeeds.pee;
        float normalizedPee = pee / playerNeeds.maxPee;

        bool canStartPeeing = pee >= peeThreshold;
        bool hasPeeLeft = pee > minPeeValue;

        if (!zipperClosed && hasPeeLeft && (canStartPeeing || isPeeing))
        {
            if (input.Pee)
            {
                StartPeeing();
                playerNeeds.Pee(1f * Time.deltaTime);
                UpdateVisuals(normalizedPee);
                if (peePuddle != null)
                {
                    peePuddle.Grow(normalizedPee);
                }
            }
            else
            {
                StopPeeing();
            }
        }
        else
        {
            StopPeeing();
        }
    }

    void StartPeeing()
    {

        if (!isPeeing)
        {
            isPeeing = true;
            peeParticleSystem.Play();
            peeSound.Play();
        }
    }

    void StopPeeing()
    {
        if (isPeeing)
        {
            isPeeing = false;
            peeParticleSystem.Stop();
            peeSound.Stop();
            if (peePuddle != null)
            {
                peePuddle.EndPuddle();
            }
        }
    }

    void UpdateVisuals(float normalized)
    {
        // Emission (flow strength)
        float rate = Mathf.Lerp(30f, 320f, normalized);
        emission.rateOverTime = rate;

        // Speed (how far it shoots)
        float speed = Mathf.Lerp(3f, 9f, normalized);
        main.startSpeed = speed;

        // Size (stream thickness)
        float size = Mathf.Lerp(0.015f, 0.05f, normalized);
        main.startSize = size;

        // Gravity (more drop when weaker)
        float gravity = Mathf.Lerp(3f, 1.5f, normalized);
        main.gravityModifier = gravity;

        // Sound volume scaling
        peeSound.volume = Mathf.Lerp(0.2f, 1f, normalized);

        // Stream instability (more jitter when weaker)
        var noise = peeParticleSystem.noise;
        float jitter = Mathf.Lerp(0.1f, 0.5f, 1f - normalized);
        noise.strength = jitter;

        // Drippy effect when emptier
        main.startLifetime = Mathf.Lerp(0.3f, 1.0f, normalized);
    }

    void UpdateCensor()
    {
        if (zipperCensor != null)
        {
            zipperCensor.SetVisible(!zipperClosed);
        }    
    }
}
