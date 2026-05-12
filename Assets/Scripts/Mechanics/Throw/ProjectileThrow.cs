using System.Collections;
using UnityEngine;

public class ProjectileThrow : MonoBehaviour
{
    ProjectileTrajectory projectileTrajectory;

    [Header("Projectile")]
    [SerializeField] private Rigidbody objectToThrow;
    [SerializeField, Range(0.0f, 50.0f)] float maxForce = 30f;
    [SerializeField] private float chargeRate = 10f;
    [SerializeField] private Transform spawnPosition;
    [SerializeField] private OwnerType owner;
    [SerializeField] private float minForce = 5f;

    [Header("Aiming")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GameObject crosshairUI;

    private float currentForce = 0f;
    private bool isCharging = false;

    private float cachedMass;
    private float cachedDrag;

    void OnEnable()
    {
        projectileTrajectory = GetComponent<ProjectileTrajectory>();

        if (spawnPosition == null)
            spawnPosition = transform;

        if (objectToThrow != null)
        {
            var rb = objectToThrow.GetComponent<Rigidbody>();
            cachedMass = rb.mass;
            cachedDrag = rb.linearDamping;
        }

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (crosshairUI != null)
            crosshairUI.SetActive(false);
    }

    void Update()
    {
        bool aimButton = Input.GetMouseButton(1);

        if (crosshairUI != null)
            crosshairUI.SetActive(aimButton);

        if (aimButton)
        {
            if (!isCharging)
            {
                isCharging = true;
                currentForce = minForce;
            }

            if (Input.GetMouseButtonDown(0))
            {
                isCharging = true;
            }

            if (isCharging && Input.GetMouseButton(0))
            {
                currentForce += chargeRate * Time.deltaTime;
                currentForce = Mathf.Clamp(currentForce, 0f, maxForce);

            }

            Predict();
        }
        else
        {
            isCharging = false;
            currentForce = 0f;
            projectileTrajectory.SetTrajectoryVisible(false);
        }

        if (isCharging && Input.GetMouseButtonUp(0))
        {
            ThrowObject();
            currentForce = 0f;
            isCharging = false;
        }
    }

    void Predict()
    {
        projectileTrajectory.SetTrajectoryVisible(true);
        projectileTrajectory.PredictTrajectory(ProjectileData());
    }

    ProjectileProperties ProjectileData()
    {
        // Offset spawn forward to avoid self-collision
        Vector3 launchPos = spawnPosition.position + playerCamera.transform.forward * 0.5f;

        // Clamp vertical angle
        Vector3 aimDir = playerCamera.transform.forward;
        aimDir.y = Mathf.Clamp(aimDir.y, -0.1f, 0.8f);
        aimDir.Normalize();

        return new ProjectileProperties
        {
            direction = aimDir,
            initialPosition = launchPos,
            initialSpeed = currentForce,
            mass = cachedMass,
            drag = cachedDrag
        };
    }

    void ThrowObject()
    {
        if (!objectToThrow) return;

        Rigidbody thrownObject = Instantiate(
            objectToThrow, 
            spawnPosition.position, 
            Quaternion.identity
        );

        thrownObject.AddForce(playerCamera.transform.forward * currentForce, ForceMode.Impulse);

        ThrownProjectile projectile = thrownObject.GetComponent<ThrownProjectile>();
        if (projectile != null)
        {
            projectile.owner = owner;
        }

        StartCoroutine(DestroyObject(thrownObject.gameObject));
    }

    IEnumerator DestroyObject(GameObject objectToDestroy)
    {
        yield return new WaitForSeconds(2);
        Destroy(objectToDestroy);
    }
}
