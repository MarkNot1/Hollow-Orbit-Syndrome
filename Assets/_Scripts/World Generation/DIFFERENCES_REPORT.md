# Differences Report: OLD_ Files vs Current Files

## 1. OLD_BlockDataManager.cs → VoxelDataManager.cs

### Class Name Change
- `BlockDataManager` → `VoxelDataManager`

### Dictionary Name Change
- `blockTextureDataDictionary` → `voxelTextureDataDictionary`
- Type: `Dictionary<BlockType, TextureData>` → `Dictionary<VoxelType, TextureData>`

### Property Type Change
- `public BlockDataSO textureData;` → `public VoxelDataSO textureData;`

### Dictionary Key Access Change
- `item.blockType` → `item.voxelType`

### Removed Imports
- Removed: `using System.Collections;`

### Code Formatting
- Added extra blank line after class declaration
- Added extra blank line after dictionary declaration
- Added extra blank line before closing brace in foreach loop

---

## 2. OLD_BlockDataSO.cs → VoxelDataSO.cs

### Class Name Change
- `BlockDataSO` → `VoxelDataSO`

### CreateAssetMenu Attribute Change
- `[CreateAssetMenu(fileName ="Block Data" ,menuName ="Data/Block Data")]`
- → `[CreateAssetMenu(fileName = "VoxelData", menuName = "ScriptableObjects/VoxelData")]`

### TextureData Class Property Change
- `public BlockType blockType;` → `public VoxelType voxelType;`

### Import Order Change
- Imports reordered (UnityEngine, System.Collections.Generic, System.Collections, System)

### Code Formatting
- Added extra blank line after class declaration
- Added extra blank line before TextureData class

---

## 3. OLD_BlockHelper.cs → VoxelHelper.cs

### Class Name Change
- `BlockHelper` → `VoxelHelper`

### Direction Array Order Change
- OLD: `backwards, down, foreward, left, right, up`
- NEW: `up, down, forward, backwards, right, left`

### Method Parameter Type Change
- `BlockType blockType` → `VoxelType voxelType` (throughout)

### Variable Name Changes
- `neighbourBlockCoordinates` → `neighborVoxelCoordinates`
- `neighbourBlockType` → `neighborVoxelType`
- `GetBlockFromChunkCoordinates` → `GetVoxelFromChunkCoordinates`

### Type References
- `BlockType` → `VoxelType` (all occurrences)
- `BlockDataManager` → `VoxelDataManager` (all occurrences)
- `blockTextureDataDictionary` → `voxelTextureDataDictionary`

### Direction Name Fix
- `Direction.foreward` → `Direction.forward` (typo fix)

### Method Name Changes
- `GetBlockFromChunkCoordinates` → `GetVoxelFromChunkCoordinates`

### UV Property Name Change
- `meshData.uv.AddRange(...)` → `meshData.uvs.AddRange(...)`
- Note: Variable renamed from `uv` to `uvs` in MeshData class

### Variable Name Typo
- `tilePOs` (typo in new version, should be `tilePos`)

### Code Formatting
- Removed comment about vertex order in GetFaceVertices
- Added extra blank line before switch statement
- Removed default case comment

---

## 4. OLD_BlockType.cs → VoxelType.cs

### Enum Name Change
- `BlockType` → `VoxelType`

### Enum Values Completely Changed
**OLD BlockType values:**
- Nothing, Air, Grass_Dirt, Dirt, Grass_Stone, Stone, TreeTrunk, TreeLeafesTransparent, TreeLeafsSolid, Water, Sand

**NEW VoxelType values:**
- Nothing, Air, Ground, Wall, Water, Sand, Grass, Rock, Infected_Growth, Ship_Metal

### Removed Imports
- Removed: `using System.Collections;` and `using System.Collections.Generic;`

---

## 5. OLD_Chunk.cs → Chunk.cs

### Method Name Changes
- `LoopThroughTheBlocks` → `LoopThroughTheVoxels`
- `GetPostitionFromIndex` → `GetPositionFromIndex` (typo fix)
- `GetBlockFromChunkCoordinates` → `GetVoxelFromChunkCoordinates` (2 overloads)
- `SetBlock` → `SetVoxel`
- `GetBlockInChunkCoordinates` → `GetVoxelInChunkCoordinates`
- `ChunkPositionFromBlockCoords` → `ChunkPositionFromVoxelCoords`

### Type Changes
- `BlockType` → `VoxelType` (all occurrences)
- `chunkData.blocks` → `chunkData.voxels`

### Method Parameter Changes
- `GetVoxelInChunkCoordinates`: Parameter changed from `Vector3Int pos` to `Vector3 pos`
- Added `Mathf.FloorToInt()` calls in `GetVoxelInChunkCoordinates`

### Helper Class Reference
- `BlockHelper.GetMeshData` → `VoxelHelper.GetMeshData`

### Added Import
- Added: `using Unity.Mathematics;`

### Code Formatting
- Improved formatting in `GetVoxelFromChunkCoordinates` (multi-line condition)
- Improved formatting in `SetVoxel` (multi-line condition)
- Improved formatting in `GetIndexFromPosition` (added parentheses for clarity)
- Improved formatting in `GetVoxelInChunkCoordinates` (indentation)

---

## 6. OLD_ChunkData.cs → ChunkData.cs

### Property Name Change
- `public BlockType[] blocks;` → `public VoxelType[] voxels;`

### Default Chunk Size Change
- `chunkSize = 16` → `chunkSize = 32`

### Constructor Changes
- Array initialization: `blocks = new BlockType[...]` → `voxels = new VoxelType[...]`
- Constructor parameter order: `chunkHeight` and `chunkSize` swapped in assignment order

### Removed Imports
- Removed: `using System.Collections;` and `using System.Collections.Generic;`

### Code Formatting
- Added extra blank line after `modifiedByThePlayer` property

---

## 7. OLD_ChunkRenderer.cs → ChunkRenderer.cs

### Property Name Change (Casing)
- `public ChunkData ChunkData { get; private set; }` → `public ChunkData chunkData { get; private set; }` (camelCase)

### Property Name Change
- `public bool ModifiedByThePlayer` → `public bool modifiedByThePlayer` (camelCase)

### Method Name Change
- `InitializeChunk` → `Initialize`

### UV Property Name Change
- `meshData.uv` → `meshData.uvs`
- `meshData.waterMesh.uv` → `meshData.waterMesh.uvs`

### Code Formatting
- Added extra blank lines in various places
- Improved indentation in `OnDrawGizmos` method
- Removed extra blank line before `#if UNITY_EDITOR`

### Import Order
- Imports reordered slightly

---

## 8. OLD_Direction.cs → Direction.cs

### Enum Value Name Fix
- `foreward` → `forward` (typo fix)

### Added Import
- Added: `using UnityEngine;`

### Comment Formatting
- Comments reformatted with better spacing and alignment
- Comments now include direction indicators (z positive, x positive, etc.)

---

## 9. OLD_DirectionExtensions.cs → DirectionExtensions.cs

### Direction Name Fix
- `Direction.foreward` → `Direction.forward`

### Switch Case Order Change
- Cases reordered: `up, down, forward, backwards, right, left` (alphabetical/logical order)

### Code Formatting
- Added extra blank line at end of file

---

## 10. OLD_MeshData.cs → MeshData.cs

### Property Name Change
- `public List<Vector2> uv = new List<Vector2>();` → `public List<Vector2> uvs = new List<Vector2>();`

### Removed Imports
- Removed: `using System.Collections;` and `using System.Collections.Generic;`

### Code Formatting
- Added extra blank line after `isMainMesh` field
- Removed trailing blank line in `AddVertex` method
- Improved formatting in `AddQuadTriangles` (added blank line before if statement)

### Import Order
- Imports reordered

---

## 11. OLD_World.cs → World.cs

### Default Values Changed
- `mapSizeInChunks = 6` → `mapSizeInChunks = 12`
- `chunkSize = 16` → `chunkSize = 32`

### Method Name Changes
- `GetBlockFromChunkCoordinates` → `GetVoxelFromChunkCoordinates`
- `ChunkPositionFromBlockCoords` → `ChunkPositionFromVoxelCoords`
- `GetBlockInChunkCoordinates` → `GetVoxelInChunkCoordinates`
- `SetBlock` → `SetVoxel`

### Type Changes
- `BlockType` → `VoxelType` (all occurrences)

### GenerateWorld Method Changes
- Loop variable changed: `z` → `y` in outer loop
- Variable name: `chunkObject` → `ChunkObject` (PascalCase)
- Method call: `InitializeChunk` → `Initialize`

### GenerateVoxels Method Changes
- Variable names: `voxelType` used instead of mixing `voxelType` and `BlockType`
- Default voxel type: `BlockType.Dirt` → `VoxelType.Infected_Growth`
- Ground voxel type: `BlockType.Grass_Dirt` → `VoxelType.Infected_Growth`
- Added separate `worldX` and `worldZ` variables for clarity
- Noise calculation: moved to use `worldX` and `worldZ` variables

### GetVoxelFromChunkCoordinates Method Changes
- Parameter type: `GetVoxelInChunkCoordinates` now takes `Vector3` instead of `Vector3Int`
- Return type: `BlockType` → `VoxelType`
- Method names updated throughout

### Removed Imports
- Removed: `using System.Collections;`

### Code Formatting
- Improved indentation and spacing throughout
- Added blank lines for better readability

---

## Summary of Major Changes

1. **Naming Convention**: All `Block*` → `Voxel*` (BlockType → VoxelType, BlockHelper → VoxelHelper, etc.)
2. **Typo Fixes**: `foreward` → `forward`, `GetPostitionFromIndex` → `GetPositionFromIndex`
3. **Property Naming**: `uv` → `uvs`, `blocks` → `voxels`
4. **Chunk Size**: Default changed from 16 to 32
5. **Map Size**: Default changed from 6 to 12 chunks
6. **Voxel Types**: Completely different enum values (terrain-focused → game-specific)
7. **Code Style**: Improved formatting, spacing, and organization throughout
8. **Method Signatures**: Some methods now use `Vector3` instead of `Vector3Int` where appropriate

