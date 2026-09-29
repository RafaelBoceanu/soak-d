using UnityEngine;

[RequireComponent(typeof(GroundVehicleController))]
public class HandlebarSteering : MonoBehaviour
{
    [SerializeField] private GameObject handle, frame;
    [Tooltip("How far the handlebar and fork turn at full steering input")]
    [SerializeField] private float handleRotVal = 30f;
    [Tooltip("How quickly the handlebar and for follow the steering input")]
    [SerializeField] private float handleTurnSharpness = 12f;

    private GroundVehicleController vehicle;
    private Quaternion handleBaseRotation = Quaternion.identity, frameBaseRotation = Quaternion.identity;
    private float visualSteer;

    void Awake()
    {
        vehicle = GetComponent<GroundVehicleController>();

        if (handle != null) handleBaseRotation = handle.transform.localRotation;
        if (frame != null) frameBaseRotation = frame.transform.localRotation;
    }

    void OnEnable() => vehicle.OnParked += ResetSteering;
    void OnDisable() => vehicle.OnParked -= ResetSteering;

    void LateUpdate()
    {
        if (!vehicle.IsControlled) return;

        visualSteer = Mathf.Lerp(visualSteer, vehicle.SteerInput, 1f - Mathf.Exp(-handleTurnSharpness * Time.deltaTime));
        Apply(Quaternion.Euler(0f, handleRotVal * visualSteer, 0f));
    }

    void ResetSteering()
    {
        visualSteer = 0f;
        Apply(Quaternion.identity);
    }

    void Apply(Quaternion steerOffset)
    {
        if (handle != null) handle.transform.localRotation = handleBaseRotation * steerOffset;
        if (frame != null) frame.transform.localRotation = frameBaseRotation * steerOffset;
    }
}
