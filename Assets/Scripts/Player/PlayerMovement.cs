using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterController controller;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerNeeds playerNeeds;

    [Header("Movement Settings")]
    [SerializeField] private float speed = 2.0f;
    [SerializeField] private float sprintSpeed = 4.0f;
    [SerializeField] private float rotationSpeed = 10.0f;

    [Header("Aiming")]
    [Tooltip("How fast the character turns to face the aim direction while aiming.")]
    [SerializeField] private float aimRotationSpeed = 20.0f;
    [Tooltip("When off, the character only snaps to the aim direction while standing still " +
             "and keeps facing its movement direction while walking.")]
    [SerializeField] private bool faceAimWhileMoving = true;

    [Header("Pee Stance")]
    [Tooltip("Walk speed while the zipper is open. Can root the character but still lets it turn on the spot.")]
    [SerializeField, Min(0f)] private float peeMoveSpeed = 0f;
    [Tooltip("How fast the character turns to face the camera while the zipper is open.")]
    [SerializeField] private float peeTurnSpeed = 14f;

    [Header("Animation Settings")]
    [SerializeField] private float animSmoothTime = 0.1f;

    [Header("Gravity")]
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float groundedOffset = -2f;

    private const float MoveDeadzone = 0.1f;

    private float verticalVelocity;

    private float currentAnimSpeed = 0f;
    private float animSpeedVelocity = 0f;

    private Vector3 inputVector = Vector3.zero;
    private bool isSprinting = false;
    private bool isAiming = false;
    public bool IsSprinting => isSprinting;
    public bool IsAiming => isAiming;
    private bool isMoving = false;
    private bool movementLocked = false;
    private bool peeStance = false;
    private bool stunned = false;

    [Header("Puddle splash")]
    [SerializeField] private ParticleSystem puddleSplash;
    [SerializeField, Min(0f)] private float splashCheckInterval = 0.15f;
    private float nextSplashCheck;
    private bool wasInPuddle;

    public bool IsMoving => isMoving;
    public bool IsStunned => stunned;
    public bool IsPeeStance => peeStance;

    private bool SprintAllowed => playerNeeds == null || playerNeeds.CanSprint;

    public bool CanPee => isActiveAndEnabled && !stunned && !movementLocked;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (playerNeeds == null)
            playerNeeds = GetComponent<PlayerNeeds>();
    }

    private void OnDisable()
    {
        isAiming = false;
        isMoving = false;
        peeStance = false;
    }

    private void Update()
    {
        if (cameraTransform == null)
        {
            Debug.LogWarning("Camera Transform not assigned on " + gameObject.name);
            return;
        }

        HandleMovement();

        CheckPuddleSplash();
    }

    private void HandleMovement()
    {
        //Camera-relative directions
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;

        camForward.y = 0f;
        camRight.y = 0f;

        camForward.Normalize();
        camRight.Normalize();

        //Build movement vector
        Vector3 move = camForward * inputVector.z + camRight * inputVector.x;
        move = Vector3.ClampMagnitude(move, 1f);

        if (movementLocked || stunned)
        {
            move = Vector3.zero;
        }

        if (peeStance && peeMoveSpeed <= 0f)
        {
            move = Vector3.zero;
        }

        isMoving = move.magnitude >= MoveDeadzone;

        float targetSpeed = peeStance
            ? peeMoveSpeed
            : (isSprinting && SprintAllowed ? sprintSpeed : speed);

        float targetAnimSpeed = move.magnitude * targetSpeed;

        //Smooth animation
        currentAnimSpeed = Mathf.SmoothDamp(
            currentAnimSpeed,
            targetAnimSpeed,
            ref animSpeedVelocity,
            animSmoothTime
        );

        if (animator != null)
        {
            animator.SetFloat("Speed", currentAnimSpeed);
            animator.SetBool("isMoving", isMoving);
        }

        // Gravity logic
        if (controller.isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = groundedOffset;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 verticalMove = Vector3.up * verticalVelocity;

        //Movement + gravity combined
        if (isMoving)
        {
            controller.Move((move * targetSpeed + verticalMove) * Time.deltaTime);
        }
        else
        {
            controller.Move(verticalMove * Time.deltaTime);
        }

        if (peeStance)
        {
            FaceDirection(camForward, peeTurnSpeed);
        }
        else if (isAiming && (faceAimWhileMoving || !isMoving))
        {
            FaceDirection(camForward, aimRotationSpeed);
        }
        else if (isMoving)
        {
            FaceDirection(move, rotationSpeed);
        }
    }

    private void FaceDirection(Vector3 direction, float turnSpeed)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            1f - Mathf.Exp(-turnSpeed * Time.deltaTime)
        );
    }

    private void CheckPuddleSplash()
    {
        if (puddleSplash == null || Time.time < nextSplashCheck)
            return;

        nextSplashCheck = Time.time + splashCheckInterval;

        bool inPuddle = isMoving && PeePuddle.IsInsideAnyPuddle(transform.position);

        if (inPuddle && !wasInPuddle)
            puddleSplash.Play();

        wasInPuddle = inPuddle;
    }
  
    //Called by PlayerInputHandler
    public void SetInputVector(Vector3 input)
    {
        inputVector = input;
    }

    public void SetSprint(bool sprinting)
    {
        isSprinting = sprinting;
    }

    public void SetAiming(bool aiming)
    {
        isAiming = aiming;
    }

    public void SetMovementLocked(bool locked)
    {
        movementLocked = locked;

        if (locked)
        {
            isMoving = false;
        }
    }

    public void SetPeeStance(bool value)
    {
        peeStance = value;
    }

    public void SetStunned(bool value)
    {
        stunned = value;

        if (value)
        {
            isMoving = false;
        }
    }

    public void ResetFall()
    {
        verticalVelocity = groundedOffset;
    }
}
