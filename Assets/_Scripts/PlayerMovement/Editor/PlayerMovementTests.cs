using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerMovementTests
{
    [TestCase(45f, false, false)]
    [TestCase(-45f, true, false)]
    [TestCase(45f, false, true)]
    public void DiagonalMovementSlidesThroughTwoVoxelGap(float yaw, bool sprint, bool fly)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        PhysicsMaterial material = null;
        try
        {
            float voxel = VoxelMetrics.Size;
            AddWall(scene, -1.5f * voxel);
            AddWall(scene, 1.5f * voxel);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/Player.prefab");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            var movement = player.GetComponent<PlayerMovement>();
            typeof(PlayerMovement).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(movement, null);
            var collider = player.GetComponent<Collider>();
            material = collider.sharedMaterial;
            var body = player.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            Physics.SyncTransforms();

            PhysicsScene physics = scene.GetPhysicsScene();
            Assert.That(physics, Is.Not.EqualTo(Physics.defaultPhysicsScene));

            const float step = 0.02f, playerRadius = 0.4f;
            for (int i = 0; i < 8; i++) physics.Simulate(step);
            float restingOffset = Mathf.Abs(body.position.x);
            float wallOffset = Mathf.Sign(yaw) * (voxel - playerRadius);
            float slideError = 0f;
            for (int i = 0; i < 50; i++)
            {
                if (fly) movement.Fly(Vector3.forward, false, false);
                else movement.Walk(Vector3.forward, sprint);
                physics.Simulate(step);
                if (i >= 10) slideError = Mathf.Max(slideError, Mathf.Abs(body.position.x - wallOffset));
            }

            Assert.That(restingOffset, Is.LessThan(0.001f), "Facing diagonally should leave the player centered at rest");
            Assert.That(body.position.z, Is.GreaterThan(3f), "Forward movement through the corridor");
            Assert.That(collider.bounds.size.x, Is.EqualTo(2f * playerRadius).Within(0.001f), "Clearance at diagonal yaw");
            Assert.That(collider.bounds.size.y, Is.EqualTo(2f).Within(0.001f), "Player height");
            Assert.That(slideError, Is.LessThan(0.005f), "Lateral position should stay steady while sliding along the wall");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(material);
        }
    }

    private static void AddWall(Scene scene, float x)
    {
        var wall = new GameObject("Corridor wall");
        SceneManager.MoveGameObjectToScene(wall, scene);
        wall.transform.position = new Vector3(x, 0f, 0f);
        wall.transform.localScale = new Vector3(VoxelMetrics.Size, 4f, 16f);
        wall.AddComponent<MeshCollider>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
    }
}
