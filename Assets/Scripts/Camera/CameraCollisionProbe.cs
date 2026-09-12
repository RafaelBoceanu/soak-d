using UnityEngine;

[System.Serializable]
public class CameraCollisionProbe : MonoBehaviour
{
    [Tooltip("Layers the camera is not allowed to pass through")]
    public LayerMask obstructionMask = (1 << 0) | (1 << 6) | (1 << 8) | (1 << 9);

    [Tooltip("Closest the camera may ever get to the pivot")]
    public float minDistance = 0.6f;

    [Tooltip("Extra clearance kept between the near clip plane and the surface it stops against")]
    public float padding = 0.12f;

    [Tooltip("Probe radius used when there is no Camera component to derive the near plane size from")]
    public float fallbackRadius = 0.25f;

    [Tooltip("How fast the camera slides back out once nothing blocks it. Pulling in is always instant")]
    public float returnSpeed = 8f;

    const int MaxHits = 16;

    readonly RaycastHit[] hits = new RaycastHit[MaxHits];

    float currentDistance = -1f;

    public void Snap()
    {
        currentDistance = -1f;
    }

    public float ProbeRadius(Camera cam)
    {
        if (cam == null || cam.orthographic)
            return fallbackRadius + padding;

        float near = cam.nearClipPlane;
        float halfHeight = near * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * Mathf.Max(0.0001f, cam.aspect);

        return Mathf.Sqrt(halfHeight * halfHeight + halfWidth * halfWidth + near * near) + padding; 
    }

    public Vector3 Resolve(Vector3 pivot, Vector3 idealPosition, Camera cam, float deltaTime,
                           Transform ignoreA = null, Transform ignoreB = null)
    {
        Vector3 toCamera = idealPosition - pivot;
        float wanted = toCamera.magnitude;

        if (wanted <= 0.0001f)
        {
            currentDistance = 0f;
            return pivot;
        }

        Vector3 direction = toCamera / wanted;
        float floor = Mathf.Min(minDistance, wanted);

        float allowed = Mathf.Clamp(Cast(pivot, direction, wanted, ProbeRadius(cam), ignoreA, ignoreB), floor, wanted);

        if (currentDistance < 0f)
            currentDistance = allowed;
        else if (allowed < currentDistance)
            currentDistance = allowed;
        else
            currentDistance = Mathf.MoveTowards(currentDistance, allowed, Mathf.Max(0f, returnSpeed) * deltaTime);

        return pivot + direction * currentDistance;
    }

    float Cast(Vector3 pivot, Vector3 direction, float wanted, float radius, Transform ignoreA, Transform ignoreB)
    {
        float closest = wanted;

        int count = Physics.SphereCastNonAlloc(pivot, radius, direction, hits, wanted,
                                               obstructionMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = hits[i].collider;

            if (hitCollider == null || IsIgnored(hitCollider.transform, ignoreA, ignoreB))
                continue;

            if (hits[i].distance <= 0.0001f)
                return minDistance;

            if (hits[i].distance < closest)
                closest = hits[i].distance;
        }

        return closest;
    }

    static bool IsIgnored(Transform hit, Transform ignoreA, Transform ignoreB)
    {
        if (ignoreA == null && ignoreB == null) 
            return false;

        for (Transform current = hit; current != null; current = current.parent)
        {
            if (current == ignoreA || current == ignoreB)
                return true;
        }

        return false;
    }
}
