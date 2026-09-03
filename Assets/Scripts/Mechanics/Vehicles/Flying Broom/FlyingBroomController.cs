using UnityEngine;

public class FlyingBroomController : MonoBehaviour
{
    [Header("Flying Broom Stats")]
    [Tooltip("How much the throttle ramps up or down")]
    public float throttleIncrement = 0.1f;
    [Tooltip("Maximum broom speed when at 100% throttle")]
    public float maxThrust = 200f;
    [Tooltip("How responsive the broom is when rolling, pitching, and yawing")]
    public float responsiveness = 10f;

    [Header("Parking")]
    [Tooltip("Freeze the broom in place once the rider gets off, so it doesn't drift away")]
    public bool freezeWhenParked = true;
    [Tooltip("Level the broom ot when it is parked, instead of it staying tilted")]
    public bool levelWhenParked = true;

    private float throttle;
    private float roll;
    private float pitch;
    private float yaw;

    private bool throttleUp;
    private bool throttleDown;
    private bool isControlled = false;

    private Rigidbody rb;

    private float responseModifier
    {
        get { return (rb.mass / 10f) * responsiveness; }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void SetControl(bool active)
    {
        isControlled = active;
        enabled = active;

        if (active)
            Unpark();
        else
            Park();
    }

    public void SetInput(float rollInput, float pitchInput, float yawInput, bool up, bool down)
    {
        roll = rollInput;
        pitch = pitchInput;
        yaw = yawInput;
        throttleUp = up;
        throttleDown = down;
    }

    public void Park()
    {
        ClearInput();

        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (rb == null) return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (levelWhenParked)
            rb.rotation = Quaternion.Euler(0f, rb.rotation.eulerAngles.y, 0f);

        if (freezeWhenParked)
            rb.isKinematic = true;
        else
            rb.Sleep();
    }

    private void Unpark()
    {
        ClearInput();

        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (rb == null) return;

        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.WakeUp();
    }

    private void ClearInput()
    {
        throttle = 0f;
        roll = 0f;
        pitch = 0f;
        yaw = 0f;
        throttleUp = false;
        throttleDown = false;
    }

    private void Update()
    {
        if (!isControlled) return;

        if (throttleUp)
            throttle += throttleIncrement;
        else if (throttleDown)
            throttle -= throttleIncrement;

        throttle = Mathf.Clamp(throttle, 0f, 100f);
    }

    private void FixedUpdate()
    {
        if (!isControlled) return;

        rb.AddForce(transform.forward * maxThrust * throttle);
        rb.AddTorque(transform.up * yaw * responseModifier);
        rb.AddTorque(transform.right * pitch * responseModifier);
        rb.AddTorque(-transform.forward * roll * responseModifier);
    }
}
