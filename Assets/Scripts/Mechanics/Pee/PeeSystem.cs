using UnityEngine;

public class PeeSystem : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Which player owns this character. Left at the default it is taken from the PlayerInputHandler on this object.")]
    [SerializeField] private OwnerType owner = OwnerType.Boy;

    [Header("Pee Settings")]
    [Tooltip("How full the bladder has to be before a stream will start, as a share of the bar.")]
    [SerializeField, Range(0f, 1f)] private float peeThreshold = 0.05f;

    [Tooltip("Share of the bar below which the stream gives out.")]
    [SerializeField, Range(0f, 1f)] private float minPeeValue = 0.01f;

    [Header("Aiming")]
    [Tooltip("Camera rig this player aims with. If empty, it is taken from the PlayerInputHandler.")]
    [SerializeField] private PlayersCameraController cameraRig;

    [Tooltip("Stream angle when the camera is looking as far up as it goes. If negative, points the arc upwards.")]
    [SerializeField] private float minPitch = -10f;

    [Tooltip("Stream angle when the camera is looking as far down as it goes. 90 deg is straigh at character's feet")]
    [SerializeField] private float maxPitch = 70f;

    [Tooltip("How quickly the stream swings to the aimed angle.")]
    [SerializeField] private float aimSharpness = 12f;

    [Tooltip("World ring showing where the stream will land.")]
    [SerializeField] private PeeAimRing aimRing;

    private bool isPeeing = false;
    private bool zipperClosed = true;
    private float currentFlow;
    private float currentPitch;
    private Quaternion spawnRestRotation;
    private bool aimApplied;

    private PlayerInputHandler inputHandler;
    private PlayerMovement playerMovement;

    private ParticleSystem peeParticleSystem;

    [SerializeField] private GameObject peePrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private AudioSource zipperOpenSound;
    [SerializeField] private AudioSource zipperCloseSound;
    [SerializeField] private AudioSource peeSound;
    [SerializeField] private PlayerNeeds playerNeeds;
    [SerializeField] private ZipperCensor zipperCensor;
    [SerializeField] private PeePuddle peePuddle;
    [SerializeField] private Animator animator;

    private ParticleSystem.EmissionModule emission;
    private ParticleSystem.MainModule main;

    private PlayerInputContext Controls => TwoPlayerInputManager.GetPlayer(owner);

    public float CurrentFlow => isPeeing ? currentFlow : 0f;
    public bool IsZipperOpen => !zipperClosed;

    void Awake()
    {
        inputHandler = GetComponent<PlayerInputHandler>();
        playerMovement = GetComponent<PlayerMovement>();
        
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
            Debug.LogWarning($"[PeeSystem] {name} found no Animator - peeing animation will not play.", this);

        if (inputHandler != null)
            owner = inputHandler.Owner;
    }

    void OnDisable()
    {
        CloseZipper(false);
    }

    private bool CanPee => playerMovement == null || playerMovement.CanPee;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        spawnRestRotation = spawnPoint.localRotation;
        currentPitch = AimPitch();
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

        if (inputHandler != null && inputHandler.IsRiding)
        {
            CancelPeeing();
            return;
        }

        if (playerNeeds != null && playerNeeds.IsHavingAccident)
        {
            CancelPeeing();
            return;
        }

        if (input.ZipPressed)
        {
            if (zipperClosed)
            {
                if (CanPee)
                {
                    OpenZipper();
                }
            }
            else
            {
                CloseZipper(true);
            }
        }

        if (!zipperClosed && !CanPee)
        {
            CloseZipper(true);
        }

        float normalizedPee = playerNeeds.maxPee > 0f ? playerNeeds.pee / playerNeeds.maxPee : 0f;

        bool canStartPeeing = normalizedPee >= peeThreshold;
        bool hasPeeLeft = normalizedPee > minPeeValue;

        if (!zipperClosed && hasPeeLeft && (canStartPeeing || isPeeing))
        {
            if (input.Pee)
            {
                StartPeeing();
                playerNeeds.Relieve(Time.deltaTime);
                currentFlow = normalizedPee * playerNeeds.FlowScale;
                UpdateVisuals(currentFlow);
                if (peePuddle != null)
                {
                    peePuddle.Grow(currentFlow);
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

    void LateUpdate()
    {
        if (spawnPoint == null)
            return;

        if (zipperClosed)
        {
            if (aimApplied)
            {
                spawnPoint.localRotation = spawnRestRotation;
                aimApplied = false;
            }

            UpdateAimMarker();
            return;
        }

        currentPitch = Mathf.Lerp(currentPitch, AimPitch(),
                                  1f - Mathf.Exp(-aimSharpness * Time.deltaTime));

        spawnPoint.rotation = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0f);
        aimApplied = true;

        UpdateAimMarker();
    }

    private PlayersCameraController Rig
    {
        get
        {
            if (cameraRig == null)
                cameraRig = inputHandler != null
                    ? inputHandler.CameraController
                    : PlayersCameraController.ForOwner(owner);

            return cameraRig;
        }
    }

    private float AimPitch()
    {
        PlayersCameraController rig = Rig;

        if (rig == null)
            return Mathf.Lerp(minPitch, maxPitch, 0.5f);

        float t = Mathf.InverseLerp(rig.MinPitch, rig.MaxPitch, rig.Pitch);

        return Mathf.Lerp(minPitch, maxPitch, t);
    }

    private float PredictedFlow()
    {
        if (isPeeing)
            return currentFlow;

        if (playerNeeds == null || playerNeeds.maxPee <= 0f)
            return 0f;

        return (playerNeeds.pee / playerNeeds.maxPee) * playerNeeds.FlowScale;
    }

    private void UpdateAimMarker()
    {
        if (aimRing == null)
            return;

        float flow = PredictedFlow();

        if (zipperClosed || peePuddle == null ||
            !peePuddle.TryPredictLanding(flow, out RaycastHit hit))
        {
            aimRing.Hide();
            return;
        }

        PlayersCameraController rig = Rig;
        Vector3 viewer = rig != null ? rig.transform.position : transform.position;

        aimRing.Place(hit.point, hit.normal, flow, viewer, isPeeing);
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
            currentFlow = 0f;
            peeParticleSystem.Stop();
            peeSound.Stop();
            if (peePuddle != null)
            {
                peePuddle.EndPuddle();
            }
        }
    }

    public void CancelPeeing()
    {
        CloseZipper(true);
    }

    private void OpenZipper()
    {
        zipperClosed = false;

        if (zipperOpenSound != null)
            zipperOpenSound.Play();

        if (playerMovement != null)
            playerMovement.SetPeeStance(true);

        currentPitch = AimPitch();

        PlayersCameraController rig = Rig;
        if (rig != null)
            rig.SetPeeView(true);

        if (animator != null)
            animator.SetBool("isPeeing", true);

        UpdateCensor();
    }

    private void CloseZipper(bool playSound)
    {
        StopPeeing();

        if (zipperClosed) return;

        zipperClosed = true;

        if (playerMovement != null)
            playerMovement.SetPeeStance(false);

        PlayersCameraController rig = Rig;
        if (rig != null)
            rig.SetPeeView(false);

        if (playSound && zipperCloseSound != null)
            zipperCloseSound.Play();

        if (animator != null)
            animator.SetBool("isPeeing", false);

        UpdateCensor();
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
