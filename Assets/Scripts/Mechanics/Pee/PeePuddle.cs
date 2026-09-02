using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class PeePuddle : MonoBehaviour
{
    [Header("Stream")]
    [Tooltip("Same transform as PeeSystem's Spawy Point. The stream flies along its forward axis.")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("What the stream can land on. Must exlcude the Player layer or the trace hits the character.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Stream Arc (match PeeSystem's UpdateVisuals)")]
    [SerializeField] private float minSpeed = 3f;
    [SerializeField] private float maxSpeed = 9f;
    [SerializeField] private float minGravity = 3f;
    [SerializeField] private float maxGravity = 1.5f;
    [Tooltip("Seconds of flight simulated per trace step. Smaller = more accurate, more raycasts.")]
    [SerializeField] private float traceStep = 0.06f;
    [SerializeField] private int maxTraceSteps = 40;

    [Header("Puddle")]
    [Tooltip("A material using the 'Shader Graphs/Decal' shader.")]
    [SerializeField] private Material puddleMaterial;
    [SerializeField] private float startRadius = 0.1f;
    [SerializeField] private float maxRadius = 0.9f;
    [Tooltip("Meters of radius gained per second at full flow.")]
    [SerializeField] private float growthPerSecond = 0.35f;
    [Tooltip("Depth of the projection box. Must be deep enough to reach the ground surface.")]
    [SerializeField] private float projectionDepth = 0.4f;
    [Tooltip("Landing points closer than this grow the current puddle instead of starting a new one.")]
    [SerializeField] private float mergeDistance = 0.5f;

    [Header("Variation")]
    [Tooltip("Blob textures to pick from. Leave empty to always use the material's own Base Map.")]
    [SerializeField] private Texture2D[] blobTextures;
    [Tooltip("How much a puddle can stretch on one axis. 0.25 = up to 25% wider or narrower.")]
    [SerializeField, Range(0f, 0.6f)] private float aspectJitter = 0.25f;
    [Tooltip("How much the maximum radius varies per puddle. 0.3 = 70% to 130% of Max Radius.")]
    [SerializeField, Range(0f, 0.6f)] private float sizeJitter = 0.3f;
    [Tooltip("Randomly mirror the blob, so rotation alone does not give it away.")]
    [SerializeField] private bool randomMirror = true;

    [Header("Lifetime")]
    [SerializeField] private int maxPuddles = 8;
    [Tooltip("Seconds a finished puddle stays at full opacity. 0 = never fades.")]
    [SerializeField] private float fadeDelay = 25f;
    [SerializeField] private float fadeDuration = 5f;

    private class Puddle
    {
        public DecalProjector Projector;
        public Material Material;
        public float Radius;
        public float MaxRadius;
        public float AspectX;
        public float AspectY;
        public float Age;
        public bool Finished;
    }

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private readonly List<Puddle> puddles = new List<Puddle>();
    private Puddle current;
    private bool warnedAboutSetup;

    void Update()
    {
        AgePuddles();
    }

    public void Grow(float flow)
    {
        if (puddleMaterial == null || spawnPoint == null)
        {
            if (!warnedAboutSetup)
            {
                warnedAboutSetup = true;
                Debug.LogError($"[PeePuddle] Not set up: Puddle Material is {(puddleMaterial == null ? "MISSING" : "ok")}, " +
                               $"Spawn Point is {(spawnPoint == null ? "MISSING" : "ok")}. No puddles will appear.", this);
            }
            return;
        }

        if (!TraceLandingPoint(flow, out RaycastHit hit))
        {
            EndPuddle();
            return;
        }

        if (current == null || Vector3.Distance(current.Projector.transform.position, hit.point) > mergeDistance)
        {
            EndPuddle();
            current = CreatePuddle(hit);
        }

        current.Radius = Mathf.Min(current.Radius + growthPerSecond * Mathf.Max(flow, 0.15f) * Time.deltaTime, current.MaxRadius);
        current.Age = 0f;
        ApplySize(current);
    }

    public void EndPuddle()
    {
        if (current != null)
        {
            current.Finished = true;
            current = null;
        }
    }

    private bool TraceLandingPoint(float flow, out RaycastHit hit)
    {
        float speed = Mathf.Lerp(minSpeed, maxSpeed, flow);
        float gravityModifier = Mathf.Lerp(minGravity, maxGravity, flow);

        Vector3 position = spawnPoint.position;
        Vector3 velocity = spawnPoint.forward * speed;
        Vector3 acceleration = Physics.gravity * gravityModifier;

        for (int i = 0; i < maxTraceSteps; i++)
        {
            Vector3 next = position + velocity * traceStep + 0.5f * acceleration * traceStep * traceStep;

            if (Physics.Linecast(position, next, out hit, groundMask, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            velocity += acceleration * traceStep;
            position = next;
        }

        hit = default;
        return false;
    }

    private Puddle CreatePuddle(RaycastHit hit)
    {
        GameObject go = new GameObject("PeePuddle");

        Vector3 projectDirection = -hit.normal;
        Vector3 upHint = Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;

        go.transform.SetPositionAndRotation(hit.point, Quaternion.LookRotation(projectDirection, upHint));
        go.transform.Rotate(0f, 0f, Random.Range(0f, 360f), Space.Self);

        DecalProjector projector = go.AddComponent<DecalProjector>();
        projector.pivot = Vector3.zero;
        projector.fadeFactor = 1f;

        Material instance = null;
        if (blobTextures != null && blobTextures.Length > 0)
        {
            Texture2D blob = blobTextures[Random.Range(0, blobTextures.Length)];
            if (blob != null)
            {
                instance = new Material(puddleMaterial) { name = "PuddleMat (Instance)" };
                instance.SetTexture(BaseMapId, blob);
            }
        }

        projector.material = instance != null ? instance : puddleMaterial;

        if (randomMirror)
        {
            float sx = Random.value < 0.5f ? -1f : 1f;
            float sy = Random.value < 0.5f ? -1f : 1f;
            projector.uvScale = new Vector2(sx, sy);
            projector.uvBias = new Vector2(sx < 0f ? 1f : 0f, sy < 0f ? 1f : 0f);
        }

        float stretch = Random.Range(-aspectJitter, aspectJitter);

        Puddle puddle = new Puddle
        {
            Projector = projector,
            Material = instance,
            Radius = startRadius,
            MaxRadius = maxRadius * Random.Range(1f - sizeJitter, 1f + sizeJitter),
            AspectX = 1f + stretch,
            AspectY = 1f - stretch,
            Age = 0f,
            Finished = false
        };

        ApplySize(puddle);

        puddles.Add(puddle);
        TrimOldest();

        return puddle;
    }

    private void ApplySize(Puddle puddle)
    {
        float diameter = puddle.Radius * 2f;
        puddle.Projector.size = new Vector3(diameter * puddle.AspectX, diameter * puddle.AspectY, projectionDepth);
    }

    private void AgePuddles()
    {
        if (fadeDelay <= 0f)
        {
            return;
        }

        for (int i = puddles.Count - 1; i >= 0; i--)
        {
            Puddle puddle = puddles[i];
            if (!puddle.Finished || puddle.Projector == null)
            {
                continue;
            }

            puddle.Age += Time.deltaTime;

            float over = puddle.Age - fadeDelay;
            if (over <= 0f)
            {
                continue;
            }

            if (over >= fadeDuration)
            {
                DestroyPuddle(puddle);
                puddles.RemoveAt(i);
                continue;
            }

            puddle.Projector.fadeFactor = 1f - (over / fadeDuration);
        }
    }

    private void TrimOldest()
    {
        while (puddles.Count > maxPuddles)
        {
            Puddle oldest = puddles[0];
            puddles.RemoveAt(0);

            if (oldest == current)
            {
                current = null;
            }

            DestroyPuddle(oldest);
        }
    }

    private void DestroyPuddle(Puddle puddle)
    {
        if (puddle.Projector != null)
        {
            Destroy(puddle.Projector.gameObject);
        }

        if (puddle.Material != null)
        {
            Destroy(puddle.Material);
        }
    }
}
