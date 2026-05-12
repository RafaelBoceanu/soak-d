using UnityEditor.Build;
using UnityEngine;

public class PeeSystem : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private string zipButton;
    [SerializeField] private string peeButton;

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

    private ParticleSystem.EmissionModule emission;
    private ParticleSystem.MainModule main;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        GameObject instance = Instantiate(peePrefab, spawnPoint.position, spawnPoint.rotation, spawnPoint);
        peeParticleSystem = instance.GetComponent<ParticleSystem>();

        emission = peeParticleSystem.emission;
        main = peeParticleSystem.main;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetButtonDown(zipButton))
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

            Debug.Log(zipButton + " zipper closed: " + zipperClosed);
        }

        float pee = playerNeeds.pee;
        float normalizedPee = pee / playerNeeds.maxPee;

        bool canStartPeeing = pee >= peeThreshold;
        bool hasPeeLeft = pee > minPeeValue;

        if (!zipperClosed && hasPeeLeft && (canStartPeeing || isPeeing))
        {
            if (Input.GetButton(peeButton))
            {
                StartPeeing();
                playerNeeds.Pee(1f * Time.deltaTime);
                UpdateVisuals(normalizedPee);
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
}
