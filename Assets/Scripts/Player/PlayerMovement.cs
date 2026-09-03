using System;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterController controller;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] Animator animator;

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

    private void Awake()
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void OnDisable()
    {
        isAiming = false;
    }

    private void Update()
    {
        if (cameraTransform == null)
        {
            Debug.LogWarning("Camera Transform not assigned on " + gameObject.name);
            return;
        }

        HandleMovement();
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

        bool isMoving = move.magnitude >= MoveDeadzone;

        float targetSpeed = isSprinting ? sprintSpeed : speed;
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
            animator.SetBool("isMoving", move.magnitude >= 0.1f);
            this.gameObject.GetComponent<PeeSystem>().enabled = move.magnitude < 0.1f; // Disable pee system when moving
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

        if (isAiming && (faceAimWhileMoving || !isMoving))
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
}
