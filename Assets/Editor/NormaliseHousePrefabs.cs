using System;
using UnityEditor;
using UnityEngine;

// One-shot fixup for the European Buildings house prefabs.
//
// The pack ships each building with its FBX node scale baked onto the prefab root, which is
// non-uniform on three of the models, and at pack scale the buildings are far too big for the
// map's grid cell. The map manager also overwrites the root's position and rotation when it
// spawns a house, so the facing rotation and the centring offset cannot live on the root
// either. This rebuilds each prefab as root / Model / Front:
//
//   root    a single uniform scale, nothing else
//   Model   the mesh, renderer and collider, plus the pack's node scale, the yaw that turns
//           the facade to local +Z, and the offset that centres the footprint on the pivot
//           and stands the base on y = 0
//   Front   the delivery zone marker, just outside the front wall
//
// Run it once from Tools > Normalise House Prefabs. Running it again is harmless.
public static class NormaliseHousePrefabs
{
    const string folder = "Assets/Prefabs/Map/Houses/";

    // Brings the pack's units into the game's roughly one-unit-per-metre scale.
    const float fitScale = 1f / 3f;

    // World units the Front marker sits ahead of the front wall.
    const float doorstep = 0.6f;

    struct Building
    {
        public string name;

        // Turns the facade to local +Z, which is the side the map manager aims at the road.
        // Read off the geometry: the door_1 material on 2, 3, 4, 6 and 8, the balconies on 7,
        // and the window faces on 1 and 5, which are symmetric so either side would do.
        public float yaw;

        // The scale the FBX node carries. Non-uniform on 3, 6 and 7, which is why it has to
        // sit on the Model child rather than on the root.
        public Vector3 nodeScale;
    }

    static readonly Vector3 packScale = new Vector3(0.633673f, 0.633673f, 0.633673f);

    static readonly Building[] buildings =
    {
        new Building { name = "building_1", yaw = 180f, nodeScale = packScale },
        new Building { name = "building_2", yaw =   0f, nodeScale = packScale },
        new Building { name = "building_3", yaw =   0f, nodeScale = new Vector3(0.027076f, 0.970674f, 0.027076f) },
        new Building { name = "building_4", yaw =   0f, nodeScale = packScale },
        new Building { name = "building_5", yaw = 180f, nodeScale = packScale },
        new Building { name = "building_6", yaw = -90f, nodeScale = new Vector3(0.633673f, 0.518878f, 0.533061f) },
        new Building { name = "building_7", yaw = -90f, nodeScale = new Vector3(0.380856f, 0.633673f, 0.633673f) },
        new Building { name = "building_8", yaw = -90f, nodeScale = packScale },
    };

    [MenuItem("Tools/Normalise House Prefabs")]
    static void Run()
    {
        int done = 0;

        foreach (Building building in buildings)
        {
            // One bad prefab should not stop the other seven from being rebuilt.
            try
            {
                if (Normalise(building)) done++;
            }
            catch (Exception error)
            {
                Debug.LogError($"{building.name} was not rebuilt: {error}");
            }
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"Normalised {done} of {buildings.Length} house prefabs.");
    }

    static bool Normalise(Building building)
    {
        string path = folder + building.name + ".prefab";

        GameObject root = PrefabUtility.LoadPrefabContents(path);

        if (root == null)
        {
            Debug.LogError($"Could not open {path}");
            return false;
        }

        try
        {
            MeshFilter source = root.GetComponentInChildren<MeshFilter>(true);

            if (source == null || source.sharedMesh == null)
            {
                Debug.LogError($"{building.name} has no mesh to work from.", root);
                return false;
            }

            // Hold on to the mesh itself rather than the MeshFilter: moving the renderers to
            // the child destroys the component they came from, but the mesh asset is untouched.
            Mesh mesh = source.sharedMesh;

            Transform model = MoveRenderersToChild(root, "Model");
            Transform front = FindOrCreateChild(root, "Front");

            Quaternion facing = Quaternion.Euler(0f, building.yaw, 0f);

            // The model's box once the node scale and the facing rotation are applied.
            Bounds box = RotatedBounds(mesh.bounds, building.nodeScale, facing);

            model.localRotation = facing;
            model.localScale = building.nodeScale;

            // Centre the footprint on the pivot and stand the base on y = 0.
            model.localPosition = new Vector3(-box.center.x, -box.min.y, -box.center.z);

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * fitScale;

            // Front is a child of the root, so its units are the root's, before the fit scale.
            front.localPosition = new Vector3(0f, 0f, box.extents.z + doorstep / fitScale);
            front.localRotation = Quaternion.identity;
            front.localScale = Vector3.one;

            PrefabUtility.SaveAsPrefabAsset(root, path);

            Debug.Log($"{building.name}: {box.size.x * fitScale:0.00} wide, " +
                      $"{box.size.y * fitScale:0.00} tall, {box.size.z * fitScale:0.00} deep.");

            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // Components cannot be reparented, so the renderers are rebuilt on the child and the
    // originals removed. Only the settings that were actually authored are carried over.
    static Transform MoveRenderersToChild(GameObject root, string childName)
    {
        Transform child = FindOrCreateChild(root, childName);

        child.gameObject.tag = root.tag;

        MeshFilter rootFilter = root.GetComponent<MeshFilter>();
        MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();
        MeshCollider rootCollider = root.GetComponent<MeshCollider>();

        // Copy everything across before destroying anything, so no step reads a component
        // that an earlier step has already removed.
        if (rootFilter != null)
            GetOrAdd<MeshFilter>(child.gameObject).sharedMesh = rootFilter.sharedMesh;

        if (rootRenderer != null)
        {
            MeshRenderer moved = GetOrAdd<MeshRenderer>(child.gameObject);
            moved.sharedMaterials = rootRenderer.sharedMaterials;
            moved.shadowCastingMode = rootRenderer.shadowCastingMode;
            moved.receiveShadows = rootRenderer.receiveShadows;
        }

        if (rootCollider != null)
        {
            MeshCollider moved = GetOrAdd<MeshCollider>(child.gameObject);
            moved.sharedMesh = rootCollider.sharedMesh;
            moved.convex = rootCollider.convex;
        }

        // Renderer before filter: a MeshRenderer left behind with no mesh to draw makes
        // Unity grumble about the pair being incomplete.
        if (rootCollider != null) UnityEngine.Object.DestroyImmediate(rootCollider);
        if (rootRenderer != null) UnityEngine.Object.DestroyImmediate(rootRenderer);
        if (rootFilter != null) UnityEngine.Object.DestroyImmediate(rootFilter);

        return child;
    }

    static Transform FindOrCreateChild(GameObject root, string name)
    {
        Transform existing = root.transform.Find(name);

        if (existing != null) return existing;

        GameObject created = new GameObject(name);
        created.transform.SetParent(root.transform, false);

        return created.transform;
    }

    static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T existing = target.GetComponent<T>();

        return existing != null ? existing : target.AddComponent<T>();
    }

    // Axis aligned bounds of a mesh box after a scale and a rotation are applied to it.
    static Bounds RotatedBounds(Bounds mesh, Vector3 scale, Quaternion rotation)
    {
        Bounds result = new Bounds();
        bool started = false;

        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = mesh.center + Vector3.Scale(
                mesh.extents,
                new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f
                )
            );

            point = rotation * UnityEngine.Vector3.Scale(scale, point);

            if (started) result.Encapsulate(point);
            else { result = new Bounds(point, Vector3.zero); started = true; }
        }

        return result;
    }
}
