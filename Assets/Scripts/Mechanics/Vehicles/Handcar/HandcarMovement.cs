using System.Collections;
using UnityEngine;

public class HandcarMovement : MonoBehaviour
{
    [SerializeField] private GameManager gameManager; // Reference to the GameManager, if needed
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private float speed = 0f;
    [SerializeField] private float acceleration = 2f;
    [SerializeField] private float deceleration = 1f;
    [SerializeField] private float maxSpeed = 10f;
    [SerializeField] private Animator animator;
    [SerializeField] private float animationResetDelay = 1f;

    private int currentSegment = 0;
    private float t = 0f; // Interpolation between waypoints
    private int handleDirection = 0;
    [SerializeField] private KeyCode lastKey = KeyCode.None;
    private Coroutine resetAnimCoroutine;

    // Update is called once per frame
    void Update()
    {
        // Increase speed on button tap
        if (Input.GetKeyDown(KeyCode.LeftArrow) && lastKey != KeyCode.LeftArrow)
        {
           PumpHandle(-1, KeyCode.LeftArrow);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow) && lastKey != KeyCode.RightArrow)
        {
            PumpHandle(1, KeyCode.RightArrow);
        }

        // Decrease speed over time
        speed = Mathf.MoveTowards(speed, 0, deceleration * Time.deltaTime);

        if (waypoints.Length < 2) return;

        // Move the handcar along the current segment
        float segmentLength = Vector3.Distance(waypoints[currentSegment].position, waypoints[currentSegment + 1].position);
        float moveDistance = speed * Time.deltaTime;
        t += moveDistance / segmentLength;

        while (t > 1f && currentSegment < waypoints.Length - 2)
        {
            t -= 1f;
            currentSegment++;
        }

        // Clamp to last segment
        if (currentSegment >= waypoints.Length - 1)
        {
            currentSegment = waypoints.Length - 2;
            t = 1f; // Ensure stopping at the last waypoint
        }

        // Interpolate position and rotation
        Vector3 pos = Vector3.Lerp(waypoints[currentSegment].position, waypoints[currentSegment + 1].position, t);
        transform.position = pos;

        Vector3 dir = (waypoints[currentSegment + 1].position - waypoints[currentSegment].position).normalized;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    private void PumpHandle(int direction, KeyCode key)
    {
        speed += acceleration;
        speed = Mathf.Clamp(speed, 0, maxSpeed);

        handleDirection = direction;
        lastKey = key;

        if (animator != null)
            animator.SetInteger("HandleDirection", handleDirection);

        if (resetAnimCoroutine != null)
            StopCoroutine(resetAnimCoroutine);
        resetAnimCoroutine = StartCoroutine(ResetHandleDirection());
    }

    private IEnumerator ResetHandleDirection()
    {
        yield return new WaitForSeconds(animationResetDelay);
        if (animator != null)
            animator.SetInteger("HandleDirection", 0);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null) return;

        if (collision.gameObject.CompareTag("End"))
        {
            gameManager.UpdateGameState(GameState.Delivery);
        }
    }
}
