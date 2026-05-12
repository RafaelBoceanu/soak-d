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

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        rotationY = angles.y;
    }

    private void LateUpdate()
    {
        rotationY += Input.GetAxis("Mouse X") * mouseSensitivity * 100f * Time.deltaTime;
        rotationX -= Input.GetAxis("Mouse Y") * mouseSensitivity * 100f * Time.deltaTime;
        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);

        Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0);
        Vector3 offset = rotation * new Vector3(0, 0, -distanceToTarget);

        Vector3 targetPosition = followTarget.position + new Vector3(framingOffset.x, framingOffset.y, 0);
        desiredPosition = targetPosition + offset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, smoothTime);
        transform.LookAt(targetPosition);
    }

    public void SetFollowTarget(Transform newTarget)
    {
        followTarget = newTarget;
    }

    public void SetOffset(float newDistance, Vector2 newFramingOffset)
    {
        distanceToTarget = newDistance;
        framingOffset = newFramingOffset;
    }
}
