using UnityEngine;

[RequireComponent(typeof(GroundVehicleController))]
public class BicycleCranks : MonoBehaviour
{
    [SerializeField] private Transform cranksPivot;
    [Tooltip("Degrees the cranks turn for every unit the bike travels")]
    [SerializeField] private float crankDegreesPerUnit = 51f;

    private GroundVehicleController vehicle;
    private float crankAngle;

    void Awake() => vehicle = GetComponent<GroundVehicleController>();

    void LateUpdate()
    {
        if (cranksPivot == null || !vehicle.IsControlled) return;

        float forwardSpeed = Mathf.Max(0f, vehicle.velocity.z);

        crankAngle = Mathf.Repeat(crankAngle + forwardSpeed * crankDegreesPerUnit * Time.deltaTime, 360f);
        cranksPivot.localRotation = Quaternion.Euler(crankAngle, 0f, 0f);
    }
}
