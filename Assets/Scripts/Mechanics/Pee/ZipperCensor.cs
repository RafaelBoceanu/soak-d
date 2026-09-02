using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class ZipperCensor : MonoBehaviour
{
    public const string ShaderName = "Custom/CensorBlur";

    [Header("Placement")]
    [Tooltip("Where the censor square is centred. Usually the pee spawn point. Falls back to this transform.")]
    [SerializeField] private Transform anchor;
    [Tooltip("Extra offset from the anchor, in the anchor's local space.")]
    [SerializeField] private Vector3 localOffset = Vector3.zero;
    [Tooltip("Size of the censor square in meters.")]
    [SerializeField] private float size = 0.35f;
    [Tooltip("How far the square is pushed towards the camera so it is not hidden inside the character mesh.")]
    [SerializeField] private float cameraOffset = 0.12f;

    [Header("Censor Look")]
    [Tooltip("Mosaic block size in screen pixels. Bigger = chunkier.")]
    [SerializeField] private float pixelSize = 24f;
    [Tooltip("Blur radius in screen pixels.")]
    [SerializeField] private float blurRadius = 12f;
    [SerializeField] private Color tint = Color.white;
    [SerializeField, Range(0f, 1f)] private float opacity = 1f;

    [Header("Shader")]
    [Tooltip("Optional. Leave empty to look the censor shader up by name.")]
    [SerializeField] private Shader censorShader;

    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
    private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");
    private static readonly int TintId = Shader.PropertyToID("_Tint");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int CameraOffsetId = Shader.PropertyToID("_CameraOffset");

    private GameObject censorQuad;
    private Renderer censorRenderer;
    private Material censorMaterial;
    private bool visible;

    public bool IsVisible => visible;

    void Awake()
    {
        CreateCensorQuad();
        SetVisible(false);
    }

    void OnDestroy()
    {
        if (censorMaterial != null)
        {
            Destroy(censorMaterial);
            censorMaterial = null;
        }
    }

    void OnValidate()
    {
        if (Application.isPlaying && censorQuad != null)
        {
            ApplyTransform();
            ApplyMaterialProperties();
        }
    }

    public void SetVisible(bool value)
    {
        visible = value;

        if (censorQuad == null)
        {
            CreateCensorQuad();
        }

        if (censorRenderer != null)
        {
            censorRenderer.enabled = value;
        }
    }

    private void CreateCensorQuad()
    {
        Shader shader = censorShader != null ? censorShader : Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogWarning($"[ZipperCensor] Shader '{ShaderName}' not found, the open zipper area will not be censored.", this);
            return;
        }

        WarnIfOpaqueTextureDisabled();

        censorQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        censorQuad.name = "ZipperCensor";
        censorQuad.layer = gameObject.layer;

        Collider quadCollider = censorQuad.GetComponent<Collider>();
        if (quadCollider != null)
        {
            Destroy(quadCollider);
        }

        censorQuad.transform.SetParent(anchor != null ? anchor : transform, false);

        censorMaterial = new Material(shader) { name = "ZipperCensor (Instance)" };

        censorRenderer = censorQuad.GetComponent<Renderer>();
        censorRenderer.sharedMaterial = censorMaterial;
        censorRenderer.shadowCastingMode = ShadowCastingMode.Off;
        censorRenderer.receiveShadows = false;
        censorRenderer.lightProbeUsage = LightProbeUsage.Off;
        censorRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        ApplyTransform();
        ApplyMaterialProperties();
    }

    private void ApplyTransform()
    {
        censorQuad.transform.localPosition = localOffset;
        censorQuad.transform.localRotation = Quaternion.identity;

        censorQuad.transform.localScale = new Vector3(size, size, 1f);
    }
    
    private void ApplyMaterialProperties()
    {
        if (censorMaterial == null)
        {
            return;
        }

        censorMaterial.SetFloat(PixelSizeId, pixelSize);
        censorMaterial.SetFloat(BlurRadiusId, blurRadius);
        censorMaterial.SetColor(TintId, tint);
        censorMaterial.SetFloat(OpacityId, opacity);
        censorMaterial.SetFloat(CameraOffsetId, cameraOffset);
    }

    private void WarnIfOpaqueTextureDisabled()
    {
        UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null && !urpAsset.supportsCameraOpaqueTexture)
        {
            Debug.LogWarning($"[ZipperCensor] '{urpAsset.name}' has Opaque Texture disabled, the censor square has nothing to blur. " +
                             "Enable it on the render pipeline asset of the active quality level.", this);
        }
    }
}
