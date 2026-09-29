using System;
using UnityEngine;

public class SpellBolt : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float speed = 22f;

    [Tooltip("Height of the arc at the middle of the flight")]
    [SerializeField, Range(0f, 0.5f)] private float arc = 0.15f;

    [SerializeField, Min(0.05f)] private float minFlightSeconds = 0.2f;

    [Tooltip("The glowing head. Its children stop with it on landing")]
    [SerializeField] private ParticleSystem flight;

    [Tooltip("Burst played where the bolt lands")]
    [SerializeField] private ParticleSystem impact;

    [Tooltip("Seconds the bolt lingers after landing so trail and impact can fade")]
    [SerializeField, Min(0f)] private float lingerSeconds = 0.8f;

    private Transform target;
    private Vector3 targetOffset;
    private Vector3 lastTargetPoint;
    private Vector3 start;
    private float duration;
    private float elapsed;
    private bool landed;
    private Action onArrive;

    public void Launch(Transform targetTransform, Vector3 offset, Action arrive)
    {
        target = targetTransform;
        targetOffset = offset;
        onArrive = arrive;

        start = transform.position;
        lastTargetPoint = target != null ? target.position + targetOffset : start;
        duration = Mathf.Max(minFlightSeconds, Vector3.Distance(start, lastTargetPoint) / speed);
    }

    Vector3 TargetPoint()
    {
        if (target != null)
            lastTargetPoint = target.position + targetOffset;

        return lastTargetPoint;
    }

    // Update is called once per frame
    void Update()
    {
        if (landed) return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        Vector3 end = TargetPoint();
        Vector3 point = Vector3.Lerp(start, end, t);
        point += Vector3.up * (Vector3.Distance(start, end) * arc * 4f * t * (1f - t));

        transform.position = point;

        if (t >= 1f)
            Land();
    }

    void Land()
    {
        landed = true;

        onArrive?.Invoke();
        onArrive = null;

        if (flight != null)
            flight.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (impact != null)
            impact.Play();

        Destroy(gameObject, lingerSeconds);
    }
}
