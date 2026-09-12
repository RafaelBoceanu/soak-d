using UnityEngine;

public class VehicleCameraController : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Transform[] positions;
    [SerializeField] float lerpSpeed = 10f;
    [SerializeField] float maxDistance = 20f;
    [Tooltip("Height above the vehicle the camera pivots around and looks at")]
    [SerializeField] float pivotHeight = 1.5f;

    [Header("Collision")]
    [Tooltip("Keeps the camera outside of buildings, props, and the other player")]
    [SerializeField] CameraCollisionProbe collisionProbe = new CameraCollisionProbe();

    private int index = 1;

    private Vector3 smoothedPosition;
    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
            cam = GetComponentInChildren<Camera>();

        smoothedPosition = transform.position;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        collisionProbe.Snap();
    }

    private void Update()
    {
        // Shared vehicle camera: either player can cycle through the mounted views
        if (positions != null && positions.Length > 0 && TwoPlayerInputManager.AnyCycleViewPressed())
            index = (index + 1) % positions.Length;
    }

    private void LateUpdate()
    {
        if (target == null || positions == null || positions.Length == 0)
            return;

        index = Mathf.Clamp(index, 0, positions.Length - 1);

        Transform offset = positions[index];
        Vector3 pivot = target.position + Vector3.up * pivotHeight;
        Vector3 desiredPosition = target.TransformPoint(offset.localPosition);

        float t = 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime);
        smoothedPosition = Vector3.Lerp(smoothedPosition, desiredPosition, t);

        Vector3 fromTarget = smoothedPosition - target.position;
        if (fromTarget.magnitude > maxDistance)
            smoothedPosition = target.position + fromTarget.normalized * maxDistance;

        transform.position = collisionProbe.Resolve(
            pivot,
            smoothedPosition,
            cam,
            Time.deltaTime,
            target
        );

        Vector3 toPivot = pivot - transform.position;
        if (toPivot.sqrMagnitude < 0.0001f)
            return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(toPivot),
            t
        );
    }
}
