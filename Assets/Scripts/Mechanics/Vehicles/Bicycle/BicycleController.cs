using UnityEngine;

public class BicycleController : MonoBehaviour
{
    RaycastHit hit;
    float moveInput, steerInput, rayLength, currentVelocityOffset;

    [HideInInspector] public Vector3 velocity;
    public Rigidbody sphereRB, bicycleBody;
    public GameObject handle, frame;
    public TrailRenderer skidTrail;
    public AnimationCurve turningCurve;

    public float maxSpeed, acceleration, steerStrength, tiltAngle, gravity, bikeXTiltIncrement = .09f, 
        zTiltAngle = 45f, handleRotVal = 30f, handleRotSpeed = .15f, skidWidth = 0.062f, minSkidVelocity = 10f;
    
    [Range(1, 10)]
    public float brakingStrength;
    public LayerMask groundLayer;

    private bool isControlled = false;
    private bool isBraking = false;

    [Header("Animation")]
    [SerializeField] private Animator cranksAnimator;

    [SerializeField] private float cranksSpeedMultiplier = 1.5f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        sphereRB.transform.parent = null;
        bicycleBody.transform.parent = null;

        rayLength = sphereRB.GetComponent<SphereCollider>().radius + 0.02f;

        skidTrail.startWidth = skidWidth;
        skidTrail.emitting = false;
    }

    public void SetControl(bool active)
    {
        isControlled = active;
        enabled = active;

        if (active)
        {
            sphereRB.WakeUp();
            bicycleBody.WakeUp();
        }
    }

    public void SetInput(float move, float steer, bool brake)
    {
        moveInput = move;
        steerInput = steer;
        isBraking = brake;
    }

    // Update is called once per frame
    void Update()
    {
        if(!isControlled) return;

        transform.position = sphereRB.transform.position;

        velocity = bicycleBody.transform.InverseTransformDirection(bicycleBody.linearVelocity);
        currentVelocityOffset = velocity.z / maxSpeed;

        HandleCranksAnimation();
    }

    private void FixedUpdate()
    {
        if(!isControlled) return;

        Movement();
        SkidTrails();
    }

    void Movement()
    {
        if (Grounded())
        {
            if (!isBraking)
            {
                Acceleration();
                Steer();
            }
            Brake();
        }
        else
        {
            Gravity();
        }
        BikeTilt();
    }

    void Acceleration()
    {
        sphereRB.linearVelocity = Vector3.Lerp(
            sphereRB.linearVelocity, 
            maxSpeed * moveInput * transform.forward, 
            Time.fixedDeltaTime * acceleration
        );
    }

    void Steer()
    {
        transform.Rotate(
            0, 
            steerInput * moveInput * turningCurve.Evaluate(Mathf.Abs(currentVelocityOffset)) * steerStrength * Time.fixedDeltaTime, 
            0, 
            Space.World
        );

        handle.transform.localRotation = Quaternion.Slerp(
            handle.transform.localRotation,
            Quaternion.Euler(handle.transform.localRotation.eulerAngles.x, 30f * steerInput, handle.transform.localRotation.eulerAngles.z),
            handleRotSpeed
        );

        frame.transform.localRotation = Quaternion.Slerp(
            frame.transform.localRotation,
            Quaternion.Euler(frame.transform.localRotation.eulerAngles.x, 30f * steerInput, frame.transform.localRotation.eulerAngles.z),
            handleRotSpeed
        );
    }

    void Brake()
    {
        if (isBraking)
        {
            sphereRB.linearVelocity *= brakingStrength / 10;
        }
    }

    bool Grounded()
    {
        float radius = rayLength - 0.02f;
        Vector3 origin = sphereRB.transform.position + radius * Vector3.up;

        return Physics.SphereCast(origin, radius + 0.02f, -transform.up, out hit, rayLength, groundLayer);
    }

    void Gravity()
    {
        sphereRB.AddForce(gravity * Vector3.down, ForceMode.Acceleration);
    }

    void SkidTrails()
    {
        skidTrail.emitting = Grounded() && Mathf.Abs(velocity.x) > minSkidVelocity;
    }

    void BikeTilt()
    {
        float xRot = (Quaternion.FromToRotation(bicycleBody.transform.up, hit.normal) * bicycleBody.transform.rotation).eulerAngles.x;
        float zRot = currentVelocityOffset > 0 ? -zTiltAngle * steerInput * currentVelocityOffset : 0;

        Quaternion targetRotation = Quaternion.Slerp(
            bicycleBody.transform.rotation, 
            Quaternion.Euler(xRot, transform.eulerAngles.y, zRot), 
            bikeXTiltIncrement
        );

        Quaternion newRotation = Quaternion.Euler(
            targetRotation.eulerAngles.x, 
            transform.eulerAngles.y, 
            targetRotation.eulerAngles.z);

        bicycleBody.MoveRotation(newRotation);
    }

    void HandleCranksAnimation()
    {
        if (cranksAnimator == null) return;

        // Only forward movement
        float forwardSpeed = Mathf.Max(0f, velocity.z);

        // Normalise and scale
        float animSpeed = forwardSpeed * cranksSpeedMultiplier;

        // Clamp to prevent excessive speeds
        animSpeed = Mathf.Clamp(animSpeed, 0f, 2f);

        cranksAnimator.SetFloat("CrankSpeed", animSpeed);
    }
}
