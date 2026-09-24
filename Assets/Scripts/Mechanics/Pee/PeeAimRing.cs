using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class PeeAimRing : MonoBehaviour
{
    [Header("Size")]
    [SerializeField] private DecalProjector projector;

    [Tooltip("Ring diameter in meters at the reference distance.")]
    [SerializeField, Min(0.05f)] private float baseSize = 0.55f;

    [Tooltip("Distance from the camera at which the ring is drawn at its base size.")]
    [SerializeField, Min(1f)] private float referenceDistance = 6f;

    [Tooltip("0 keeps the ring a fixed size in the world, 1 keeps it a fixed size on screen.")]
    [SerializeField, Range(0f, 1f)] private float distanceCompensation = 0.6f;

    [Tooltip("Smallest and largest diameter the ring is ever drawn at.")]
    [SerializeField] private Vector2 sizeClamp = new Vector2(0.35f, 1.6f);

    [SerializeField, Min(0f)] private float projectionDepth = 0.5f;

    [Header("Feedback")]
    [Tooltip("Extra diameter at full flow, as a share of the base size.")]
    [SerializeField, Range(0f, 1f)] private float flowSwell = 0.25f;

    [Tooltip("How much the ring throbs while the stream is running.")]
    [SerializeField, Range(0f, 0.5f)] private float pulseAmount = 0.07f;

    [SerializeField] private float pulseSpeed = 5f;

    [Tooltip("Degrees per second the ring turns on the ground. Idles at a quarter speed.")]
    [SerializeField] private float spinSpeed = 25f;

    [Header("On Target")]
    [Tooltip("Extra size while the stream is lined up on a pedestrian")]
    [SerializeField, Range(0f, 1f)] private float targetSwell = 0.3f;

    [Tooltip("How much faster the ring spins while the stream is lined up on a pedestrian")]
    [SerializeField, Min(1f)] private float targetSpinBoost = 4f;

    private float spin;

    void Awake()
    {
        if (projector == null)
            projector = GetComponent<DecalProjector>();

        if (projector == null)
        {
            Debug.LogError($"[PeeAimRing] {name} has no DecalProjector - the aim ring will not show.", this);
            return;
        }

        projector.pivot = Vector3.zero;
        Hide();
    }

    public void Hide()
    {
        if (projector != null)
            projector.enabled = false;
    }

    public void Place(Vector3 point, Vector3 normal, float flow, Vector3 viewerPosition, bool streaming) =>
        Place(point, normal, flow, viewerPosition, streaming, false);

    public void Place(Vector3 point, Vector3 normal, float flow, Vector3 viewerPosition, bool streaming, bool onTarget)
    {
        if (projector == null)
            return;

        projector.enabled = true;

        Vector3 upHint = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;

        float spinRate = streaming ? spinSpeed : spinSpeed * 0.25f;

        if (onTarget)
            spinRate *= targetSpinBoost;

        spin += spinRate * Time.deltaTime;

        // The projector shoots along its own forward, so it has to look into the surface.
        transform.SetPositionAndRotation(point, Quaternion.LookRotation(-normal, upHint));
        transform.Rotate(0f, 0f, spin, Space.Self);

        float distance = Vector3.Distance(viewerPosition, point);
        float scale = Mathf.Lerp(1f, distance / referenceDistance, distanceCompensation);
        float size = baseSize * scale * (1f + flowSwell * Mathf.Clamp01(flow));

        if (streaming)
            size *= 1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f);

        if (onTarget)
            size *= 1f + targetSwell;

        size = Mathf.Clamp(size, sizeClamp.x, sizeClamp.y);

        projector.size = new Vector3(size, size, projectionDepth);
    }
}