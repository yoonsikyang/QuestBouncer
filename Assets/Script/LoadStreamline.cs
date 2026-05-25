using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using System.Globalization;
using Photon.Pun;
using System.Threading.Tasks;
using System.Threading.Tasks;

public class LoadStreamline : MonoBehaviour, IPunObservable
{
    public string rootFolder = "data1";
    public string subFolder = "streamline";
    public string filePrefix = "Streamline_";
    public float animationInterval = 0.1f;
    public Material lineMaterial;
    public float lineWidth = 0.002f;

    [Header("visionOS RealityKit Budget")]
    [Tooltip("Caps generated streamline frame GameObjects on visionOS RealityKit to avoid PolySpatial SynchronizationComponent limits.")]
    public int visionOSMaxFramesToBuild = 1;
    public int visionOSMaxLinesPerFrame = 400;
    public int visionOSMaxPointsPerLine = 64;

    [Header("Coordinate Mapping")]
    public bool autoDetectBounds = true;
    public Vector3 dataMin;
    public Vector3 dataMax;
    public Vector3 targetMin = new Vector3(-0.5f, 0, -0.5f);
    public Vector3 targetMax = new Vector3(0.5f, 1f, 0.5f);
    public bool fitToMeshBounds = true; // match vessel bounds like velocity arrows
    public bool keepAspectRatio = true;
    public Vector3 userScale = Vector3.one;

    public bool isActivated = true;
    public bool IsDataLoaded { get; private set; } = false;
    public Texture inactiveTexture;

    public GameObject displayObject; // parent for all frames

    private List<List<List<Vector3>>> parsedFrames = new List<List<List<Vector3>>>(); // original lines per frame
    private List<GameObject> frames = new List<GameObject>(); // each frame holds line renderers
    public bool isAnimating = false;
    private Coroutine animationCoroutine;
    private int currentFrameIndex = 0;
    public int CurrentFrameIndex => currentFrameIndex; // Public getter for network sync
    private Coroutine rebuildCoroutine;

    // Frame Control 지원
    public int TotalFrameCount => frames.Count;

    /// <summary>
    /// 애니메이션 일시정지 (프레임 컨트롤 모드용)
    /// </summary>
    public void PauseAnimation()
    {
        if (isAnimating)
        {
            isAnimating = false;
            if (animationCoroutine != null) StopCoroutine(animationCoroutine);
            Debug.Log("<color=yellow>[LoadStreamline] Animation paused</color>");
        }
    }

    /// <summary>
    /// 애니메이션 재개
    /// </summary>
    public void ResumeAnimation()
    {
        if (!isAnimating && isActivated && frames.Count > 0)
        {
            isAnimating = true;
            if (animationCoroutine != null) StopCoroutine(animationCoroutine);
            animationCoroutine = StartCoroutine(PlayAnimation());
            Debug.Log("<color=green>[LoadStreamline] Animation resumed</color>");
        }
    }

    private Vector3 prevUserScale = Vector3.one;
    private bool prevKeepAspectRatio = true;
    private Vector3 prevTargetMin;
    private Vector3 prevTargetMax;

    private Texture originalTexture;
    private string currentFolderPath;
    private bool lastActivatedState = true;
    private float cachedLineWidth = -1f;

    private string FullFolderPath => Path.Combine(rootFolder, subFolder);

    private static bool IsVisionOSRealityKitRuntime
    {
        get
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    void Start()
    {
        // Manager handles initialization usually.
        if (Manager.Instance == null)
        {
            StartCoroutine(initialization());
        }
    }

    public IEnumerator initialization()
    {
        Debug.Log("Streamline Start()");
        
        if (Manager.Instance != null)
        {
            rootFolder = Manager.Instance.currentDataFolder;
        }
        
        currentFolderPath = FullFolderPath;
        EnsureValidPath();
        lastActivatedState = isActivated;

        // Find or create the parent object under MixedRealitySceneContent/Object Parent
        GameObject root = GameObject.Find("MixedRealitySceneContent");
        if (root != null)
        {
            Transform parent = root.transform.Find("Object Parent/Streamline");
            if (parent == null)
            {
                GameObject go = new GameObject("Streamline");
                Transform objParent = root.transform.Find("Object Parent");
                if (objParent != null)
                {
                    go.transform.SetParent(objParent, false);
                }
                parent = go.transform;
            }
            displayObject = parent.gameObject;
        }
        else
        {
            Debug.LogError("Could not find 'MixedRealitySceneContent' in scene");
        }

        // Apply initial state
        UpdateActivationState();
        
        // Fix: Apply standard offset so data is centered in ObjectParent
        if (Manager.Instance != null)
        {
             displayObject.transform.localPosition = Manager.Instance.CurrentDataOffset;
             //displayObject.transform.localRotation = Manager.Instance.CurrentDataRotation;
             displayObject.transform.localScale = Vector3.one * Manager.Instance.CurrentDataScale;
        }

        StartCoroutine(LoadStreamlineFiles());

        prevUserScale = userScale;
        prevKeepAspectRatio = keepAspectRatio;
        prevTargetMin = targetMin;
        prevTargetMax = targetMax;
        yield return null;
    }

    void Update()
    {
        // Check for path change
        // string newPath = FullFolderPath;
        // if (currentFolderPath != newPath)
        // {
        //     currentFolderPath = newPath;
        //     StartCoroutine(ReloadStreamlineSequence());
        // }

        if (isActivated != lastActivatedState)
        {
            lastActivatedState = isActivated;
            UpdateActivationState();
        }

        // If re-activated with existing frames, ensure playback resumes
        // Don't auto-restart animation in frame control mode
        var buttonController = FindObjectOfType<ButtonControllerManager>();
        bool isFrameControlMode = buttonController != null && buttonController.isFrameControlMode;
        if (isActivated && frames.Count > 0 && !isAnimating && !isFrameControlMode)
        {
            currentFrameIndex %= Mathf.Max(1, frames.Count);
            ShowFrame(currentFrameIndex);
            isAnimating = true;
            if (animationCoroutine != null) StopCoroutine(animationCoroutine);
            animationCoroutine = StartCoroutine(PlayAnimation());
        }

        // Respond to live changes in scaling/aspect/targets
        if (userScale != prevUserScale || keepAspectRatio != prevKeepAspectRatio || targetMin != prevTargetMin || targetMax != prevTargetMax)
        {
            prevUserScale = userScale;
            prevKeepAspectRatio = keepAspectRatio;
            prevTargetMin = targetMin;
            prevTargetMax = targetMax;
            prevTargetMin = targetMin;
            prevTargetMax = targetMax;
            
            if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
            rebuildCoroutine = StartCoroutine(RebuildFramesFromParsedAsync());
        }
    }

    IEnumerator ReloadStreamlineSequence()
    {
        yield return null; // Wait for frame
        EnsureValidPath();
        IsDataLoaded = false;
        if (animationCoroutine != null) StopCoroutine(animationCoroutine);
        
        // Use coroutine to clear frames to prevent freeze
        yield return StartCoroutine(ClearFramesAsyncAndLoad());
    }

    IEnumerator ClearFramesAsyncAndLoad()
    {
        yield return StartCoroutine(ClearFramesAsync());
        StartCoroutine(LoadStreamlineFiles());
    }

    IEnumerator ClearFramesAsync()
    {
        Debug.Log("Starting ClearFramesAsync...");
        int destroyPerFrame = 50; // Destroy 50 objects per frame
        int count = 0;

        foreach (var frame in frames)
        {
            if (frame != null) Destroy(frame);
            count++;
            if (count >= destroyPerFrame)
            {
                count = 0;
                yield return null;
            }
        }
        frames.Clear();
        currentFrameIndex = 0;
        yield return null; // Wait one more frame to let GC catch up potentially
        Debug.Log("Finished ClearFramesAsync.");
    }

    void EnsureValidPath()
    {
        string candidate = FullFolderPath;
        string candidateFull = Path.Combine(Application.streamingAssetsPath, candidate);

        if (Directory.Exists(candidateFull))
        {
            currentFolderPath = candidate;
            return;
        }

        string fallback = subFolder;
        string fallbackFull = Path.Combine(Application.streamingAssetsPath, fallback);
        if (Directory.Exists(fallbackFull))
        {
            currentFolderPath = fallback;
            return;
        }

        Debug.LogError($"Streamline data folder not found. Checked: {candidateFull} and {fallbackFull}");
    }

    void UpdateActivationState()
    {
        if (displayObject != null)
        {
            displayObject.SetActive(isActivated);
        }

        if (!isActivated && isAnimating)
        {
            isAnimating = false;
            if (animationCoroutine != null) StopCoroutine(animationCoroutine);
        }
    }

    IEnumerator LoadStreamlineFiles()
    {
        string dirPath = Path.Combine(Application.streamingAssetsPath, currentFolderPath);
        if (!Directory.Exists(dirPath))
        {
            Debug.LogError("Directory not found: " + dirPath);
            yield break;
        }

        string[] filePaths = Directory.GetFiles(dirPath, "streamline_frame_*.bin");
        
        if (filePaths.Length == 0)
        {
             // Fallback or error?
             // Maybe user hasn't run generation yet or using old data?
             // But we are optimizing. Assume binary exists.
             Debug.LogWarning($"No binary streamline files found in {dirPath}. Checking for legacy txt...");
             filePaths = Directory.GetFiles(dirPath, filePrefix + "*.txt");
             if (filePaths.Length > 0)
             {
                 Debug.LogWarning("Found legacy text files. Please run data generation script.");
                 // Could fallback to legacy loader, but for now enforcing binary as per objective.
                 yield break;
             }
             yield break;
        }

        Array.Sort(filePaths, (a, b) => {
            int numA = ExtractNumber(Path.GetFileNameWithoutExtension(a));
            int numB = ExtractNumber(Path.GetFileNameWithoutExtension(b));
            return numA.CompareTo(numB);
        });

        parsedFrames.Clear();

        var parsedArray = new List<List<Vector3>>[filePaths.Length];
        var mins = new Vector3[filePaths.Length];
        var maxs = new Vector3[filePaths.Length];

        var loadTask = Task.Run(() =>
        {
            Parallel.For(0, filePaths.Length, i =>
            {
                var lines = LoadBinaryStreamlineFrame(filePaths[i]);
                parsedArray[i] = lines;

                Vector3 localMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 localMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                if (lines != null)
                {
                    foreach (var line in lines)
                    {
                        foreach (var p in line)
                        {
                            localMin = Vector3.Min(localMin, p);
                            localMax = Vector3.Max(localMax, p);
                        }
                    }
                }

                mins[i] = localMin;
                maxs[i] = localMax;
            });
        });

        yield return new WaitUntil(() => loadTask.IsCompleted);

        if (loadTask.Exception != null)
        {
            Debug.LogError($"Streamline load task failed: {loadTask.Exception.Flatten().Message}");
            yield break;
        }

        Vector3 globalMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 globalMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < parsedArray.Length; i++)
        {
            var lines = parsedArray[i] ?? new List<List<Vector3>>();
            parsedFrames.Add(lines);

            bool hasData = lines.Count > 0 && mins[i].x < float.MaxValue && maxs[i].x > float.MinValue;
            if (hasData)
            {
                globalMin = Vector3.Min(globalMin, mins[i]);
                globalMax = Vector3.Max(globalMax, maxs[i]);
            }
        }

        if (autoDetectBounds && parsedFrames.Count > 0)
        {
            dataMin = globalMin;
            dataMax = globalMax;
        }

        // Use coroutine to rebuild frames
        if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
        rebuildCoroutine = StartCoroutine(RebuildFramesFromParsedAsync());
        yield return rebuildCoroutine; // Wait for frames to be built
        IsDataLoaded = true;
        Debug.Log("Streamline Data Loaded");
    }

    void ClearFrames()
    {
        foreach (var frame in frames)
        {
            if (frame != null) Destroy(frame);
        }
        frames.Clear();
        currentFrameIndex = 0;
    }
    
    // Load Binary Frame
    List<List<Vector3>> LoadBinaryStreamlineFrame(string filePath)
    {
         var result = new List<List<Vector3>>();
         
         try 
         {
             using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
             {
                 int numLines = reader.ReadInt32();
                 
                 // Fix: Python script writes ALL line lengths first, then ALL points.
                 // Read line lengths array
                 int[] lineLengths = new int[numLines];
                 byte[] lengthBytes = reader.ReadBytes(numLines * 4);
                 Buffer.BlockCopy(lengthBytes, 0, lineLengths, 0, lengthBytes.Length);
                 
                 for(int i=0; i<numLines; i++)
                 {
                     int numPoints = lineLengths[i];
                     if (numPoints <= 0) continue;
                     
                     byte[] bytes = reader.ReadBytes(numPoints * 12); // 3 * 4 bytes
                     
                     var linePoints = new List<Vector3>(numPoints);
                     for(int k=0; k<numPoints; k++)
                     {
                         float x = System.BitConverter.ToSingle(bytes, k*12);
                         float y = System.BitConverter.ToSingle(bytes, k*12+4);
                         float z = System.BitConverter.ToSingle(bytes, k*12+8);
                         
                         linePoints.Add(new Vector3(y, -x, z));
                     }
                     
                     if (linePoints.Count > 1)
                     {
                         result.Add(linePoints);
                     }
                 }
             }
         }
         catch (System.Exception e)
         {
             Debug.LogError($"Error loading binary streamline {filePath}: {e.Message}");
         }
         
         return result;
    }

    // ParseStreamlineLines replaced by LoadBinaryStreamlineFrame
    // Removing ParseStreamlineLines...
    
    // Keeping CloneLines etc.


    List<List<Vector3>> CloneLines(List<List<Vector3>> src)
    {
        var dst = new List<List<Vector3>>(src.Count);
        for (int i = 0; i < src.Count; i++)
        {
            var line = src[i];
            var copy = new List<Vector3>(line.Count);
            for (int j = 0; j < line.Count; j++)
            {
                copy.Add(line[j]);
            }
            dst.Add(copy);
        }
        return dst;
    }

    void NormalizeLines(List<List<Vector3>> lines)
    {
        // Fix: Disable normalization if we are relying on Manager's Auto-Scaling
        // The raw data points (y, -x, z) should already align with the vessel mesh
        // Normalization squashes them into a [0,1] box or Target Bounds, breaking the alignment if the aspect ratio or scale differs slightly.
        
        // We just want to use raw points.
        // If linewidth depends on scale, calculate it roughly.
        
        cachedLineWidth = Mathf.Max(0.00001f, lineWidth * Manager.Instance.CurrentDataScale); 

        // Do nothing to 'lines' -> preserve raw coordinates
        // The container 'displayObject' already has Position/Rotation/Scale applied from Manager.
    }

    GameObject BuildFrame(List<List<Vector3>> lines)
    {
        if (lines == null || lines.Count == 0 || displayObject == null) return null;

        GameObject frame = new GameObject($"StreamlineFrame_{frames.Count}");
        frame.transform.SetParent(displayObject.transform, false);

        // Deterministic color per line for visual differentiation
        System.Random rng = new System.Random(frames.Count * 7919 + 17);

        int lineCount = lines.Count;
        int buildLineCount = lineCount;
        if (IsVisionOSRealityKitRuntime)
        {
            buildLineCount = Mathf.Min(lineCount, Mathf.Max(0, visionOSMaxLinesPerFrame));
            if (buildLineCount < lineCount)
            {
                Debug.Log($"[LoadStreamline] visionOS RealityKit line count capped from {lineCount} to {buildLineCount} for frame {frames.Count}.");
            }
        }

        for (int i = 0; i < buildLineCount; i++)
        {
            int sourceIndex = ResolveSourceIndex(i, lineCount, buildLineCount);
            var pts = lines[sourceIndex];
            if (pts.Count < 2) continue;

            GameObject lineObj = new GameObject($"Line_{i}");
            lineObj.transform.SetParent(frame.transform, false);
            var lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            Vector3[] renderPoints = BuildRenderPoints(pts);
            lr.positionCount = renderPoints.Length;
            lr.SetPositions(renderPoints);
            float width = cachedLineWidth > 0f ? cachedLineWidth : lineWidth;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.material = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Unlit/Color"));

            // Assign a stable random color per line
            float h = (float)rng.NextDouble();
            lr.material.color = Color.HSVToRGB(h, 0.9f, 0.9f);
        }

        return frame;
    }

    private Vector3[] BuildRenderPoints(List<Vector3> points)
    {
        if (!IsVisionOSRealityKitRuntime || points.Count <= visionOSMaxPointsPerLine || visionOSMaxPointsPerLine < 2)
        {
            return points.ToArray();
        }

        int pointCount = Mathf.Min(points.Count, visionOSMaxPointsPerLine);
        Vector3[] sampledPoints = new Vector3[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            int sourceIndex = ResolveSourceIndex(i, points.Count, pointCount);
            sampledPoints[i] = points[sourceIndex];
        }

        return sampledPoints;
    }

    // Helper to get extract number
    int ExtractNumber(string filename)
    {
        string numberPart = "";
        for (int i = filename.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(filename[i]))
                numberPart = filename[i] + numberPart;
            else if (numberPart.Length > 0)
                break;
        }
        
        if (int.TryParse(numberPart, out int result))
            return result;
        return 0;
    }

    IEnumerator PlayAnimation()
    {
        while (isAnimating && isActivated)
        {
            if (frames.Count == 0)
            {
                yield return null;
                continue;
            }

            ShowFrame(currentFrameIndex);

            yield return new WaitForSeconds(animationInterval);

            currentFrameIndex++;
            if (currentFrameIndex >= frames.Count)
            {
                currentFrameIndex = 0;
            }
        }
    }

    void ShowFrame(int index)
    {
        if (frames.Count == 0) return;
        if (index < 0 || index >= frames.Count) return;
        
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i] != null)
            {
               bool shouldBeActive = (i == index);
               if (frames[i].activeSelf != shouldBeActive)
                   frames[i].SetActive(shouldBeActive);
            }
        }
    }

    IEnumerator RebuildFramesFromParsedAsync()
    {
        if (parsedFrames.Count == 0) yield break;
        
        yield return StartCoroutine(ClearFramesAsync());

        Debug.Log("Rebuilding Streamline Frames...");
        int framesProcessed = 0;

        int parsedFrameCount = parsedFrames.Count;
        int frameBuildCount = parsedFrameCount;
        if (IsVisionOSRealityKitRuntime)
        {
            frameBuildCount = Mathf.Min(parsedFrameCount, Mathf.Max(1, visionOSMaxFramesToBuild));
            if (frameBuildCount < parsedFrameCount)
            {
                Debug.Log($"[LoadStreamline] visionOS RealityKit frame count capped from {parsedFrameCount} to {frameBuildCount}.");
            }
        }

        for (int frameBuildIndex = 0; frameBuildIndex < frameBuildCount; frameBuildIndex++)
        {
             int sourceFrameIndex = ResolveSourceIndex(frameBuildIndex, parsedFrameCount, frameBuildCount);
             var originalLines = parsedFrames[sourceFrameIndex];

             // 1. Clone
             var lines = CloneLines(originalLines);
             
             // 2. Normalize
             NormalizeLines(lines); // Uses current targetMin/Max/UserScale
             
             // 3. Build
             GameObject frameObj = BuildFrame(lines);
             if (frameObj != null)
             {
                 frameObj.SetActive(false); // Hidden by default
                 frames.Add(frameObj);
             }
             
             framesProcessed++;
             if (framesProcessed % 5 == 0) yield return null;
        }

        // Restore frame if possible
        if (currentFrameIndex >= frames.Count) currentFrameIndex = 0;
        ShowFrame(currentFrameIndex);
        
        Debug.Log("Rebuild Complete");
    }

    private static int ResolveSourceIndex(int poolIndex, int dataCount, int poolCount)
    {
        if (dataCount <= 0 || poolCount <= 0)
        {
            return 0;
        }

        if (poolCount >= dataCount)
        {
            return poolIndex;
        }

        float normalized = (poolIndex + 0.5f) / poolCount;
        return Mathf.Min(dataCount - 1, Mathf.FloorToInt(normalized * dataCount));
    }

    // Frame Control Interface
    public void SetFrameIndex(int index)
    {
        if (frames.Count == 0) return;
        index = Mathf.Clamp(index, 0, frames.Count - 1);
        currentFrameIndex = index;
        ShowFrame(currentFrameIndex);
    }
    
    public void ToggleAnimation()
    {
        if (isAnimating)
            PauseAnimation();
        else
            ResumeAnimation();
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(isActivated);
            stream.SendNext(isAnimating);
            stream.SendNext(rootFolder);
            stream.SendNext(subFolder);
        }
        else
        {
            isActivated = (bool)stream.ReceiveNext();
            bool networkIsAnimating = (bool)stream.ReceiveNext();
            rootFolder = (string)stream.ReceiveNext();
            subFolder = (string)stream.ReceiveNext();

            if (networkIsAnimating != isAnimating)
            {
                if (networkIsAnimating)
                {
                    if (frames.Count > 0 && isActivated)
                    {
                        isAnimating = true;
                        if (animationCoroutine != null) StopCoroutine(animationCoroutine);
                        animationCoroutine = StartCoroutine(PlayAnimation());
                    }
                }
                else
                {
                    isAnimating = false;
                    if (animationCoroutine != null) StopCoroutine(animationCoroutine);
                }
            }
        }
    }
}
