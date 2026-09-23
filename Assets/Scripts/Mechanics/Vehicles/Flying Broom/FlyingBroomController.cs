using UnityEngine;

public class FlyingBroomController : MonoBehaviour
{
    [Header("Speed")]
    [Tooltip("Top speed in m/s while holding throttle up")]
    public float maxSpeed = 20f;
    [Tooltip("m/s gained per second while holding throttle up")]
    public float acceleration = 8f;
    [Tooltip("m/s lost per second while holding throttle down")]
    public float braking = 14f;
    [Tooltip("Speed the broom settles to when no throttle is held")]
    public float idleSpeed = 0f;
    [Tooltip("How fast speed drifts to idle when no throttle is held")]
    public float idleSpeedReturn = 3f;

    [Header("Handling")]
    [Tooltip("Degrees per second of turning at full stick")]
    public float turnRate = 90f;
    [Tooltip("Degrees per second of nose up/down at full stick")]
    public float pitchRate = 70f;
    [Tooltip("Steepest climb/dive angle allowed")]
    public float maxPitch = 55f;
    [Tooltip("How far the broom leans into turns")]
    public float maxBank = 35f;
    [Tooltip("How fast the nose returns to level when pitch is released")]
    public float autoLevel = 2f;
    [Tooltip("How fast the lean follows the turn input")]
    public float bankSharpness = 6f;
    [Tooltip("How fast the broom model catches up to the target rotation")]
    public float rotationSharpness = 10f;
    [Tooltip("Tick if pushing forward should climb instead of dive")]
    public bool invertPitch = false;

    [Header("Movement Feel")]
    [Tooltip("How fast the velocity lines up with where the broom is pointing")]
    public float grip = 4f;
    [Tooltip("Vertical speed from pitch input while hovering/flying slowly")]
    public float hoverClimbSpeed = 5f;
    [Tooltip("Below this speed the hover climb fully applies")]
    public float hoverAssistSpeed = 6f;

    [Header("Parking")]
    [Tooltip("Freeze the broom in place once the rider gets off, so it doesn't drift away")]
    public bool freezeWhenParked = true;
    [Tooltip("Level the broom to when it is parked, instead of it staying tilted")]
    public bool levelWhenParked = true;

    private float roll;
    private float pitch;
    private float yaw;

    private float speed;
    private float heading;
    private float pitchAngle;
    private float bankAngle;

    private bool throttleUp;
    private bool throttleDown;
    private bool isControlled = false;

    private Rigidbody rb;

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
        heading = rb.rotation.eulerAngles.y;
        pitchAngle = 0f;
        bankAngle = 0f;
    }

    private void ClearInput()
    {
        speed = 0f;
        roll = 0f;
        pitch = 0f;
        yaw = 0f;
        throttleUp = false;
        throttleDown = false;
    }

    private void FixedUpdate()
    {
        if (!isControlled) return;

        float dt = Time.fixedDeltaTime;

        if (throttleUp)
            speed += acceleration * dt;
        else if (throttleDown)
            speed -= braking * dt;
        else
            speed = Mathf.MoveTowards(speed, idleSpeed, idleSpeedReturn * dt);

        speed = Mathf.Clamp(speed, 0f, maxSpeed);

        float turnInput = Mathf.Clamp(roll + yaw, -1f, 1f);
        float pitchInput = invertPitch ? -pitch : pitch;

        heading += turnInput * turnRate * dt;

        if (Mathf.Abs(pitchInput) > 0.05f)
            pitchAngle += pitchInput * pitchRate * dt;
        else
            pitchAngle = Mathf.Lerp(pitchAngle, 0f, 1f - Mathf.Exp(-autoLevel * dt));

        pitchAngle = Mathf.Clamp(pitchAngle, -maxPitch, maxPitch);

        float targetBank = -turnInput * maxBank;
        bankAngle = Mathf.Lerp(bankAngle, targetBank, 1f - Mathf.Exp(-bankSharpness * dt));

        Quaternion targetRotation = Quaternion.Euler(pitchAngle, heading, bankAngle);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, 1f - Mathf.Exp(-rotationSharpness * dt)));
        rb.angularVelocity = Vector3.zero;

        Vector3 travelDir = Quaternion.Euler(pitchAngle, heading, 0f) * Vector3.forward;
        Vector3 desiredVelocity = travelDir * speed;

        float hoverAssist = 1f - Mathf.Clamp01(speed / Mathf.Max(0.01f, hoverAssistSpeed));
        desiredVelocity += Vector3.up * (-pitchInput) * hoverClimbSpeed * hoverAssist;

        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, desiredVelocity, 1f - Mathf.Exp(-grip * dt));
    }
}
