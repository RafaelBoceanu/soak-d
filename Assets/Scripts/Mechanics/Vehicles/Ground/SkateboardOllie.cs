using UnityEngine;

[RequireComponent(typeof(GroundVehicleController))]
public class SkateboardOllie : MonoBehaviour
{
    [Tooltip("Pivot at the tail of the board that tips the deck up on a hop")]
    [SerializeField] private Transform olliePivot;
    [Tooltip("Degrees the nose tips up at the height of the pop")]
    [SerializeField] private float ollieAngle = 22f;
    [Tooltip("Share of the tilt applied over the seconds since take-off")]
    [SerializeField]
    private AnimationCurve ollieCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.08f, 1f), new Keyframe(0.3f, 0f));

    private GroundVehicleController vehicle;
    private Quaternion baseRotation;
    private float startedAt = float.NegativeInfinity;

    void Awake()
    {
        vehicle = GetComponent<GroundVehicleController>();

        if (olliePivot != null) baseRotation = olliePivot.localRotation;
    }

    void OnEnable()
    {
        vehicle.OnHop += StartOllie;
        vehicle.OnParked += ResetBoard;
    }

    void OnDisable()
    {
        vehicle.OnHop -= StartOllie;
        vehicle.OnParked -= ResetBoard;
    }

    void StartOllie() => startedAt = Time.time;

    void ResetBoard()
    {
        startedAt = float.NegativeInfinity;

        if (olliePivot != null) olliePivot.localRotation = baseRotation;
    }

    void LateUpdate()
    {
        if (olliePivot == null) return;

        float elapsed = Time.time - startedAt;
        float duration = ollieCurve.length > 0 ? ollieCurve[ollieCurve.length - 1].time : 0f;
        float tilt = elapsed < duration ? ollieAngle * ollieCurve.Evaluate(elapsed) : 0f;

        olliePivot.localRotation = baseRotation * Quaternion.Euler(-tilt, 0f, 0f);
    }
}
