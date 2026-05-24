using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// Manages slice data extraction from VelocityLoader
/// Caches and provides slice data for rendering
/// </summary>
public class SliceDataManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("VelocityLoader to extract data from")]
    public VelocityLoader velocityLoader;
    
    [Header("Slice Settings")]
    public SliceController.SliceAxis currentAxis = SliceController.SliceAxis.X_Axis;
    
    [Range(0, 1)]
    public float slicePosition = 0.5f;
    
    // Cached data
    private List<SliceDataPoint> cachedSliceData = new List<SliceDataPoint>();
    private float cachedSliceCoord = 0f;
    private SliceController.SliceAxis cachedAxis = SliceController.SliceAxis.None;
    private float cachedPosition = -1f;
    private int cachedFrameIndex = -1;

    // Job System Cache
    private NativeArray<Vector3> nativePositions;
    private int nativePositionsFrameIndex = -1;
    private string nativePositionsVesselPath = "";

    // Async Job State
    private JobHandle activeJobHandle;
    private bool isJobActive = false;
    private NativeArray<int> activeResults; // Size of positions, stores index if match
    private int activeMaxPoints = 10000;
    
    private Vector3 lastPlaneNormal;
    private Vector3 lastPlanePoint;
    private float lastThickness;
    private int lastFrameIdx = -1;
    
    /// <summary>
    /// Represents a single data point in a slice
    /// </summary>
    public struct SliceDataPoint
    {
        public Vector3 position;
        public Vector3 velocity;
        public float magnitude;
        public Color color;
        
        public SliceDataPoint(Vector3 pos, Vector3 vel, float mag, Color col)
        {
            position = pos;
            velocity = vel;
            magnitude = mag;
            color = col;
        }
    }
    
    void Start()
    {
        if (velocityLoader == null)
        {
            velocityLoader = FindObjectOfType<VelocityLoader>();
        }
    }
    
    /// <summary>
    /// Gets slice data for the current axis and position
    /// Returns cached data if parameters haven't changed
    /// </summary>
    public List<SliceDataPoint> GetSliceData(int maxPoints = 10000)
    {
        if (velocityLoader == null || velocityLoader.loadedFrames.Count == 0)
        {
            return new List<SliceDataPoint>();
        }
        
        // Debug: Axis Slice Entry
        // Debug.Log($"<color=white>[SliceDataManager] GetSliceData called. Axis: {currentAxis}, Pos: {slicePosition}</color>");

        // Check if we can use cached data
        bool needsUpdate = currentAxis != cachedAxis ||
                          Mathf.Abs(slicePosition - cachedPosition) > 0.001f ||
                          velocityLoader.currentFrameIndex != cachedFrameIndex;
        
        if (!needsUpdate && cachedSliceData.Count > 0)
        {
            return cachedSliceData;
        }
        
        // Extract new slice data
        cachedSliceData = ExtractSliceData(maxPoints);
        cachedAxis = currentAxis;
        cachedPosition = slicePosition;
        cachedFrameIndex = velocityLoader.currentFrameIndex;
        
        return cachedSliceData;
    }
    
    /// <summary>
    /// Extracts slice data from the current frame
    /// </summary>
    List<SliceDataPoint> ExtractSliceData(int maxPoints = 10000)
    {
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        
        if (currentAxis == SliceController.SliceAxis.None)
        {
            return new List<SliceDataPoint>();
        }
        
        var frame = velocityLoader.loadedFrames[velocityLoader.currentFrameIndex];
        
        // Update NativeArray cache if frame changed 
        UpdateNativePositions(frame);
        
        if (!nativePositions.IsCreated || nativePositions.Length == 0)
        {
             return new List<SliceDataPoint>();
        }

        // Get unique positions along the slice axis
        HashSet<float> uniquePositions = new HashSet<float>();
        for (int i = 0; i < frame.positions.Count; i++)
        {
            Vector3 pos = frame.positions[i];
            if (currentAxis == SliceController.SliceAxis.X_Axis)
                uniquePositions.Add(pos.x);
            else if (currentAxis == SliceController.SliceAxis.Y_Axis)
                uniquePositions.Add(pos.y);
        }
        
        var sortedPositions = uniquePositions.OrderBy(p => p).ToList();
        if (sortedPositions.Count == 0) return new List<SliceDataPoint>();
        
        // Find target position
        int targetIndex = Mathf.FloorToInt(slicePosition * (sortedPositions.Count - 1));
        targetIndex = Mathf.Clamp(targetIndex, 0, sortedPositions.Count - 1);
        float targetPosition = sortedPositions[targetIndex];
        cachedSliceCoord = targetPosition;
        
        // Define Plane Normal for the axis
        Vector3 planeNormal = Vector3.right; 
        if (currentAxis == SliceController.SliceAxis.Y_Axis) planeNormal = Vector3.up;
        
        Vector3 planePoint = planeNormal * targetPosition;

        // Use Job System for point collection
        NativeArray<int> results = new NativeArray<int>(nativePositions.Length, Allocator.TempJob);
        
        SliceFilterJob job = new SliceFilterJob
        {
            positions = nativePositions,
            planeNormal = planeNormal,
            planePoint = planePoint,
            halfThickness = 0.001f, 
            results = results
        };

        JobHandle handle = job.Schedule(nativePositions.Length, 64);
        handle.Complete();

        // 1. Count matches and identify indices
        List<int> matches = new List<int>();
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i] != -1) matches.Add(results[i]);
        }

        int matchedCount = matches.Count;
        int extractStep = Mathf.Max(1, Mathf.CeilToInt((float)matchedCount / maxPoints));

        // 2. Extract result with downsampling
        var sliceData = new List<SliceDataPoint>(Mathf.Min(matchedCount, maxPoints + 1));
        for (int i = 0; i < matchedCount; i += extractStep)
        {
            int idx = matches[i];
            sliceData.Add(new SliceDataPoint(
                frame.positions[idx],
                frame.velocities[idx],
                frame.velocityMagnitudes[idx],
                frame.colors[idx]
            ));
        }

        results.Dispose();

        sw.Stop();
        Debug.Log($"<color=magenta>[SliceDataManager] ExtractSliceData (JOBS): {sw.Elapsed.TotalMilliseconds:F2}ms ({sliceData.Count}/{frame.positions.Count} points)</color>");
        
        return sliceData;
    }
    
    /// <summary>
    /// Gets the current slice coordinate in world space
    /// </summary>
    public float GetSliceCoordinate()
    {
        return cachedSliceCoord;
    }
    
    /// <summary>
    /// Gets the data bounds for the current frame
    /// </summary>
    public Bounds GetDataBounds()
    {
        if (velocityLoader == null || velocityLoader.loadedFrames.Count == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one * 0.2f);
        }
        
        var frame = velocityLoader.loadedFrames[velocityLoader.currentFrameIndex];
        
        if (frame.positions.Count == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one * 0.2f);
        }
        
        float minX = frame.positions.Min(p => p.x);
        float maxX = frame.positions.Max(p => p.x);
        float minY = frame.positions.Min(p => p.y);
        float maxY = frame.positions.Max(p => p.y);
        float minZ = frame.positions.Min(p => p.z);
        float maxZ = frame.positions.Max(p => p.z);
        
        Vector3 center = new Vector3(
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f,
            (minZ + maxZ) * 0.5f
        );
        
        Vector3 size = new Vector3(
            maxX - minX,
            maxY - minY,
            maxZ - minZ
        );
        
        return new Bounds(center, size);
    }
    
    /// <summary>
    /// Forces a refresh of cached data
    /// </summary>
    public void ForceRefresh()
    {
        cachedFrameIndex = -1;
        nativePositionsFrameIndex = -1;
    }

    private void OnDestroy()
    {
        if (isJobActive)
        {
            activeJobHandle.Complete();
            if (activeResults.IsCreated) activeResults.Dispose();
        }
        if (nativePositions.IsCreated) nativePositions.Dispose();
    }

    [BurstCompile]
    struct SliceFilterJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> positions;
        public float3 planeNormal;
        public float3 planePoint;
        public float halfThickness;
        
        public NativeArray<int> results;

        public void Execute(int index)
        {
            float3 pos = positions[index];
            float distance = math.dot(pos - planePoint, planeNormal);
            if (math.abs(distance) < halfThickness)
            {
                results[index] = index;
            }
            else
            {
                results[index] = -1;
            }
        }
    }

    /// <summary>
    /// Get slice data using arbitrary plane (for SlicePlaneController)
    /// SIMPLIFIED: Direct synchronous calculation every frame (no Job system)
    /// </summary>
    public List<SliceDataPoint> GetSliceDataByPlane(Vector3 planeNormal, Vector3 planePoint, float thickness = 0.005f, int maxPoints = 10000)
    {
        if (velocityLoader == null || !velocityLoader.IsDataLoaded || velocityLoader.loadedFrames.Count == 0)
        {
            return new List<SliceDataPoint>();
        }

        var frame = velocityLoader.loadedFrames[velocityLoader.currentFrameIndex];
        
        // Direct calculation (no async Job)
        float halfThickness = thickness * 0.5f;
        planeNormal = planeNormal.normalized;
        
        List<int> matchedIndices = new List<int>();
        
        // Simple loop to find points within thickness of plane
        for (int i = 0; i < frame.positions.Count; i++)
        {
            Vector3 pos = frame.positions[i];
            float distance = Mathf.Abs(Vector3.Dot(pos - planePoint, planeNormal));
            
            if (distance < halfThickness)
            {
                matchedIndices.Add(i);
            }
        }
        
        // Downsample if too many points
        int matchedCount = matchedIndices.Count;
        int extractStep = Mathf.Max(1, Mathf.CeilToInt((float)matchedCount / maxPoints));
        
        List<SliceDataPoint> result = new List<SliceDataPoint>(Mathf.Min(matchedCount, maxPoints));
        for (int i = 0; i < matchedCount; i += extractStep)
        {
            int idx = matchedIndices[i];
            result.Add(new SliceDataPoint(
                frame.positions[idx],
                frame.velocities[idx],
                frame.velocityMagnitudes[idx],
                frame.colors[idx]
            ));
        }
        
        return result;
    }

    private void UpdateNativePositions(VelocityData frame)
    {
        string currentPath = velocityLoader.GetCurrentDataFolderPath();
        if (nativePositionsFrameIndex != velocityLoader.currentFrameIndex || nativePositionsVesselPath != currentPath)
        {
            // IMPORTANT: If a job is active, we MUST complete it before deallocating the source data
            if (isJobActive) activeJobHandle.Complete();

            if (nativePositions.IsCreated) nativePositions.Dispose();
            
            nativePositions = new NativeArray<Vector3>(frame.positions.Count, Allocator.Persistent);
            nativePositions.CopyFrom(frame.positions.ToArray());
            
            nativePositionsFrameIndex = velocityLoader.currentFrameIndex;
            nativePositionsVesselPath = currentPath;
        }
    }
}
