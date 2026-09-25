using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class VomitPuddle : MonoBehaviour
{
    private const int MaxPuddles = 12;
    private const int TextureSize = 128;

    private static readonly int BaseMapId = Shader.PropertyToID("Base_Map");
    private static readonly List<VomitPuddle> live = new List<VomitPuddle>();

    private static Material fallbackMaterial;
    private static Texture2D fallbackTexture;

    private DecalProjector projector;
    private float radius;
    private float aspect;
    private float delay;
    private float spreadSeconds;
    private float holdSeconds;
    private float fadeSeconds;
    private float age;
    private float depth;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => live.Clear();

    public static VomitPuddle Spawn(RaycastHit hit, float radius, float delay, float spreadSeconds,
                                    float holdSeconds, float fadeSeconds, Material material = null)
    {
        Material mat = material != null ? material : FallbackMaterial();

        if (mat == null)
            return null;

        GameObject go = new GameObject("VomitPuddle");

        Vector3 upHint = Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;

        go.transform.SetPositionAndRotation(hit.point, Quaternion.LookRotation(-hit.normal, upHint));
        go.transform.Rotate(0f, 0f, Random.Range(0f, 360f), Space.Self);

        DecalProjector projector = go.AddComponent<DecalProjector>();
        projector.pivot = Vector3.zero;
        projector.material = mat;
        projector.fadeFactor = 1f;

        VomitPuddle puddle = go.AddComponent<VomitPuddle>();
        puddle.projector = projector;
        puddle.radius = radius;
        puddle.aspect = 1f + Random.Range(-0.2f, 0.2f);
        puddle.delay = delay;
        puddle.spreadSeconds = Mathf.Max(0.01f, spreadSeconds);
        puddle.holdSeconds = holdSeconds;
        puddle.fadeSeconds = Mathf.Max(0.01f, fadeSeconds);
        puddle.depth = 0.4f;
        puddle.ApplySize(0f);

        live.Add(puddle);

        while (live.Count > MaxPuddles)
        {
            VomitPuddle oldest = live[0];
            live.RemoveAt(0);

            if (oldest != null)
                Destroy(oldest.gameObject);
        }

        return puddle;
    }

    void OnDestroy()
    {
        live.Remove(this);        
    }

    // Update is called once per frame
    void Update()
    {
        age += Time.deltaTime;

        float t = age - delay;

        if (t < 0f)
            return;

        if (t < spreadSeconds)
        {
            float s = t / spreadSeconds;
            ApplySize(1f - (1f - s) * (1f - s));
            return;
        }

        ApplySize(1f);

        if (holdSeconds <= 0f)
            return;

        float over = t - spreadSeconds - holdSeconds;

        if (over <= 0f)
            return;

        if (over >= fadeSeconds)
        {
            Destroy(gameObject);
            return;
        }

        projector.fadeFactor = 1f - over / fadeSeconds;
    }

    void ApplySize(float share)
    {
        float diameter = Mathf.Max(0.01f, radius * 2f * share);
        projector.size = new Vector3(diameter * aspect, diameter / aspect, depth);
    }

    static Material FallbackMaterial()
    {
        if (fallbackMaterial != null)
            return fallbackMaterial;

        Shader shader = Shader.Find("Shader Graphs/Decal");

        if (shader == null)
        {
            Debug.LogWarning("[VomitPuddle] The 'Shader Graphs/Decal' shader was not found, so vomit leaves " +
                             "no puddle");
            return null;
        }

        fallbackMaterial = new Material(shader) { name = "VomitPuddle (Runtime)" };
        fallbackMaterial.SetTexture(BaseMapId, FallbackTexture());

        return fallbackMaterial;
    }

    static Texture2D FallbackTexture()
    {
        if (fallbackTexture != null)
            return fallbackTexture;

        Color baseColour = new Color(0.52f, 0.58f, 0.16f);
        Color chunkColour = new Color(0.82f, 0.72f, 0.34f);
        Color darkColour = new Color(0.36f, 0.40f, 0.10f);

        Vector2[] splashes = new Vector2[6];
        float[] splashSizes = new float[splashes.Length];

        for (int i = 0; i < splashes.Length; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float distance = Random.Range(0.62f, 0.85f);
            splashes[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            splashSizes[i] = Random.Range(0.05f, 0.11f);
        }

        float seed = Random.Range(0f, 100f);

        Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = "VomitSplat (Runtime)",
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[TextureSize * TextureSize];

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                Vector2 p = new Vector2(x, y) / (TextureSize - 1) * 2f - Vector2.one;
                float r = p.magnitude;

                Vector2 dir = r > 0.0001f ? p / r : Vector2.right;
                float edge = 0.5f + 0.16f * Mathf.PerlinNoise(seed + dir.x * 1.6f, seed + dir.y * 1.6f)
                                  + 0.06f * Mathf.PerlinNoise(seed + dir.x * 5f, seed - dir.y * 5f);

                float alpha = Mathf.Clamp01((edge - r) / 0.05f);

                for (int i = 0; i < splashes.Length; i++)
                {
                    float d = Vector2.Distance(p, splashes[i]);
                    alpha = Mathf.Max(alpha, Mathf.Clamp01((splashSizes[i] - d) / 0.03f));
                }

                float shade = Mathf.PerlinNoise(seed + x * 0.06f, seed + y * 0.06f);
                Color colour = Color.Lerp(darkColour, baseColour, shade);

                float chunk = Mathf.PerlinNoise(seed * 2f + x * 0.22f, seed * 2f + y * 0.22f);
                if (chunk > 0.68f)
                    colour = Color.Lerp(colour, chunkColour, Mathf.Clamp01((chunk - 0.68f) / 0.08f));

                colour.a = alpha * 0.92f;
                pixels[y * TextureSize + x] = colour;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        fallbackTexture = texture;
        return fallbackTexture;
    }
}
