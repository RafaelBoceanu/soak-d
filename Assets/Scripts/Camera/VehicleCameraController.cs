using UnityEngine;

public class VehicleCameraController : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Transform[] positions;
    [SerializeField] float lerpSpeed = 10f;
    [SerializeField] float maxDistance = 20f;

    private int index = 1;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void Update()
    {
        // Shared vehicle camera: either player can cycle through the mounted views
        if (positions != null && positions.Length > 0 && TwoPlayerInputManager.AnyCycleViewPressed())
            index = (index + 1) % positions.Length;
    }

    private void LateUpdate()
    {
        if (target == null || positions.Length == 0)
            return;

        Transform offset = positions[index];
        Vector3 desiredPosition = target.TransformPoint(offset.localPosition);

        transform.position = Vector3.Lerp(
            transform.position, 
            desiredPosition, 
            lerpSpeed * Time.deltaTime
        );

        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > maxDistance)
        {
            transform.position = target.position + 
                (transform.position - target.position).normalized * maxDistance;
        
        }
        
        Vector3 lookAtPoint = target.position + Vector3.up * 1.5f;

        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            Quaternion.LookRotation(lookAtPoint - transform.position),
            lerpSpeed * Time.deltaTime
        );
    }
}
