using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

public partial class World
{
    private readonly struct DataBuildResult
    {
        public readonly int session;
        public readonly int requestId;
        public readonly Vector3Int position;
        public readonly ChunkData data;

        public DataBuildResult(int session, int requestId, Vector3Int position, ChunkData data)
        {
            this.session = session;
            this.requestId = requestId;
            this.position = position;
            this.data = data;
        }
    }

    private readonly struct MeshRequestTicket
    {
        public readonly int requestId;
        public readonly ChunkLodLevel lod;
        public readonly int contentVersion;
        public readonly int neighbourHash;
        public readonly bool collision;
        public readonly float queuedAt;

        public MeshRequestTicket(
            int requestId,
            ChunkLodLevel lod,
            int contentVersion,
            int neighbourHash,
            bool collision,
            float queuedAt)
        {
            this.requestId = requestId;
            this.lod = lod;
            this.contentVersion = contentVersion;
            this.neighbourHash = neighbourHash;
            this.collision = collision;
            this.queuedAt = queuedAt;
        }
    }

    private readonly struct MeshBuildResult
    {
        public readonly int session;
        public readonly Vector3Int position;
        public readonly MeshRequestTicket ticket;
        public readonly MeshData mesh;

        public MeshBuildResult(int session, Vector3Int position, MeshRequestTicket ticket, MeshData mesh)
        {
            this.session = session;
            this.position = position;
            this.ticket = ticket;
            this.mesh = mesh;
        }
    }

    private readonly struct CollisionBakeResult
    {
        public readonly int session;
        public readonly int requestId;
        public readonly Vector3Int position;
        public readonly ChunkRenderer.CollisionPreparation preparation;
        public readonly bool success;

        public CollisionBakeResult(
            int session,
            int requestId,
            Vector3Int position,
            ChunkRenderer.CollisionPreparation preparation,
            bool success)
        {
            this.session = session;
            this.requestId = requestId;
            this.position = position;
            this.preparation = preparation;
            this.success = success;
        }
    }

    private readonly struct PendingCollisionBake
    {
        public readonly int session;
        public readonly int requestId;
        public readonly ChunkRenderer.CollisionPreparation preparation;

        public PendingCollisionBake(
            int session,
            int requestId,
            ChunkRenderer.CollisionPreparation preparation)
        {
            this.session = session;
            this.requestId = requestId;
            this.preparation = preparation;
        }
    }

    private static readonly ProfilerMarker StreamUpdateMarker = new ProfilerMarker("VoxelWorld.StreamUpdate");
    private static readonly ProfilerMarker MeshUploadMarker = new ProfilerMarker("VoxelWorld.MeshUploads");
    private static readonly ProfilerMarker DataApplyMarker = new ProfilerMarker("VoxelWorld.DataCompletions");
    private static readonly ProfilerMarker DataGenerationMarker = new ProfilerMarker("VoxelWorld.GenerateData.Worker");
    private static readonly ProfilerMarker MeshBuildMarker = new ProfilerMarker("VoxelWorld.BuildMesh.Worker");
    private static readonly RaycastHit[] CollisionRecoveryHits = new RaycastHit[32];
    private const float CollisionMeshWorkerTimeoutSeconds = 0.75f;

    private PriorityWorkScheduler workScheduler;
    private TerrainGenerationSnapshot terrainSnapshot;
    private Transform streamingTarget;
    private Rigidbody streamingTargetBody;
    private PlayerMovement streamingTargetMovement;
    private Character streamingTargetCharacter;
    private Vector3 lastCollisionSafePosition;
    private bool hasCollisionSafePosition;
    private bool collisionMovementBlocked;
    private bool collisionTransformsDirty;
    private Vector3Int blockedCollisionChunk;
    private Vector3 blockedTargetPosition;
    private bool streamingStarted;
    private int worldSession;
    private int nextRequestId;
    private Vector3Int streamCenterVoxel;
    private Vector3Int streamCenterChunk;
    private Vector3Int bootstrapChunk;
    private float streamStartedAt;

    private readonly HashSet<Vector3Int> desiredRender = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> desiredData = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> desiredCollision = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> retainedCollision = new HashSet<Vector3Int>();
    private readonly Dictionary<Vector3Int, ChunkLodLevel> desiredLods = new Dictionary<Vector3Int, ChunkLodLevel>();
    private readonly Dictionary<Vector3Int, int> pendingData = new Dictionary<Vector3Int, int>();
    private readonly Dictionary<Vector3Int, MeshRequestTicket> pendingMeshes = new Dictionary<Vector3Int, MeshRequestTicket>();
    private readonly Dictionary<Vector3Int, PendingCollisionBake> pendingCollisionBakes =
        new Dictionary<Vector3Int, PendingCollisionBake>();
    private readonly ConcurrentQueue<DataBuildResult> completedData = new ConcurrentQueue<DataBuildResult>();
    private readonly ConcurrentQueue<MeshBuildResult> completedMeshes = new ConcurrentQueue<MeshBuildResult>();
    private readonly ConcurrentQueue<CollisionBakeResult> completedBakes = new ConcurrentQueue<CollisionBakeResult>();
    private readonly ConcurrentQueue<Exception> workerErrors = new ConcurrentQueue<Exception>();
    private readonly ConcurrentDictionary<int, byte> cancelledWork = new ConcurrentDictionary<int, byte>();
    private readonly List<MeshBuildResult> readyMeshes = new List<MeshBuildResult>();

    public int QueuedWorkerItems => workScheduler != null ? workScheduler.QueuedCount : 0;
    public int PendingDataChunks => pendingData.Count;
    public int PendingMeshChunks => pendingMeshes.Count;
    public int ReadyMeshChunks => readyMeshes.Count + completedMeshes.Count;
    public float OldestPendingCollisionSeconds
    {
        get
        {
            float oldest = 0f;
            float now = Time.realtimeSinceStartup;
            foreach (MeshRequestTicket ticket in pendingMeshes.Values)
            {
                if (ticket.collision)
                    oldest = Mathf.Max(oldest, now - ticket.queuedAt);
            }
            return oldest;
        }
    }
    public int PendingCollisionBakeCount => pendingCollisionBakes.Count;
    public int RetainedCollisionChunkCount => retainedCollision.Count;
    public bool IsCollisionSafetyBlocking => collisionMovementBlocked;

    private void InitializeStreaming()
    {
        terrainSnapshot = terrainGenerator != null ? terrainGenerator.CreateSnapshot() : null;
        workScheduler = new PriorityWorkScheduler(ResolveGenerationWorkerCount());
        // MeshCollider assignment performs the small, surface-only cook on the
        // main thread. Unity's background Physics.BakeMesh lane could stop
        // returning after repeated direction changes, permanently orphaning a
        // required collider.
    }

    private void ShutdownStreaming()
    {
        worldSession++;
        workScheduler?.Dispose();
        workScheduler = null;
        streamingStarted = false;
        collisionMovementBlocked = false;
        hasCollisionSafePosition = false;
    }

    private void BeginStreaming(Vector3Int playerVoxel)
    {
        if (workScheduler == null)
            InitializeStreaming();
        if (terrainSnapshot == null && terrainGenerator != null)
            terrainSnapshot = terrainGenerator.CreateSnapshot();
        VoxelDataManager voxelManager = FindFirstObjectByType<VoxelDataManager>();
        if (voxelManager != null)
            voxelManager.RebuildRuntimeData();

        worldSession++;
        cancelledWork.Clear();
        foreach (int requestId in pendingData.Values)
            CancelWork(requestId);
        foreach (MeshRequestTicket ticket in pendingMeshes.Values)
            CancelWork(ticket.requestId);
        pendingData.Clear();
        pendingMeshes.Clear();
        desiredLods.Clear();
        pendingCollisionBakes.Clear();
        readyMeshes.Clear();
        while (completedData.TryDequeue(out _)) { }
        while (completedMeshes.TryDequeue(out _)) { }
        while (completedBakes.TryDequeue(out _)) { }
        streamStartedAt = Time.realtimeSinceStartup;
        streamingStarted = true;
        IsWorldCreated = false;
        collisionMovementBlocked = false;
        hasCollisionSafePosition = false;
        streamCenterVoxel = playerVoxel;
        streamCenterChunk = WorldDataHelper.ChunkPositionFromVoxelCoords(this, playerVoxel);
        bootstrapChunk = new Vector3Int(streamCenterChunk.x, 0, streamCenterChunk.z);
        RefreshDesiredSets();
    }

    public void SetStreamingTarget(Transform target)
    {
        if (streamingTarget != target)
        {
            streamingTargetBody = target != null ? target.GetComponent<Rigidbody>() : null;
            if (streamingTargetBody != null &&
                streamingTargetBody.collisionDetectionMode == CollisionDetectionMode.Discrete)
                streamingTargetBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            streamingTargetMovement = target != null ? target.GetComponent<PlayerMovement>() : null;
            streamingTargetCharacter = target != null ? target.GetComponent<Character>() : null;
            collisionMovementBlocked = false;
            hasCollisionSafePosition = false;
        }
        streamingTarget = target;
        if (target == null)
            return;
        Vector3Int voxel = VoxelMetrics.WorldToVoxelCoord(target.position);
        if (!streamingStarted)
            BeginStreaming(voxel);
        else
        {
            streamCenterVoxel = voxel;
            Vector3Int nextChunk = WorldDataHelper.ChunkPositionFromVoxelCoords(this, voxel);
            if (nextChunk != streamCenterChunk)
            {
                streamCenterChunk = nextChunk;
                RefreshDesiredSets();
            }
        }
        if (IsCollisionChunkReady(
                WorldDataHelper.ChunkPositionFromVoxelCoords(this, voxel)))
            RecordCollisionSafePosition();
    }

    private void Update()
    {
        if (!streamingStarted)
            return;
        using (StreamUpdateMarker.Auto())
        {
            UpdateStreamingTargetPosition();

            ProcessWorkerErrors();
            ProcessDataResults();
            ProcessMeshResults();
            ProcessCollisionBakeResults();
            RecoverStalledCollisionMeshRequests();
            UpdateCollisionRetention();
            SyncReactivatedColliders();
            TryCompleteBootstrap();
            EnforceStreamingCollisionSafety();
        }
    }

    private void UpdateStreamingTargetPosition()
    {
        if (streamingTarget == null)
            return;

        streamCenterVoxel = VoxelMetrics.WorldToVoxelCoord(streamingTarget.position);
        Vector3Int nextChunk = WorldDataHelper.ChunkPositionFromVoxelCoords(this, streamCenterVoxel);
        if (collisionMovementBlocked)
        {
            // The player may retreat or turn sideways before the abandoned
            // forward chunk finishes. Never keep streaming pinned to that stale
            // blocked direction; recenter on the chunk the player actually chose.
            if (nextChunk != blockedCollisionChunk)
                collisionMovementBlocked = false;
            else if (!IsCollisionChunkReady(blockedCollisionChunk))
                return;
            else
            {
                if (!TryRecoverBlockedTargetAboveSurface())
                    return;
                collisionMovementBlocked = false;
                streamCenterVoxel = VoxelMetrics.WorldToVoxelCoord(streamingTarget.position);
                nextChunk = WorldDataHelper.ChunkPositionFromVoxelCoords(this, streamCenterVoxel);
            }
        }

        if (nextChunk != streamCenterChunk)
        {
            streamCenterChunk = nextChunk;
            RefreshDesiredSets();
        }
    }

    private void RefreshDesiredSets()
    {
        // Retention is for proven safety coverage, not every requested bake.
        // Keeping unfinished requests from an abandoned direction alive starved
        // the direction the player turned toward on the serialized bake lane.
        foreach (Vector3Int position in desiredCollision)
        {
            if (IsActiveCollisionCurrent(position))
                retainedCollision.Add(position);
        }
        desiredRender.Clear();
        desiredData.Clear();
        desiredCollision.Clear();

        int centerX = streamCenterChunk.x;
        int centerZ = streamCenterChunk.z;
        int surfaceY = 0;
        for (int x = -chunkDrawingRange; x <= chunkDrawingRange; x++)
        {
            for (int z = -chunkDrawingRange; z <= chunkDrawingRange; z++)
                desiredRender.Add(new Vector3Int(centerX + x * chunkSize, surfaceY, centerZ + z * chunkSize));
        }

        int dataRadius = chunkDrawingRange + 1;
        for (int x = -dataRadius; x <= dataRadius; x++)
        {
            for (int z = -dataRadius; z <= dataRadius; z++)
                desiredData.Add(new Vector3Int(centerX + x * chunkSize, surfaceY, centerZ + z * chunkSize));
        }

        int lowestUndergroundY = Mathf.Min(-chunkHeight, streamCenterChunk.y - chunkHeight);
        for (int y = -chunkHeight; y >= lowestUndergroundY; y -= chunkHeight)
        {
            for (int x = -2; x <= 2; x++)
            {
                for (int z = -2; z <= 2; z++)
                    desiredData.Add(new Vector3Int(centerX + x * chunkSize, y, centerZ + z * chunkSize));
            }

            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int underground = new Vector3Int(
                        centerX + x * chunkSize, y, centerZ + z * chunkSize);
                    desiredRender.Add(underground);
                    desiredCollision.Add(underground);
                }
            }
        }

        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                desiredCollision.Add(new Vector3Int(
                    centerX + x * chunkSize, surfaceY, centerZ + z * chunkSize));
            }
        }

        CancelOutOfRangeWork();
        RefreshDesiredLods();
        QueueMissingData();
        TryScheduleMeshes();
        PruneUnneededChunksAndData();
    }

    private void RefreshDesiredLods()
    {
        var staleKeys = new List<Vector3Int>();
        foreach (Vector3Int key in desiredLods.Keys)
        {
            if (!desiredRender.Contains(key))
                staleKeys.Add(key);
        }
        foreach (Vector3Int key in staleKeys)
            desiredLods.Remove(key);

        foreach (Vector3Int position in desiredRender)
        {
            desiredLods.TryGetValue(position, out ChunkLodLevel previous);
            bool hasPrevious = desiredLods.ContainsKey(position);
            if (!hasPrevious && worldData.chunkDictionary.TryGetValue(position, out ChunkRenderer renderer))
            {
                previous = renderer.ActiveLod;
                hasPrevious = true;
            }

            ChunkLodLevel selected = DistanceSimplifier.SelectLod(
                streamCenterVoxel,
                position,
                chunkSize,
                previous,
                hasPrevious,
                distanceSimplification);
            bool changed = !hasPrevious || selected != previous;
            desiredLods[position] = selected;
            if (changed)
                TryScheduleMesh(position, true);
        }
    }

    private void QueueMissingData()
    {
        var missing = new List<Vector3Int>();
        foreach (Vector3Int position in desiredData)
        {
            if (!worldData.chunkDataDictionary.ContainsKey(position) && !pendingData.ContainsKey(position))
                missing.Add(position);
        }
        missing.Sort((a, b) => WorkPriority(a, desiredCollision.Contains(a), 0)
            .CompareTo(WorkPriority(b, desiredCollision.Contains(b), 0)));
        foreach (Vector3Int position in missing)
        {
            int requestId = ++nextRequestId;
            int session = worldSession;
            pendingData[position] = requestId;
            TerrainGenerationSnapshot snapshot = terrainSnapshot;
            Vector2Int seed = mapSeedOffset;
            int size = chunkSize;
            int height = chunkHeight;
            int priority = WorkPriority(position, desiredCollision.Contains(position), 0);
            workScheduler.Enqueue(priority, requestId, token =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (ConsumeCancellation(requestId))
                        return;
                    ChunkData data = new ChunkData(size, height, this, position);
                    using (DataGenerationMarker.Auto())
                        snapshot.Generate(data, seed);
                    if (ConsumeCancellation(requestId))
                        return;
                    completedData.Enqueue(new DataBuildResult(session, requestId, position, data));
                }
                catch (OperationCanceledException) { }
                catch (Exception exception) { workerErrors.Enqueue(exception); }
            });
        }
    }

    private void ProcessDataResults()
    {
        using (DataApplyMarker.Auto())
        {
            int applied = 0;
            while (applied < 96 && completedData.TryDequeue(out DataBuildResult result))
            {
                cancelledWork.TryRemove(result.requestId, out _);
                if (!pendingData.TryGetValue(result.position, out int requestId) || requestId != result.requestId)
                    continue;
                pendingData.Remove(result.position);
                if (result.session != worldSession || !desiredData.Contains(result.position))
                    continue;
                if (!worldData.chunkDataDictionary.ContainsKey(result.position))
                    worldData.chunkDataDictionary.Add(result.position, result.data);
                applied++;
            }
            if (applied > 0)
            {
                TryScheduleMeshes();
            }
        }
    }

    private void TryScheduleMeshes()
    {
        var positions = new List<Vector3Int>(desiredRender);
        positions.Sort((a, b) => WorkPriority(a, desiredCollision.Contains(a), 0)
            .CompareTo(WorkPriority(b, desiredCollision.Contains(b), 0)));
        foreach (Vector3Int position in positions)
            TryScheduleMesh(position, false);
    }

    private void TryScheduleMesh(Vector3Int position, bool force)
    {
        if (!desiredRender.Contains(position) || !desiredLods.TryGetValue(position, out ChunkLodLevel lod) ||
            !worldData.chunkDataDictionary.TryGetValue(position, out ChunkData data))
            return;

        bool collision = desiredCollision.Contains(position);
        if (collision && worldData.chunkDictionary.TryGetValue(position, out ChunkRenderer cached) &&
            !cached.IsCollisionActive && cached.TryReactivateCollision(data.ContentVersion))
            collisionTransformsDirty = true;

        if (!HasHorizontalHalo(position))
            return;
        // A retained collider is the safety bridge to the new 3x3. Do not replace
        // its render mesh or start another bake merely because its visual LOD
        // changed while moving away. It will be updated after replacement
        // collision is live and retention has been released.
        if (!collision && retainedCollision.Contains(position) && IsActiveCollisionCurrent(position))
            return;
        if (!force && worldData.chunkDictionary.TryGetValue(position, out ChunkRenderer existing) &&
            existing.ActiveLod == lod && existing.AppliedContentVersion == data.ContentVersion &&
            (!collision || IsCollisionBuildCurrent(position) || pendingCollisionBakes.ContainsKey(position)))
            return;
        if (collision && pendingCollisionBakes.ContainsKey(position))
            return;

        ChunkMeshContext context = CreateMeshContext(position);
        if (context == null)
            return;
        int requestContextHash = context.NeighbourVersionHash;

        if (pendingMeshes.TryGetValue(position, out MeshRequestTicket pending))
        {
            if (!force && pending.lod == lod && pending.contentVersion == data.ContentVersion &&
                pending.neighbourHash == requestContextHash && pending.collision == collision)
                return;
            CancelWork(pending.requestId);
        }

        var ticket = new MeshRequestTicket(
            ++nextRequestId,
            lod,
            data.ContentVersion,
            requestContextHash,
            collision,
            Time.realtimeSinceStartup);
        pendingMeshes[position] = ticket;
        int session = worldSession;
        int priority = WorkPriority(position, collision, 10);
        workScheduler.Enqueue(priority, ticket.requestId, token =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (ConsumeCancellation(ticket.requestId))
                    return;
                MeshData mesh;
                using (MeshBuildMarker.Auto())
                {
                    mesh = DistanceSimplifier.BuildMesh(context, lod, collision);
                }
                if (ConsumeCancellation(ticket.requestId))
                    return;
                completedMeshes.Enqueue(new MeshBuildResult(session, position, ticket, mesh));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { workerErrors.Enqueue(exception); }
        });
    }

    private bool HasHorizontalHalo(Vector3Int position)
    {
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                if (!worldData.chunkDataDictionary.ContainsKey(position + new Vector3Int(x * chunkSize, 0, z * chunkSize)))
                    return false;
            }
        }
        return true;
    }

    private ChunkMeshContext CreateMeshContext(Vector3Int position)
    {
        if (!worldData.chunkDataDictionary.TryGetValue(position, out ChunkData center))
            return null;
        var neighborhood = new Dictionary<Vector3Int, ChunkData>();
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int key = position + new Vector3Int(
                        x * chunkSize, y * chunkHeight, z * chunkSize);
                    if (worldData.chunkDataDictionary.TryGetValue(key, out ChunkData data))
                        neighborhood.Add(key, data);
                }
            }
        }

        return new ChunkMeshContext(center, neighborhood);
    }

    private void ProcessMeshResults()
    {
        while (completedMeshes.TryDequeue(out MeshBuildResult completed))
        {
            cancelledWork.TryRemove(completed.ticket.requestId, out _);
            readyMeshes.Add(completed);
        }
        if (readyMeshes.Count == 0)
            return;

        readyMeshes.Sort((a, b) => WorkPriority(b.position, b.ticket.collision, 0)
            .CompareTo(WorkPriority(a.position, a.ticket.collision, 0)));
        using (MeshUploadMarker.Auto())
        {
            Stopwatch timer = Stopwatch.StartNew();
            int uploaded = 0;
            bool appliedAny = false;
            while (readyMeshes.Count > 0 && uploaded < meshUploadsPerFrame)
            {
                int last = readyMeshes.Count - 1;
                MeshBuildResult result = readyMeshes[last];
                readyMeshes.RemoveAt(last);
                if (!IsCurrent(result))
                {
                    if (pendingMeshes.TryGetValue(result.position, out MeshRequestTicket stale) &&
                        stale.requestId == result.ticket.requestId)
                    {
                        pendingMeshes.Remove(result.position);
                        TryScheduleMesh(result.position, true);
                    }
                    continue;
                }

                pendingMeshes.Remove(result.position);
                bool created = !worldData.chunkDictionary.TryGetValue(result.position, out ChunkRenderer renderer);
                if (created)
                {
                    renderer = worldRenderer.RenderChunk(
                        worldData,
                        result.position);
                    worldData.chunkDictionary.Add(result.position, renderer);
                    if (SpecialVoxels != null)
                        SpecialVoxels.ShowSpecialPrefabsForChunk(result.position);
                }

                ChunkRenderer.CollisionPreparation preparation = renderer.ApplyMesh(
                    result.mesh,
                    result.ticket.lod,
                    result.ticket.contentVersion,
                    result.ticket.collision);
                if (preparation.IsValid)
                    QueueCollisionBake(result.position, preparation);
                else if (!desiredCollision.Contains(result.position) && !retainedCollision.Contains(result.position))
                    renderer.SetCollisionActive(false);

                OnChunkRendered?.Invoke(result.position, renderer);
                uploaded++;
                appliedAny = true;
                if (IsWorldCreated && timer.Elapsed.TotalMilliseconds >= meshUploadBudgetMs)
                    break;
            }
            if (appliedAny)
                OnNewChunksGenerated?.Invoke();
        }
    }

    private void RecoverStalledCollisionMeshRequests()
    {
        if (pendingMeshes.Count == 0)
            return;

        float now = Time.realtimeSinceStartup;
        Vector3Int stalledPosition = default;
        MeshRequestTicket stalledTicket = default;
        bool foundStalled = false;
        foreach (KeyValuePair<Vector3Int, MeshRequestTicket> pair in pendingMeshes)
        {
            MeshRequestTicket ticket = pair.Value;
            if (!ticket.collision || !desiredCollision.Contains(pair.Key) ||
                now - ticket.queuedAt < CollisionMeshWorkerTimeoutSeconds)
                continue;

            // Recover only one safety chunk per frame. A mesh worker cannot be
            // forcibly interrupted once it is building, and repeatedly replacing
            // its ticket could starve the same collider forever while the player
            // changes direction. The old request is invalidated by its id; this
            // bounded main-thread fallback guarantees progress even if a worker
            // result is orphaned or severely delayed.
            stalledPosition = pair.Key;
            stalledTicket = ticket;
            foundStalled = true;
            break;
        }

        if (!foundStalled)
            return;

        CancelWork(stalledTicket.requestId);
        pendingMeshes.Remove(stalledPosition);
        if (!desiredRender.Contains(stalledPosition) ||
            !desiredCollision.Contains(stalledPosition) ||
            !desiredLods.TryGetValue(stalledPosition, out ChunkLodLevel lod) ||
            !worldData.chunkDataDictionary.TryGetValue(stalledPosition, out ChunkData data))
            return;

        ChunkMeshContext context = CreateMeshContext(stalledPosition);
        if (context == null || !HasHorizontalHalo(stalledPosition))
        {
            TryScheduleMesh(stalledPosition, true);
            return;
        }

        var recoveryTicket = new MeshRequestTicket(
            ++nextRequestId,
            lod,
            data.ContentVersion,
            context.NeighbourVersionHash,
            true,
            now);
        pendingMeshes[stalledPosition] = recoveryTicket;
        try
        {
            using (MeshBuildMarker.Auto())
            {
                MeshData mesh = DistanceSimplifier.BuildMesh(context, lod, true);
                readyMeshes.Add(new MeshBuildResult(
                    worldSession,
                    stalledPosition,
                    recoveryTicket,
                    mesh));
            }
        }
        catch (Exception exception)
        {
            pendingMeshes.Remove(stalledPosition);
            Debug.LogException(exception);
        }
    }

    private bool IsCurrent(MeshBuildResult result)
    {
        if (result.session != worldSession || !desiredRender.Contains(result.position) ||
            !pendingMeshes.TryGetValue(result.position, out MeshRequestTicket pending) ||
            pending.requestId != result.ticket.requestId ||
            !desiredLods.TryGetValue(result.position, out ChunkLodLevel lod) || lod != result.ticket.lod ||
            result.ticket.collision != desiredCollision.Contains(result.position) ||
            !worldData.chunkDataDictionary.TryGetValue(result.position, out ChunkData data) ||
            data.ContentVersion != result.ticket.contentVersion)
            return false;
        ChunkMeshContext currentContext = CreateMeshContext(result.position);
        return currentContext != null &&
               currentContext.NeighbourVersionHash == result.ticket.neighbourHash;
    }

    private void QueueCollisionBake(Vector3Int position, ChunkRenderer.CollisionPreparation preparation)
    {
        int requestId = ++nextRequestId;
        int session = worldSession;
        pendingCollisionBakes[position] = new PendingCollisionBake(session, requestId, preparation);
        // ProcessCollisionBakeResults runs later in this same Update. Assigning
        // sharedMesh there cooks deterministically and is bounded by the render
        // upload cap that produced this preparation (four by default).
        completedBakes.Enqueue(new CollisionBakeResult(
            session,
            requestId,
            position,
            preparation,
            true));
    }

    private void ProcessCollisionBakeResults()
    {
        bool activatedAny = false;
        bool processedAny = false;
        while (completedBakes.TryDequeue(out CollisionBakeResult result))
        {
            processedAny = true;
            if (result.session != worldSession ||
                !pendingCollisionBakes.TryGetValue(result.position, out PendingCollisionBake pending) ||
                pending.session != result.session || pending.requestId != result.requestId ||
                pending.preparation.buildId != result.preparation.buildId ||
                pending.preparation.leaseId != result.preparation.leaseId)
                continue;

            pendingCollisionBakes.Remove(result.position);
            bool hasRenderer = worldData.chunkDictionary.TryGetValue(
                result.position,
                out ChunkRenderer renderer);
            if (!result.success ||
                (!desiredCollision.Contains(result.position) && !retainedCollision.Contains(result.position)) ||
                !hasRenderer)
            {
                if (hasRenderer)
                    renderer.DiscardCollisionPreparation(result.preparation);
                if (desiredCollision.Contains(result.position))
                    TryScheduleMesh(result.position, true);
                continue;
            }
            bool activated = renderer.TryActivateBakedCollision(
                result.preparation.leaseId,
                result.preparation.buildId,
                result.preparation.contentVersion);
            if (!activated)
                TryScheduleMesh(result.position, true);
            else if (renderer.ChunkData != null &&
                      renderer.ChunkData.ContentVersion != result.preparation.contentVersion)
                TryScheduleMesh(result.position, true);
            else
            {
                activatedAny = true;
                if (desiredLods.TryGetValue(result.position, out ChunkLodLevel desiredLod) &&
                    renderer.ActiveLod != desiredLod)
                    TryScheduleMesh(result.position, true);
            }
        }
        // Newly assigned MeshColliders must be visible to raycasts and the next
        // physics step before old collision coverage can be released.
        if (activatedAny)
            Physics.SyncTransforms();
        if (processedAny)
        {
            CancelObsoleteCollisionBakes();
            PruneUnneededChunksAndData();
        }
    }

    private bool IsCollisionChunkReady(Vector3Int chunkPosition)
    {
        return worldData.chunkDictionary.TryGetValue(chunkPosition, out ChunkRenderer renderer) &&
               (renderer.IsCollisionActive || IsCollisionBuildCurrent(chunkPosition));
    }

    private bool IsActiveCollisionCurrent(Vector3Int position)
    {
        return worldData.chunkDictionary.TryGetValue(position, out ChunkRenderer renderer) &&
               renderer.IsCollisionActive &&
               renderer.ChunkData != null &&
               renderer.ActiveCollisionContentVersion == renderer.ChunkData.ContentVersion;
    }

    private bool IsCollisionBuildCurrent(Vector3Int position)
    {
        return worldData.chunkDictionary.TryGetValue(position, out ChunkRenderer renderer) &&
               renderer.ChunkData != null &&
               renderer.AppliedContentVersion == renderer.ChunkData.ContentVersion &&
               renderer.CollisionResolvedContentVersion == renderer.ChunkData.ContentVersion;
    }

    private void RecordCollisionSafePosition()
    {
        if (streamingTarget == null)
            return;
        // Do not move the recovery point through the air while jumping/falling.
        if (streamingTargetMovement != null && !streamingTargetMovement.IsGrounded &&
            hasCollisionSafePosition)
            return;
        lastCollisionSafePosition = streamingTarget.position;
        hasCollisionSafePosition = true;
    }

    private void EnforceStreamingCollisionSafety()
    {
        if (streamingTarget == null)
            return;
        if (streamingTargetCharacter != null && streamingTargetCharacter.fly)
        {
            collisionMovementBlocked = false;
            return;
        }

        Vector3Int voxel = VoxelMetrics.WorldToVoxelCoord(streamingTarget.position);
        Vector3Int currentChunk = WorldDataHelper.ChunkPositionFromVoxelCoords(this, voxel);
        if (IsCollisionChunkReady(currentChunk))
        {
            if (collisionMovementBlocked && currentChunk == blockedCollisionChunk &&
                !TryRecoverBlockedTargetAboveSurface())
                return;
            collisionMovementBlocked = false;
            RecordCollisionSafePosition();
            return;
        }

        // A missing collider must never turn into a fall. Rebuild its chunk at
        // highest priority and hold the player at the last safe height.
        Vector3Int requestedCollisionChunk = currentChunk;
        if (!collisionMovementBlocked || blockedCollisionChunk != requestedCollisionChunk)
        {
            collisionMovementBlocked = true;
            blockedCollisionChunk = requestedCollisionChunk;
            blockedTargetPosition = streamingTarget.position;
            if (hasCollisionSafePosition)
                blockedTargetPosition.y = lastCollisionSafePosition.y;
            if (streamCenterChunk != blockedCollisionChunk)
            {
                streamCenterChunk = blockedCollisionChunk;
                streamCenterVoxel = VoxelMetrics.WorldToVoxelCoord(blockedTargetPosition);
                RefreshDesiredSets();
            }
        }
        TryScheduleMesh(requestedCollisionChunk, false);
        if (!hasCollisionSafePosition)
            return;

        if (streamingTargetBody != null)
        {
            streamingTargetBody.position = blockedTargetPosition;
            streamingTargetBody.linearVelocity = Vector3.zero;
            streamingTargetBody.angularVelocity = Vector3.zero;
        }
        streamingTarget.position = blockedTargetPosition;
        Physics.SyncTransforms();
    }

    private bool TryRecoverBlockedTargetAboveSurface()
    {
        if (streamingTarget == null ||
            !worldData.chunkDictionary.TryGetValue(blockedCollisionChunk, out ChunkRenderer expected) ||
            !expected.IsCollisionActive)
            return false;

        float scale = VoxelMetrics.Size;
        Vector3 origin = new Vector3(
            blockedTargetPosition.x,
            (blockedCollisionChunk.y + chunkHeight + 4) * scale,
            blockedTargetPosition.z);
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            CollisionRecoveryHits,
            (chunkHeight + 8) * scale,
            ~0,
            QueryTriggerInteraction.Ignore);
        float highestSurface = float.NegativeInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = CollisionRecoveryHits[i];
            if (hit.collider == null || hit.collider.GetComponent<ChunkRenderer>() != expected)
                continue;
            highestSurface = Mathf.Max(highestSurface, hit.point.y);
        }
        if (!float.IsFinite(highestSurface))
            return false;

        Collider playerCollider = streamingTarget.GetComponent<Collider>();
        float feetOffset = playerCollider != null
            ? Mathf.Max(0.05f, streamingTarget.position.y - playerCollider.bounds.min.y)
            : scale;
        Vector3 recovered = blockedTargetPosition;
        recovered.y = highestSurface + feetOffset + 0.02f;
        blockedTargetPosition = recovered;
        if (streamingTargetBody != null)
        {
            streamingTargetBody.position = recovered;
            streamingTargetBody.linearVelocity = Vector3.zero;
            streamingTargetBody.angularVelocity = Vector3.zero;
        }
        streamingTarget.position = recovered;
        lastCollisionSafePosition = recovered;
        hasCollisionSafePosition = true;
        Physics.SyncTransforms();
        return true;
    }

    private void SyncReactivatedColliders()
    {
        if (!collisionTransformsDirty)
            return;
        collisionTransformsDirty = false;
        Physics.SyncTransforms();
    }

    private void UpdateCollisionRetention()
    {
        if (retainedCollision.Count == 0)
            return;
        // Do not release the old 3x3 until every replacement surface chunk has
        // either activated its collider or resolved as empty.
        if (!AreDesiredCollisionChunksReady())
            return;

        var released = new List<Vector3Int>();
        foreach (Vector3Int position in retainedCollision)
        {
            if (desiredCollision.Contains(position))
            {
                released.Add(position);
                continue;
            }
            if (worldData.chunkDictionary.TryGetValue(position, out ChunkRenderer renderer))
                renderer.SetCollisionActive(false);
            released.Add(position);
        }
        foreach (Vector3Int position in released)
            retainedCollision.Remove(position);
        CancelObsoleteCollisionBakes();
        RefreshDesiredLods();
        TryScheduleMeshes();
        PruneUnneededChunksAndData();
    }

    private bool AreDesiredCollisionChunksReady()
    {
        foreach (Vector3Int position in desiredCollision)
        {
            if (!IsCollisionChunkResolved(position))
                return false;
        }
        return true;
    }

    private bool IsCollisionChunkResolved(Vector3Int chunkPosition)
    {
        return IsActiveCollisionCurrent(chunkPosition) || IsCollisionBuildCurrent(chunkPosition);
    }

    private void TryCompleteBootstrap()
    {
        if (IsWorldCreated || !IsActiveCollisionCurrent(bootstrapChunk))
            return;
        IsWorldCreated = true;
        Debug.Log($"Voxel world playable in {(Time.realtimeSinceStartup - streamStartedAt) * 1000f:0} ms. " +
                  $"Distant radius {chunkDrawingRange} continues streaming.");
        OnWorldCreated?.Invoke();
    }

    private void PruneUnneededChunksAndData()
    {
        var chunksToRemove = new List<Vector3Int>();
        foreach (KeyValuePair<Vector3Int, ChunkRenderer> pair in worldData.chunkDictionary)
        {
            if (!desiredRender.Contains(pair.Key) && !retainedCollision.Contains(pair.Key) &&
                !pendingCollisionBakes.ContainsKey(pair.Key))
                chunksToRemove.Add(pair.Key);
        }
        foreach (Vector3Int position in chunksToRemove)
        {
            if (SpecialVoxels != null)
                SpecialVoxels.HideSpecialPrefabsForChunk(position);
            WorldDataHelper.RemoveChunk(this, position);
        }

        var dataToRemove = new List<Vector3Int>();
        foreach (KeyValuePair<Vector3Int, ChunkData> pair in worldData.chunkDataDictionary)
        {
            if (!desiredData.Contains(pair.Key) && !pair.Value.modifiedByThePlayer &&
                !worldData.chunkDictionary.ContainsKey(pair.Key))
                dataToRemove.Add(pair.Key);
        }
        foreach (Vector3Int position in dataToRemove)
            worldData.chunkDataDictionary.Remove(position);

    }

    private void CancelOutOfRangeWork()
    {
        var staleData = new List<Vector3Int>();
        foreach (KeyValuePair<Vector3Int, int> pair in pendingData)
        {
            if (!desiredData.Contains(pair.Key))
            {
                CancelWork(pair.Value);
                staleData.Add(pair.Key);
            }
        }
        foreach (Vector3Int position in staleData)
            pendingData.Remove(position);

        var staleMeshes = new List<Vector3Int>();
        foreach (KeyValuePair<Vector3Int, MeshRequestTicket> pair in pendingMeshes)
        {
            if (!desiredRender.Contains(pair.Key) ||
                pair.Value.collision != desiredCollision.Contains(pair.Key))
            {
                CancelWork(pair.Value.requestId);
                staleMeshes.Add(pair.Key);
            }
        }
        foreach (Vector3Int position in staleMeshes)
            pendingMeshes.Remove(position);

        for (int i = readyMeshes.Count - 1; i >= 0; i--)
        {
            if (desiredRender.Contains(readyMeshes[i].position) &&
                readyMeshes[i].ticket.collision ==
                desiredCollision.Contains(readyMeshes[i].position))
                continue;
            cancelledWork.TryRemove(readyMeshes[i].ticket.requestId, out _);
            readyMeshes.RemoveAt(i);
        }
        CancelObsoleteCollisionBakes();
    }

    private void CancelObsoleteCollisionBakes()
    {
        if (pendingCollisionBakes.Count == 0)
            return;

        var cancelled = new List<Vector3Int>();
        foreach (KeyValuePair<Vector3Int, PendingCollisionBake> pair in pendingCollisionBakes)
        {
            if (desiredCollision.Contains(pair.Key) || retainedCollision.Contains(pair.Key))
                continue;

            if (worldData.chunkDictionary.TryGetValue(pair.Key, out ChunkRenderer renderer))
                renderer.DiscardCollisionPreparation(pair.Value.preparation);
            cancelled.Add(pair.Key);
        }
        foreach (Vector3Int position in cancelled)
            pendingCollisionBakes.Remove(position);
    }

    private void CancelWork(int requestId)
    {
        if (requestId != 0 && (workScheduler == null || !workScheduler.CancelQueued(requestId)))
            cancelledWork.TryAdd(requestId, 0);
    }

    private bool ConsumeCancellation(int requestId)
    {
        return cancelledWork.TryRemove(requestId, out _);
    }

    private int WorkPriority(Vector3Int position, bool collision, int phase)
    {
        int dx = Mathf.Abs(position.x - streamCenterChunk.x) / Mathf.Max(1, chunkSize);
        int dz = Mathf.Abs(position.z - streamCenterChunk.z) / Mathf.Max(1, chunkSize);
        int dy = Mathf.Abs(position.y - streamCenterChunk.y) / Mathf.Max(1, chunkHeight);
        int ring = Mathf.Max(dx, dz);
        return (collision ? -10000 : 0) + ring * 100 + dy * 10 + phase;
    }

    private void ProcessWorkerErrors()
    {
        while (workerErrors.TryDequeue(out Exception exception))
            Debug.LogException(exception);
    }

    private void RequestChunkRemesh(ChunkData data, bool includeEdgeNeighbours)
    {
        if (data == null)
            return;
        TryScheduleMesh(data.worldPosition, true);
        if (!includeEdgeNeighbours)
            return;
        Vector3Int[] offsets =
        {
            Vector3Int.left * chunkSize,
            Vector3Int.right * chunkSize,
            Vector3Int.back * chunkSize,
            Vector3Int.forward * chunkSize,
            Vector3Int.down * chunkHeight,
            Vector3Int.up * chunkHeight
        };
        foreach (Vector3Int offset in offsets)
        {
            TryScheduleMesh(data.worldPosition + offset, true);
        }
    }

    public void CopyActiveCollisionChunkKeys(List<Vector3Int> destination)
    {
        destination.Clear();
        foreach (KeyValuePair<Vector3Int, ChunkRenderer> pair in worldData.chunkDictionary)
        {
            if (IsActiveCollisionCurrent(pair.Key))
                destination.Add(pair.Key);
        }
    }
}
