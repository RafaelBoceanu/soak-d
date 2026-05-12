using System;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class ProjectileTrajectory : MonoBehaviour
{
    #region Members
    private LineRenderer trajectoryLine;

    [Header("Trajectory Settings")]
    [SerializeField] Transform hitMarker;
    [SerializeField, Range(10, 100)] int maxPoints = 50;
    [SerializeField, Range(0.01f, 0.5f)] float increment = 0.025f;
    [SerializeField, Range(1.05f, 2f)] float rayOverlap = 1.1f;

    [Header("Collision Settings")]
    public LayerMask collisionMask;
    #endregion


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (trajectoryLine == null)
            trajectoryLine = GetComponent<LineRenderer>(); 
    }

    public void PredictTrajectory(ProjectileProperties projectile)
    {
        Vector3 velocity = projectile.direction * (projectile.initialSpeed / projectile.mass);
        Vector3 position = projectile.initialPosition;
        Vector3 nextPosition;
        float overlap;

        UpdateLineRender(maxPoints, (0, position));

        for (int i = 1; i < maxPoints; ++i)
        {
            // Estimate velocity and update next predicted position
            velocity = CalculateNewVelocity(velocity, projectile.drag, increment);
            nextPosition = position + velocity * increment;

            // Overlap rays by small margin to ensure a surface is never missed
            overlap = Vector3.Distance(position, nextPosition) * rayOverlap;

            // When hitting a surface, show the marker and stop updating the line
            if (Physics.Raycast(position, velocity.normalized, out RaycastHit hit, overlap, collisionMask))
            {
                UpdateLineRender(i, (i - 1, hit.point));
                MoveHitMarker(hit);
                break;
            }

            hitMarker.gameObject.SetActive(false);
            position = nextPosition;
            UpdateLineRender(maxPoints, (i, position));
        }
    }

    private void UpdateLineRender(int count, (int point, Vector3 pos) pointPos)
    {
        trajectoryLine.positionCount = count;
        trajectoryLine.SetPosition(pointPos.point, pointPos.pos);
    }

    private Vector3 CalculateNewVelocity(Vector3 velocity, float drag, float increment)
    {
        velocity += Physics.gravity * increment;
        velocity *= Mathf.Clamp01(1f - drag * increment);
        return velocity;
    }

    private void MoveHitMarker(RaycastHit hit) 
    {
        hitMarker.gameObject.SetActive(true);

        // Offset marker from surface
        float offset = 0.025f;
        hitMarker.position = hit.point + hit.normal * offset;
        hitMarker.rotation = Quaternion.LookRotation(hit.normal, Vector3.up);
    }

    public void SetTrajectoryVisible(bool visible) 
    {
        trajectoryLine.enabled = visible;
        hitMarker.gameObject.SetActive(visible);
    }
}
