using System;
using System.Collections;
using UnityEditor.Build;
using UnityEngine;

public class ProjectileThrow : MonoBehaviour
{
    public enum Ammo { Newspaper, WaterBalloon }

    ProjectileTrajectory projectileTrajectory;

    [Header("Projectile")]
    [SerializeField] private Rigidbody objectToThrow;
    [SerializeField, Range(0.0f, 50.0f)] float maxForce = 30f;
    [SerializeField] private float chargeRate = 10f;
    [SerializeField] private Transform spawnPosition;
    [SerializeField] private OwnerType owner;
    [SerializeField] private float minForce = 5f;
    [SerializeField] private float tumbleTorque = 0.10f;
    [SerializeField] private Color trailColor = Color.white;

    [Header("Ammo")]
    [Tooltip("Inventory item spent by one throw.")]
    [SerializeField] private InventoryItemType ammoItem = InventoryItemType.Newspaper;

    [Tooltip("Off, the player throws for free and the inventory is ignored.")]
    [SerializeField] private bool requireAmmo = true;

    [Tooltip("Inventory to spend from. Left empty it is taken from this object, then from the rig registered for the same owner.")]
    [SerializeField] private PlayerInventory inventory;

    [Header("Water Balloon")]
    [Tooltip("Lets this player swap to water balloons, filled from their own bladder")]
    [SerializeField] private bool allowWaterBalloons = false;
    [Tooltip("Must carry a WaterBalloon component")]
    [SerializeField] private Rigidbody waterBalloonPrefab;
    [Tooltip("Bladder spent filling one balloon")]
    [SerializeField, Min(0f)] private float bladderPerBalloon = 15f;
    [SerializeField] private Color balloonTrailColor = new Color(0.95f, 0.85f, 0.2f);
    [Tooltip("Left empty, taken from this object")]
    [SerializeField] private PlayerNeeds playerNeeds;
    [Tooltip("Where the balloon's puddle is drawn")]
    [SerializeField] private PeePuddle peePuddle;

    [Header("Aiming")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GameObject crosshairUI;

    private float currentForce = 0f;
    private bool isCharging = false;

    private float cachedMass;
    private float cachedDrag;

    private bool warnedNoInventory;

    private Ammo loaded = Ammo.Newspaper;

    public static event Action<OwnerType, Ammo> OnAmmoSwapped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => OnAmmoSwapped = null;

    public Ammo Loaded => loaded;

    public int AmmoLeft => inventory != null ? inventory.GetCount(ammoItem) : 0;

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

        ResolveInventory();

        if (playerNeeds == null)
            playerNeeds = GetComponentInParent<PlayerNeeds>();

        if (peePuddle == null)
            peePuddle = GetComponentInParent<PeePuddle>();
    }

    void Update()
    {
        PlayerInputContext input = TwoPlayerInputManager.GetPlayer(owner);

        if (input == null)
            return;

        if (allowWaterBalloons && input.SwapAmmoPressed)
            SwapAmmo();

        bool aimButton = input.Aim;
        bool hasAmmo = HasAmmo();

        if (crosshairUI != null)
            crosshairUI.SetActive(aimButton);

        if (aimButton && hasAmmo)
        {
            if (!isCharging)
            {
                isCharging = true;
                currentForce = minForce;
            }

            if (input.ThrowPressed)
            {
                isCharging = true;
            }

            if (isCharging && input.ThrowHeld)
            {
                currentForce += chargeRate * Time.deltaTime;
                currentForce = Mathf.Clamp(currentForce, 0f, maxForce);

            }

            if (crosshairUI != null)
            {
                float charge = Mathf.InverseLerp(minForce, maxForce, currentForce);
                crosshairUI.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.6f, charge);
            }

            Predict();
        }
        else
        {
            isCharging = false;
            currentForce = 0f;
            projectileTrajectory.SetTrajectoryVisible(false);
        }

        if (isCharging && input.ThrowReleased)
        {
            ThrowObject();
            currentForce = 0f;
            isCharging = false;
        }
    }

    #region Ammo
    PlayerInventory ResolveInventory()
    {
        if (inventory != null)
            return inventory;

        inventory = GetComponent<PlayerInventory>();

        if (inventory == null)
            inventory = GetComponentInParent<PlayerInventory>();

        if (inventory == null)
            inventory = PlayerInventory.ForOwner(owner);

        if (inventory == null && requireAmmo && !warnedNoInventory)
        {
            warnedNoInventory = true;
            Debug.LogError($"[ProjectileThrow] {name} needs a PlayerInventory for {owner} to spend {ammoItem} - " +
                           $"throwing stays disabled until one is added (or untick RequireAmmo).", this);
        }

        return inventory;
    }

    public void SwapAmmo()
    {
        if (!allowWaterBalloons)
            return;

        loaded = loaded == Ammo.Newspaper ? Ammo.WaterBalloon : Ammo.Newspaper;

        isCharging = false;
        currentForce = 0f;

        OnAmmoSwapped?.Invoke(owner, loaded);
    }

    bool CanFillBalloon() =>
        playerNeeds != null && !playerNeeds.IsHavingAccident && playerNeeds.pee >= bladderPerBalloon;

    bool HasAmmo()
    {
        if (loaded == Ammo.WaterBalloon)
            return CanFillBalloon();

        if (!requireAmmo)
            return true;

        PlayerInventory playerInventory = ResolveInventory();

        return playerInventory != null && playerInventory.Has(ammoItem);
    }
    #endregion

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
            mass = loaded == Ammo.WaterBalloon && waterBalloonPrefab != null ? waterBalloonPrefab.mass : cachedMass,
            drag = loaded == Ammo.WaterBalloon && waterBalloonPrefab != null ? waterBalloonPrefab.linearDamping : cachedDrag
        };
    }

    void ThrowObject()
    {
        if (loaded == Ammo.WaterBalloon)
        {
            ThrowBalloon();
            return;
        }

        if (!objectToThrow) return;

        if (requireAmmo)
        {
            PlayerInventory playerInventory = ResolveInventory();

            if (playerInventory == null || !playerInventory.TryConsume(ammoItem))
                return;
        }

        ProjectileProperties data = ProjectileData();

        Quaternion spawnRot = Quaternion.FromToRotation(data.direction, Vector3.up);

        Rigidbody thrownObject = Instantiate(
            objectToThrow, 
            data.initialPosition, 
            spawnRot
        );

        thrownObject.AddForce(data.direction * currentForce, ForceMode.Impulse);

        Vector3 tumbleAxis = Vector3.Cross(data.direction, Vector3.up);
        if (tumbleAxis.sqrMagnitude < 0.001f)
            tumbleAxis = playerCamera.transform.right;

        tumbleAxis = (tumbleAxis.normalized + UnityEngine.Random.insideUnitSphere * 0.12f).normalized;
        thrownObject.AddTorque(tumbleAxis * tumbleTorque, ForceMode.Impulse);

        ThrownProjectile projectile = thrownObject.GetComponent<ThrownProjectile>();
        if (projectile != null)
            projectile.owner = owner;

        TrailRenderer trail = thrownObject.GetComponentInChildren<TrailRenderer>();
        if (trail != null)
        {
            trail.startColor = trailColor;
            trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
        }

        StartCoroutine(DestroyObject(thrownObject.gameObject));
    }

    void ThrowBalloon()
    {
        if (waterBalloonPrefab == null || !CanFillBalloon())
            return;

        playerNeeds.Pee(bladderPerBalloon);

        ProjectileProperties data = ProjectileData();

        Rigidbody balloon = Instantiate(waterBalloonPrefab, data.initialPosition,
                                        Quaternion.LookRotation(data.direction));

        balloon.AddForce(data.direction * currentForce, ForceMode.Impulse);
        balloon.AddTorque(UnityEngine.Random.insideUnitSphere * tumbleTorque, ForceMode.Impulse);

        WaterBalloon splash = balloon.GetComponent<WaterBalloon>();

        if (splash != null)
            splash.Launch(owner, peePuddle);
        else
            Debug.LogWarning($"[ProjectileThrow] {balloon.name} has no WaterBalloon component, so it will never burst.", balloon);

        TrailRenderer trail = balloon.GetComponentInChildren<TrailRenderer>();
        if (trail != null)
        {
            trail.startColor = balloonTrailColor;
            trail.endColor = new Color(balloonTrailColor.r, balloonTrailColor.g, balloonTrailColor.b, 0f);
        }
    }

    IEnumerator DestroyObject(GameObject objectToDestroy)
    {
        yield return new WaitForSeconds(2);
        Destroy(objectToDestroy);
    }
}
