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

    [Header("Animation Settings")]
    [SerializeField] private float animSmoothTime = 0.1f;

    [Header("Gravity")]
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float groundedOffset = -2f;

    private float verticalVelocity;

    private float currentAnimSpeed = 0f;
    private float animSpeedVelocity = 0f;

    private Vector3 inputVector = Vector3.zero;
    private bool isSprinting = false;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
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
        if (move.magnitude >= 0.1f)
        {
            controller.Move((move * targetSpeed + verticalMove) * Time.deltaTime);

            //Rotate towards movement
            Quaternion targetRotation = Quaternion.LookRotation(move, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }
        else
        {
            controller.Move(verticalMove * Time.deltaTime);
        }
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
}
