using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class StreamingPlayModeTests
{
    [UnityTearDown]
    public IEnumerator EnsureEditModeAfterTest()
    {
        if (Application.isPlaying)
            yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Movement_PreservesFullCollisionAcrossTheTexturedLodRing()
    {
        yield return new EnterPlayMode();
        AsyncOperation load = SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        while (!load.isDone)
            yield return null;

        World world = null;
        GameManager gameManager = null;
        float deadline = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < deadline)
        {
            world = Object.FindFirstObjectByType<World>();
            gameManager = Object.FindFirstObjectByType<GameManager>();
            if (world != null && gameManager != null)
                break;
            yield return null;
        }

        Assert.That(world, Is.Not.Null, "SampleScene did not create its World.");
        Assert.That(gameManager, Is.Not.Null, "SampleScene did not create its GameManager.");
        yield return null;
        float generationStarted = Time.realtimeSinceStartup;
        world.GenerateWorld();

        deadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < deadline && gameManager.PlayerObject == null)
            yield return null;
        Assert.That(gameManager.PlayerObject, Is.Not.Null, "Player did not spawn after center collision became active.");
        float timeToPlayer = Time.realtimeSinceStartup - generationStarted;
        Debug.Log($"STREAMING_TEST timeToPlayer={timeToPlayer * 1000f:0}ms");
        Assert.That(timeToPlayer, Is.LessThan(1f), "Playable center exceeded the one-second target.");

        var activeKeys = new List<Vector3Int>();
        deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
            if (ContainsSafetyNeighborhood(activeKeys, gameManager.currentPlayerChunkPosition, world))
                break;
            yield return null;
        }
        Assert.That(
            ContainsSafetyNeighborhood(activeKeys, gameManager.currentPlayerChunkPosition, world),
            Is.True,
            "Initial surface 3x3 collision neighborhood was not ready.");
        float safetyReady = Time.realtimeSinceStartup - generationStarted;
        Debug.Log($"STREAMING_TEST safetyReady={safetyReady * 1000f:0}ms");
        Assert.That(safetyReady, Is.LessThan(2f), "Nearby collision exceeded the two-second target.");

        deadline = generationStarted + 5f;
        while (Time.realtimeSinceStartup < deadline &&
               !HasSurfaceRenderRadius(world, gameManager.currentPlayerChunkPosition))
            yield return null;
        float radiusReady = Time.realtimeSinceStartup - generationStarted;
        Debug.Log($"STREAMING_TEST radiusReady={radiusReady * 1000f:0}ms renderers={world.worldData.chunkDictionary.Count}");
        Assert.That(
            HasSurfaceRenderRadius(world, gameManager.currentPlayerChunkPosition),
            Is.True,
            "Radius-eight surface rendering did not complete within five seconds.");

        const int frameSamples = 180;
        float sampledSeconds = 0f;
        float worstFrame = 0f;
        for (int frame = 0; frame < frameSamples; frame++)
        {
            yield return null;
            float delta = Time.unscaledDeltaTime;
            sampledSeconds += delta;
            worstFrame = Mathf.Max(worstFrame, delta);
        }
        float averageFps = frameSamples / Mathf.Max(0.0001f, sampledSeconds);
        Debug.Log($"STREAMING_TEST averageFps={averageFps:0.0} worstFrameMs={worstFrame * 1000f:0.0}");
        Assert.That(averageFps, Is.GreaterThan(55f), "Settled radius-eight streaming remained below the performance floor.");
        Assert.That(worstFrame, Is.LessThan(0.25f), "A streaming frame exceeded the hitch ceiling.");

        Vector3Int initialChunk = gameManager.currentPlayerChunkPosition;
        Vector3Int futureChunk = initialChunk + Vector3Int.right * (world.chunkSize * 2);
        Vector3Int transitionChunk = initialChunk + Vector3Int.right * (world.chunkSize * 4);
        Vector3Int distantChunk = initialChunk + Vector3Int.right * (world.chunkSize * 6);
        deadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (world.worldData.chunkDictionary.TryGetValue(futureChunk, out ChunkRenderer future) &&
                future.ActiveLod == ChunkLodLevel.Full &&
                world.worldData.chunkDictionary.TryGetValue(transitionChunk, out ChunkRenderer transition) &&
                transition.ActiveLod == ChunkLodLevel.Merge4 &&
                world.worldData.chunkDictionary.TryGetValue(distantChunk, out ChunkRenderer distant) &&
                distant.ActiveLod == ChunkLodLevel.Merge16)
                break;
            yield return null;
        }
        Assert.That(world.worldData.chunkDictionary.TryGetValue(futureChunk, out ChunkRenderer futureRenderer), Is.True);
        Assert.That(futureRenderer.ActiveLod, Is.EqualTo(ChunkLodLevel.Full));
        Assert.That(futureRenderer.IsCollisionActive, Is.False);
        Assert.That(world.worldData.chunkDictionary[transitionChunk].ActiveLod, Is.EqualTo(ChunkLodLevel.Merge4));
        Assert.That(world.worldData.chunkDictionary[distantChunk].ActiveLod, Is.EqualTo(ChunkLodLevel.Merge16));

        var previousCollision = new HashSet<Vector3Int>(activeKeys);
        GameObject player = gameManager.PlayerObject;
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        Vector3Int approachChunk = initialChunk + Vector3Int.right * world.chunkSize;
        TeleportOneChunkRight(player, body, world);
        yield return null;

        gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
        Assert.That(
            HasContinuousHandoff(previousCollision, activeKeys, approachChunk, world),
            Is.True,
            "Old collision was removed before the replacement neighborhood became active.");

        deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
            if (gameManager.currentPlayerChunkPosition == approachChunk &&
                ContainsSafetyNeighborhood(activeKeys, approachChunk, world) &&
                world.worldData.chunkDictionary.TryGetValue(futureChunk, out futureRenderer) &&
                futureRenderer.ActiveLod == ChunkLodLevel.Full &&
                futureRenderer.IsCollisionActive)
                break;
            yield return null;
        }

        Assert.That(gameManager.currentPlayerChunkPosition, Is.EqualTo(approachChunk));
        Assert.That(ContainsSafetyNeighborhood(activeKeys, approachChunk, world), Is.True);
        Assert.That(world.worldData.chunkDictionary[futureChunk].ActiveLod, Is.EqualTo(ChunkLodLevel.Full));
        Assert.That(world.worldData.chunkDictionary[futureChunk].IsCollisionActive, Is.True);
        Assert.That(player.transform.position.y, Is.GreaterThan(0f), "Player fell through terrain during collision handoff.");

        previousCollision.Clear();
        previousCollision.UnionWith(activeKeys);
        Vector3Int thirdChunk = futureChunk + Vector3Int.right * world.chunkSize;
        TeleportOneChunkRight(player, body, world);
        yield return null;
        gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
        Assert.That(
            HasContinuousHandoff(previousCollision, activeKeys, futureChunk, world),
            Is.True);

        deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
            if (gameManager.currentPlayerChunkPosition == futureChunk &&
                ContainsSafetyNeighborhood(activeKeys, futureChunk, world))
                break;
            yield return null;
        }
        Assert.That(gameManager.currentPlayerChunkPosition, Is.EqualTo(futureChunk));
        Assert.That(ContainsSafetyNeighborhood(activeKeys, futureChunk, world), Is.True);
        Assert.That(player.transform.position.y, Is.GreaterThan(0f));

        previousCollision.Clear();
        previousCollision.UnionWith(activeKeys);
        TeleportOneChunkRight(player, body, world);
        yield return null;
        gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
        Assert.That(
            HasContinuousHandoff(previousCollision, activeKeys, thirdChunk, world),
            Is.True);

        deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
            if (gameManager.currentPlayerChunkPosition == thirdChunk &&
                ContainsSafetyNeighborhood(activeKeys, thirdChunk, world) &&
                world.worldData.chunkDictionary.TryGetValue(initialChunk, out ChunkRenderer original) &&
                original.ActiveLod == ChunkLodLevel.Full)
                break;
            yield return null;
        }

        Assert.That(gameManager.currentPlayerChunkPosition, Is.EqualTo(thirdChunk));
        Assert.That(ContainsSafetyNeighborhood(activeKeys, thirdChunk, world), Is.True);
        Assert.That(world.worldData.chunkDictionary[initialChunk].ActiveLod, Is.EqualTo(ChunkLodLevel.Full));
        Assert.That(player.transform.position.y, Is.GreaterThan(0f));

        previousCollision.Clear();
        previousCollision.UnionWith(activeKeys);
        Vector3Int fourthChunk = thirdChunk + Vector3Int.right * world.chunkSize;
        TeleportOneChunkRight(player, body, world);
        yield return null;
        gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
        Assert.That(
            HasContinuousHandoff(previousCollision, activeKeys, fourthChunk, world),
            Is.True);

        deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
            if (gameManager.currentPlayerChunkPosition == fourthChunk &&
                ContainsSafetyNeighborhood(activeKeys, fourthChunk, world) &&
                world.worldData.chunkDictionary.TryGetValue(initialChunk, out ChunkRenderer original) &&
                original.ActiveLod == ChunkLodLevel.Merge4)
                break;
            yield return null;
        }
        Assert.That(gameManager.currentPlayerChunkPosition, Is.EqualTo(fourthChunk));
        Assert.That(ContainsSafetyNeighborhood(activeKeys, fourthChunk, world), Is.True);
        Assert.That(world.worldData.chunkDictionary[initialChunk].ActiveLod, Is.EqualTo(ChunkLodLevel.Merge4));
        Assert.That(player.transform.position.y, Is.GreaterThan(0f));
        Assert.That(world.IsCollisionSafetyBlocking, Is.False);
        Assert.That(world.RetainedCollisionChunkCount, Is.LessThanOrEqualTo(18),
            "Old collision neighborhoods accumulated instead of being released.");
        Assert.That(activeKeys.TrueForAll(key => key.y == 0), Is.True,
            "Collision must never be satisfied by a hidden chunk below the terrain surface.");

        Vector3Int expectedChunk = fourthChunk;
        for (int step = 0; step < 8; step++)
        {
            previousCollision.Clear();
            previousCollision.UnionWith(activeKeys);
            expectedChunk += Vector3Int.right * world.chunkSize;
            TeleportOneChunkRight(player, body, world);
            yield return null;

            gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
            Assert.That(HasContinuousHandoff(
                    previousCollision, activeKeys, expectedChunk, world), Is.True,
                $"Collision handoff dropped old coverage on stress step {step}.");
            deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
                if (gameManager.currentPlayerChunkPosition == expectedChunk &&
                    ContainsSafetyNeighborhood(activeKeys, expectedChunk, world))
                    break;
                yield return null;
            }

            Assert.That(
                gameManager.currentPlayerChunkPosition,
                Is.EqualTo(expectedChunk),
                DescribeStreamingState(world, player, expectedChunk));
            Assert.That(
                ContainsSafetyNeighborhood(activeKeys, expectedChunk, world),
                Is.True,
                DescribeSafetyNeighborhood(world, activeKeys, expectedChunk));
            Assert.That(activeKeys.TrueForAll(key => key.y == 0), Is.True);
            Assert.That(player.transform.position.y, Is.GreaterThan(0f));
            Assert.That(world.RetainedCollisionChunkCount, Is.LessThanOrEqualTo(18));
        }

        // Return across the entire streamed distance. These chunks had their
        // colliders released and some renderers/data were pooled while travelling
        // forward, which is the path that previously failed to restore collision.
        for (int step = 0; step < 12; step++)
        {
            previousCollision.Clear();
            previousCollision.UnionWith(activeKeys);
            expectedChunk += Vector3Int.left * world.chunkSize;
            TeleportOneChunkLeft(player, body, world);
            yield return null;

            gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
            Assert.That(HasContinuousHandoff(
                    previousCollision, activeKeys, expectedChunk, world), Is.True,
                $"Reverse collision handoff dropped old coverage on step {step}.");
            deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                gameManager.CopyActiveColliderChunkKeysTo(activeKeys);
                if (gameManager.currentPlayerChunkPosition == expectedChunk &&
                    ContainsSafetyNeighborhood(activeKeys, expectedChunk, world))
                    break;
                yield return null;
            }

            Assert.That(gameManager.currentPlayerChunkPosition, Is.EqualTo(expectedChunk));
            Assert.That(ContainsSafetyNeighborhood(activeKeys, expectedChunk, world), Is.True,
                $"Collision was not restored after reversing onto chunk {expectedChunk}. " +
                DescribeSafetyNeighborhood(world, activeKeys, expectedChunk));
            AssertPhysicalGround(world, expectedChunk);
            Assert.That(activeKeys.TrueForAll(key => key.y == 0), Is.True);
            Assert.That(player.transform.position.y, Is.GreaterThan(0f));
            Assert.That(world.RetainedCollisionChunkCount, Is.LessThanOrEqualTo(18));
            Assert.That(world.PendingCollisionBakeCount, Is.LessThanOrEqualTo(18),
                "Obsolete collider bakes accumulated during reverse traversal.");
        }
        Assert.That(expectedChunk, Is.EqualTo(initialChunk));
        yield return new ExitPlayMode();
    }

    private static bool ContainsSafetyNeighborhood(
        List<Vector3Int> active,
        Vector3Int center,
        World world)
    {
        var keys = new HashSet<Vector3Int>(active);
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                if (!keys.Contains(new Vector3Int(
                        center.x + x * world.chunkSize,
                        0,
                        center.z + z * world.chunkSize)))
                    return false;
            }
        }
        return true;
    }

    private static bool HasContinuousHandoff(
        HashSet<Vector3Int> previous,
        List<Vector3Int> active,
        Vector3Int newCenter,
        World world)
    {
        return previous.IsSubsetOf(active) ||
               ContainsSafetyNeighborhood(active, newCenter, world);
    }

    private static void TeleportOneChunkRight(GameObject player, Rigidbody body, World world)
    {
        TeleportOneChunk(player, body, world, Vector3.right);
    }

    private static void TeleportOneChunkLeft(GameObject player, Rigidbody body, World world)
    {
        TeleportOneChunk(player, body, world, Vector3.left);
    }

    private static void TeleportOneChunk(
        GameObject player,
        Rigidbody body,
        World world,
        Vector3 direction)
    {
        Vector3 destination = player.transform.position +
                              direction * (world.chunkSize * VoxelMetrics.Size);
        destination = PlaceOnTargetSurfaceWhenAvailable(player, world, destination);
        SetPlayerPosition(player, body, destination);
    }

    private static Vector3 PlaceOnTargetSurfaceWhenAvailable(
        GameObject player,
        World world,
        Vector3 destination)
    {
        Vector3Int voxel = VoxelMetrics.WorldToVoxelCoord(destination);
        Vector3Int key = WorldDataHelper.ChunkPositionFromVoxelCoords(world, voxel);
        key.y = 0;
        if (!world.worldData.chunkDictionary.TryGetValue(key, out ChunkRenderer expected) ||
            !expected.IsCollisionActive)
            return destination;

        float scale = VoxelMetrics.Size;
        Vector3 origin = new Vector3(
            destination.x,
            (world.chunkHeight + 20) * scale,
            destination.z);
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            (world.chunkHeight + 40) * scale,
            ~0,
            QueryTriggerInteraction.Ignore);
        float surface = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider != null && hit.collider.GetComponent<ChunkRenderer>() == expected)
                surface = Mathf.Max(surface, hit.point.y);
        }
        if (!float.IsFinite(surface))
            return destination;

        Collider playerCollider = player.GetComponent<Collider>();
        float feetOffset = playerCollider != null
            ? Mathf.Max(0.05f, player.transform.position.y - playerCollider.bounds.min.y)
            : scale;
        destination.y = surface + feetOffset + 0.02f;
        return destination;
    }

    private static void SetPlayerPosition(GameObject player, Rigidbody body, Vector3 destination)
    {
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = destination;
        }
        // Rigidbody interpolation otherwise leaves Transform on the previous
        // position until a physics step, which is not an immediate reversal.
        player.transform.position = destination;
        Physics.SyncTransforms();
    }

    private static void AssertPhysicalGround(World world, Vector3Int chunk)
    {
        Vector3Int surfaceChunk = new Vector3Int(chunk.x, 0, chunk.z);
        Assert.That(
            world.worldData.chunkDictionary.TryGetValue(surfaceChunk, out ChunkRenderer expected),
            Is.True,
            $"No renderer exists for collision chunk {surfaceChunk}.");

        float scale = VoxelMetrics.Size;
        Vector3 origin = new Vector3(
            (surfaceChunk.x + world.chunkSize * 0.5f) * scale,
            (world.chunkHeight + 20) * scale,
            (surfaceChunk.z + world.chunkSize * 0.5f) * scale);
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            (world.chunkHeight + 40) * scale,
            ~0,
            QueryTriggerInteraction.Ignore);
        bool found = false;
        var hitSummary = new List<string>();
        foreach (RaycastHit hit in hits)
        {
            ChunkRenderer hitChunk = hit.collider != null
                ? hit.collider.GetComponent<ChunkRenderer>()
                : null;
            hitSummary.Add(hitChunk != null && hitChunk.ChunkData != null
                ? $"{hitChunk.ChunkData.worldPosition}@{hit.point}"
                : $"{(hit.collider != null ? hit.collider.name : "null")}@{hit.point}");
            if (hit.collider != null && hit.collider.GetComponent<ChunkRenderer>() == expected)
            {
                found = true;
                break;
            }
        }
        MeshCollider expectedCollider = expected.GetComponent<MeshCollider>();
        Mesh shared = expectedCollider != null ? expectedCollider.sharedMesh : null;
        Assert.That(found, Is.True,
            $"Chunk {surfaceChunk} reported active collision but had no raycastable MeshCollider. " +
            $"rendererActive={expected.gameObject.activeInHierarchy}, " +
            $"colliderEnabled={(expectedCollider != null && expectedCollider.enabled)}, " +
            $"sharedMesh={(shared != null ? shared.name : "null")}, " +
            $"vertices={(shared != null ? shared.vertexCount : -1)}, " +
            $"triangles={(shared != null ? shared.triangles.Length / 3 : -1)}, " +
            $"transform={expected.transform.position}, origin={origin}, " +
            $"hits=[{string.Join(", ", hitSummary)}].");
    }

    private static string DescribeStreamingState(
        World world,
        GameObject player,
        Vector3Int chunk)
    {
        Vector3Int key = new Vector3Int(chunk.x, 0, chunk.z);
        string rendererState = "missing";
        if (world.worldData.chunkDictionary.TryGetValue(key, out ChunkRenderer renderer))
        {
            MeshCollider collider = renderer.GetComponent<MeshCollider>();
            rendererState = $"active={renderer.IsCollisionActive}, " +
                            $"enabled={(collider != null && collider.enabled)}, " +
                            $"shared={(collider != null && collider.sharedMesh != null)}, " +
                            $"reusable={renderer.HasReusableCollision}, " +
                            $"activeVersion={renderer.ActiveCollisionContentVersion}, " +
                            $"contentVersion={(renderer.ChunkData != null ? renderer.ChunkData.ContentVersion : -1)}";
        }
        Character character = player.GetComponent<Character>();
        return $"player={player.transform.position}, block={world.IsCollisionSafetyBlocking}, " +
               $"fly={(character != null && character.fly)}, renderer=[{rendererState}]";
    }

    private static string DescribeSafetyNeighborhood(
        World world,
        List<Vector3Int> active,
        Vector3Int center)
    {
        var activeSet = new HashSet<Vector3Int>(active);
        var states = new List<string>();
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector3Int key = new Vector3Int(
                    center.x + x * world.chunkSize,
                    0,
                    center.z + z * world.chunkSize);
                if (activeSet.Contains(key))
                    continue;
                if (!world.worldData.chunkDictionary.TryGetValue(key, out ChunkRenderer renderer))
                {
                    states.Add($"{key}:renderer-missing");
                    continue;
                }
                MeshCollider collider = renderer.GetComponent<MeshCollider>();
                states.Add($"{key}:enabled={(collider != null && collider.enabled)}," +
                           $"shared={(collider != null && collider.sharedMesh != null)}," +
                           $"reuse={renderer.HasReusableCollision}," +
                           $"applied={renderer.AppliedContentVersion}," +
                           $"activeVersion={renderer.ActiveCollisionContentVersion}," +
                        $"resolved={renderer.CollisionResolvedContentVersion}," +
                        $"preparedVerts={renderer.LastPreparedCollisionVertexCount}," +
                        $"preparedIndices={renderer.LastPreparedCollisionTriangleCount}," +
                        $"activationFailure={renderer.LastCollisionActivationFailure}," +
                           $"content={(renderer.ChunkData != null ? renderer.ChunkData.ContentVersion : -1)}");
            }
        }
        return $"Missing collision: [{string.Join("; ", states)}], " +
            $"pendingMeshes={world.PendingMeshChunks}, readyMeshes={world.ReadyMeshChunks}, " +
            $"oldestCollision={world.OldestPendingCollisionSeconds:0.000}s, " +
            $"pendingBakes={world.PendingCollisionBakeCount}, " +
               $"queued={world.QueuedWorkerItems}, retained={world.RetainedCollisionChunkCount}, " +
               $"blocked={world.IsCollisionSafetyBlocking}.";
    }

    private static bool HasSurfaceRenderRadius(World world, Vector3Int center)
    {
        for (int x = -world.chunkDrawingRange; x <= world.chunkDrawingRange; x++)
        {
            for (int z = -world.chunkDrawingRange; z <= world.chunkDrawingRange; z++)
            {
                Vector3Int key = new Vector3Int(
                    center.x + x * world.chunkSize,
                    0,
                    center.z + z * world.chunkSize);
                if (!world.worldData.chunkDictionary.TryGetValue(key, out ChunkRenderer renderer) ||
                    renderer.AppliedContentVersion < 0)
                    return false;
            }
        }
        return true;
    }
}
