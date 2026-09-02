using UnityEngine;
using UnityEngine.InputSystem;

public class PlayersCameraController : MonoBehaviour
{
    [SerializeField] Transform followTarget;
    [SerializeField] float distanceToTarget = 7f;
    [SerializeField] float minVerticalAngle = -20f;
    [SerializeField] float maxVerticalAngle = 60f;
    [SerializeField] Vector2 framingOffset = new Vector2(0, 1f);
    [SerializeField] float mouseSensitivity = 0.7f;
    [SerializeField] float smoothTime = 0.3f;

    float rotationX = 20f;
    float rotationY;
    Vector3 currentVelocity;
    Vector3 desiredPosition;

    Vector3 lookPoint;
    Vector3 lookVelocity;
    [SerializeField] float lookSmoothTime = 0.05f;
    [SerializeField] float rotationSharpness = 20f;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        rotationY = angles.y;
        if (followTarget != null) 
            lookPoint = followTarget.position;
    }

    private void LateUpdate()
    {
        if (followTarget == null) return;

        rotationY += Input.GetAxis("Mouse X") * mouseSensitivity * 100f * Time.deltaTime;
        rotationX -= Input.GetAxis("Mouse Y") * mouseSensitivity * 100f * Time.deltaTime;
        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);

        Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0);
        Vector3 offset = rotation * new Vector3(0, 0, -distanceToTarget);

        Vector3 targetPosition = followTarget.position
                       + Vector3.up * framingOffset.y
                       + rotation * Vector3.right * framingOffset.x;
        desiredPosition = targetPosition + offset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, smoothTime);
        lookPoint = Vector3.SmoothDamp(lookPoint, targetPosition, ref lookVelocity, lookSmoothTime);

        Vector3 toTarget = lookPoint - transform.position;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(toTarget),
            1f - Mathf.Exp(-rotationSharpness * Time.deltaTime)
        );
    }

    public void SetFollowTarget(Transform newTarget)
    {
        followTarget = newTarget;
        if (newTarget != null)
            lookPoint = newTarget.position;
    }

    public void SetOffset(float newDistance, Vector2 newFramingOffset)
    {
        distanceToTarget = newDistance;
        framingOffset = newFramingOffset;
    }
}
