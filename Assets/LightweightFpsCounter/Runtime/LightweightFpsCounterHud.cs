#if UNITY_EDITOR || DEVELOPMENT_BUILD || FPS_COUNTER_ENABLE_IN_RELEASE
#define FPS_COUNTER_ACTIVE
#endif

using UnityEngine;
#if FPS_COUNTER_ACTIVE
using System;
using System.Collections;
using UnityEngine.Rendering;
#endif

namespace LightweightFpsCounter
{
    [DisallowMultipleComponent]
    public sealed class LightweightFpsCounterHud : MonoBehaviour
    {
        public enum ScreenCorner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
        }

        // Latest / per-second averaged values, readable from any code.
        // Always compiled so callers build in every configuration; they stay 0
        // when the runtime implementation is excluded.
        public static double LatestFps { get; private set; }
        public static double AverageFps { get; private set; }
        public static double LatestCpuFrameTimeMs { get; private set; }
        public static double AverageCpuFrameTimeMs { get; private set; }
        public static double LatestCpuMainThreadFrameTimeMs { get; private set; }
        public static double AverageCpuMainThreadFrameTimeMs { get; private set; }
        public static double LatestCpuPresentWaitTimeMs { get; private set; }
        public static double AverageCpuPresentWaitTimeMs { get; private set; }
        public static double LatestCpuRenderThreadFrameTimeMs { get; private set; }
        public static double AverageCpuRenderThreadFrameTimeMs { get; private set; }
        public static double LatestGpuFrameTimeMs { get; private set; }
        public static double AverageGpuFrameTimeMs { get; private set; }

        // The active instance. There is at most one: it persists across scene
        // loads, and any second instance destroys itself with a warning.
        // It stays registered while disabled, so a debug command can show the
        // HUD with Instance.enabled = true. Null when the runtime implementation
        // is excluded from the build.
        public static LightweightFpsCounterHud Instance { get; private set; }

        [Header("Startup")]
        [Tooltip("Start disabled after registering the singleton and DontDestroyOnLoad. Turn the HUD on later from code: LightweightFpsCounterHud.Instance.enabled = true;")]
        [SerializeField] private bool startDisabled = false;

        [Header("Metrics (FPS is always shown)")]
        [SerializeField] private bool showCpuTotalFrameTime = true;
        [SerializeField] private bool showCpuMainThreadFrameTime = true;
        [SerializeField] private bool showCpuMainThreadPresentWaitTime = true;
        [SerializeField] private bool showCpuRenderThreadFrameTime = true;
        [SerializeField] private bool showGpuFrameTime = true;

        [Header("Labels")]
        [Tooltip("Column headers. Leading spaces in row labels can be used for indentation.")]
        [SerializeField] private string nowHeaderLabel = "NOW";
        [SerializeField] private string avgHeaderLabel = "AVG";
        [SerializeField] private string fpsLabel = "FPS";
        [SerializeField] private string cpuTotalLabel = "CPU";
        [SerializeField] private string cpuMainThreadLabel = " Main";
        [SerializeField] private string cpuPresentWaitLabel = " Wait";
        [SerializeField] private string cpuRenderThreadLabel = " Render";
        [SerializeField] private string gpuLabel = "GPU";

        [Header("Display")]
        [Tooltip("Number of completed frame timings averaged into each NOW update.")]
        [SerializeField, Min(1)] private int framesPerUpdate = 6;
        [Tooltip("Scale factor for the text. Integer values keep pixels crisp.")]
        [SerializeField, Min(0.25f)] private float textScale = 2f;
        [SerializeField] private ScreenCorner anchor = ScreenCorner.TopLeft;
        [Tooltip("Margin from the anchored corner, in pixels.")]
        [SerializeField] private Vector2Int screenMargin = new Vector2Int(8, 8);
        [SerializeField] private Color textColor = Color.white;
        [Tooltip("Background panel color behind the text. Alpha 0 disables the panel.")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.5f);
        [Tooltip("Background panel padding around the text, in pixels.")]
        [SerializeField, Min(0)] private int backgroundPadding = 6;
        [Tooltip("Integer digits reserved for every value column.")]
        [SerializeField] private int integerDigits = 3;

        [Header("Thresholds (0 = disabled)")]
        [Tooltip("Apply warning color to the NOW column. Disable to avoid false alarms from single-frame spikes.")]
        [SerializeField] private bool nowCanWarn = true;
        [Tooltip("Apply error color to the NOW column. Disable to avoid false alarms from single-frame spikes.")]
        [SerializeField] private bool nowCanError = true;
        [SerializeField] private Color warningColor = Color.yellow;
        [SerializeField] private Color errorColor = Color.red;
        [Tooltip("FPS turns warning/error colored when it drops below these values.")]
        [SerializeField, Min(0f)] private float fpsWarningBelow = 30f;
        [SerializeField, Min(0f)] private float fpsErrorBelow = 20f;
        [Tooltip("Frame times turn warning/error colored when they rise above these values (ms).")]
        [SerializeField, Min(0f)] private float cpuTotalWarningAboveMs = 16.8f;
        [SerializeField, Min(0f)] private float cpuTotalErrorAboveMs = 33.4f;
        [SerializeField, Min(0f)] private float cpuMainThreadWarningAboveMs = 16.8f;
        [SerializeField, Min(0f)] private float cpuMainThreadErrorAboveMs = 33.4f;
        [SerializeField, Min(0f)] private float cpuPresentWaitWarningAboveMs = 0f;
        [SerializeField, Min(0f)] private float cpuPresentWaitErrorAboveMs = 0f;
        [SerializeField, Min(0f)] private float cpuRenderThreadWarningAboveMs = 16.8f;
        [SerializeField, Min(0f)] private float cpuRenderThreadErrorAboveMs = 33.4f;
        [SerializeField, Min(0f)] private float gpuWarningAboveMs = 16.8f;
        [SerializeField, Min(0f)] private float gpuErrorAboveMs = 33.4f;

        [Header("Font")]
        [Tooltip("Font atlas texture. Assign the bundled Monogram.png or a custom atlas.")]
        [SerializeField] private Texture2D fontTexture;
        [Tooltip("Overlay shader. Assign the bundled LightweightFpsCounterOverlay.shader.")]
        [SerializeField] private Shader overlayShader;
        [Tooltip("Glyph size in pixels. Glyphs sit at the top-left of their atlas cell.")]
        [SerializeField] private Vector2Int glyphSize = new Vector2Int(5, 9);
        [Tooltip("Atlas cell stride in pixels. Used only to locate glyphs in the atlas.")]
        [SerializeField] private Vector2Int cellSize = new Vector2Int(6, 10);
        [Tooltip("Pixel position of the first cell from the texture's top-left corner.")]
        [SerializeField] private Vector2Int atlasOrigin = Vector2Int.zero;
        [Tooltip("Number of glyph columns in the atlas.")]
        [SerializeField, Min(1)] private int atlasColumns = 16;
        [Tooltip("On-screen spacing between characters, in font pixels.")]
        [SerializeField, Min(0)] private int letterSpacing = 1;
        [Tooltip("On-screen line advance, in font pixels.")]
        [SerializeField, Min(1)] private int lineHeight = 10;

#if FPS_COUNTER_ACTIVE
        private struct ValueField
        {
            public int Metric;
            public bool IsAverage;
            public int FirstQuad;
        }

        // Supports disabled Domain Reload (Enter Play Mode Options).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            LatestFps = AverageFps = 0.0;
            LatestCpuFrameTimeMs = AverageCpuFrameTimeMs = 0.0;
            LatestCpuMainThreadFrameTimeMs = AverageCpuMainThreadFrameTimeMs = 0.0;
            LatestCpuPresentWaitTimeMs = AverageCpuPresentWaitTimeMs = 0.0;
            LatestCpuRenderThreadFrameTimeMs = AverageCpuRenderThreadFrameTimeMs = 0.0;
            LatestGpuFrameTimeMs = AverageGpuFrameTimeMs = 0.0;
        }

        // The number of tracked metrics: FPS + 5 frame times.
        private const int MetricCount = 6;
        // The atlas stores the printable ASCII range 32..126 (95 glyphs).
        private const char FirstChar = ' ';
        private const char LastChar = '~';
        private const int GlyphCount = LastChar - FirstChar + 1;
        // Blank character cells between the label column and each value column.
        private const int ColumnGapChars = 2;
        // Static capacity: 7 lines (header + 6 rows) x up to 54 label/punctuation
        // characters = 378 quads, plus the quad reserved for the background
        // panel (quad 0) -> 384.
        private const int MaxStaticQuads = 384;
        // Dynamic capacity: 6 rows x 2 values x (8 integer + 1 point + 2 fraction)
        // digit slots = 132 quads.
        private const int MaxDynamicQuads = 132;
        // The refresh path uploads buffers whose layout never changes, so all of
        // Unity's re-validation work can be skipped.
        private const MeshUpdateFlags FastUpload = MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontResetBoneBounds;

        private static readonly int AnchorParamsId = Shader.PropertyToID("_AnchorParams");
        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();

        private readonly double[] _latest = new double[MetricCount];
        private readonly double[] _sum = new double[MetricCount];
        private readonly double[] _avg = new double[MetricCount];
        private FrameTiming[] _frameTimings;
        private readonly double[] _pendingTimingSum = new double[MetricCount];
        private readonly ValueField[] _fields = new ValueField[MetricCount * 2];
        private readonly byte[] _fieldSeverity = new byte[MetricCount * 2];
        private readonly float[] _warningThresholds = new float[MetricCount];
        private readonly float[] _errorThresholds = new float[MetricCount];
        // Frame counting for accurate FPS measurement (counts actual rendered frames).
        private int _fpsDisplayFrameCount;
        private float _fpsDisplayElapsedSec;
        private int _fpsAvgFrameCount;
        private int _pendingTimingCount;
        private int _framesUntilTimingRead;
        private ulong _lastFrameTimingTimestamp;
        private bool _timingTimestampsAvailable = true;

        private int _fieldCount;
        private int _sampleCount;
        private bool _hasAvg;
        private float _avgTimer;
        private bool _layoutDirty = true;
        private Coroutine _endOfFrameLoop;

        private Material _material;
        private Mesh _mesh;
        private CommandBuffer _drawCommands;
        private Vector3[] _staticVertices;
        private Vector2[] _staticUvs;
        private Color32[] _staticColors;
        private Vector3[] _dynamicVertices;
        private Vector2[] _dynamicUvs;
        private Color32[] _dynamicColors;
        private Vector2[] _glyphUvs;
        private int _staticQuadCursor;
        private int _dynamicQuadCursor;
        private int _nowQuadCursor;
        private int _averageQuadCursor;
        private int _averageFirstQuad;
        private int _prevStaticQuadCount;
        private int _prevDynamicQuadCount;
        private int _penX;
        private int _penY;
        private int _advanceX;
        private float _blockWidthPx;
        private float _blockHeightPx;
        private double _maxDisplayValue;

        // Singleton lifetime lives in Awake/OnDestroy so Instance stays valid
        // while the HUD is disabled and can be re-enabled from code.
        private void Awake()
        {
            if (Instance != null && !ReferenceEquals(Instance, this))
            {
                Debug.LogWarning("[LightweightFpsCounter] Another LightweightFpsCounterHud is already active; destroying this one.", this);
                enabled = false;
                Destroy(gameObject);
                return;
            }

            // Persist across scene loads; place the prefab in your first scene.
            DontDestroyOnLoad(gameObject);
            Instance = this;

            if (startDisabled) enabled = false;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        private void OnEnable()
        {
            if (fontTexture == null || overlayShader == null)
            {
                Debug.LogError("[LightweightFpsCounter] Assign the bundled Monogram.png and LightweightFpsCounterOverlay.shader.");
                enabled = false;
                return;
            }

            _material = new Material(overlayShader)
            {
                mainTexture = fontTexture,
                hideFlags = HideFlags.HideAndDontSave
            };
            _glyphUvs = new Vector2[GlyphCount * 4];
            _frameTimings = new FrameTiming[Math.Max(framesPerUpdate, 1)];
            _framesUntilTimingRead = _frameTimings.Length;
            _staticVertices = new Vector3[MaxStaticQuads * 4];
            _staticUvs = new Vector2[MaxStaticQuads * 4];
            _staticColors = new Color32[MaxStaticQuads * 4];
            _dynamicVertices = new Vector3[MaxDynamicQuads * 4];
            _dynamicUvs = new Vector2[MaxDynamicQuads * 4];
            _dynamicColors = new Color32[MaxDynamicQuads * 4];
            _mesh = CreateMesh(MaxStaticQuads + MaxDynamicQuads);
            _drawCommands = new CommandBuffer { name = "LightweightFpsCounter" };
            _prevStaticQuadCount = 0;
            _prevDynamicQuadCount = 0;
            _layoutDirty = true;
            _endOfFrameLoop = StartCoroutine(EndOfFrameLoop());

            if (!FrameTimingManager.IsFeatureEnabled())
            {
                Debug.LogWarning("[LightweightFpsCounter] Frame Timing Stats is disabled in Player Settings; all values will read 0.");
            }
        }

        private void OnDisable()
        {
            if (_endOfFrameLoop != null)
            {
                StopCoroutine(_endOfFrameLoop);
                _endOfFrameLoop = null;
            }
            if (_drawCommands != null)
            {
                _drawCommands.Release();
                _drawCommands = null;
            }
            if (_mesh != null) DestroyImmediate(_mesh);
            if (_material != null) DestroyImmediate(_material);
            _mesh = null;
            _material = null;
        }

        private void OnValidate()
        {
            _layoutDirty = true;
        }

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;

            // Count this frame for accurate FPS measurement.
            _fpsDisplayFrameCount++;
            _fpsDisplayElapsedSec += deltaTime;
            _fpsAvgFrameCount++;

            var updateNow = SampleFrameTimings();
            var updateAverage = false;

            _avgTimer += deltaTime;
            if (_avgTimer >= 1f)
            {
                // FPS AVG: actual frames rendered divided by elapsed time.
                _avg[0] = _fpsAvgFrameCount / _avgTimer;
                _fpsAvgFrameCount = 0;

                for (var i = 1; i < MetricCount; i++)
                {
                    _avg[i] = _sampleCount > 0 ? _sum[i] / _sampleCount : 0.0;
                    _sum[i] = 0.0;
                }
                _sampleCount = 0;
                _hasAvg = true;
                _avgTimer -= 1f;

                AverageFps = _avg[0];
                AverageCpuFrameTimeMs = _avg[1];
                AverageCpuMainThreadFrameTimeMs = _avg[2];
                AverageCpuPresentWaitTimeMs = _avg[3];
                AverageCpuRenderThreadFrameTimeMs = _avg[4];
                AverageGpuFrameTimeMs = _avg[5];
                updateAverage = true;
            }

            if (_layoutDirty)
            {
                _layoutDirty = false;
                RebuildLayout();
                updateNow = updateAverage = true;
            }

            if (updateNow || updateAverage) UpdateValues(updateNow, updateAverage);
        }

        // Runs after every camera and after Screen Space - Overlay UI, drawing
        // straight to the back buffer so the HUD stays on top of everything.
        private IEnumerator EndOfFrameLoop()
        {
            while (true)
            {
                yield return EndOfFrame;
                if (_material == null || _staticQuadCursor == 0) continue;

                Graphics.ExecuteCommandBuffer(_drawCommands);
            }
        }

        private bool SampleFrameTimings()
        {
            FrameTimingManager.CaptureFrameTimings();

            var requestedFrames = Math.Max(framesPerUpdate, 1);
            if (_frameTimings == null || _frameTimings.Length != requestedFrames)
            {
                _frameTimings = new FrameTiming[requestedFrames];
                ResetPendingTimings();
                _framesUntilTimingRead = requestedFrames;
            }

            if (--_framesUntilTimingRead > 0) return false;

            var timingCount = (int)FrameTimingManager.GetLatestTimings((uint)requestedFrames, _frameTimings);
            if (timingCount == 0) return false;

            if (_timingTimestampsAvailable && FrameTimingTimestamp(in _frameTimings[0]) == 0)
            {
                _timingTimestampsAvailable = false;
                ResetPendingTimings();
            }

            // GetLatestTimings returns newest first. Accumulate unseen samples in
            // chronological order until a complete display batch is available.
            for (var i = timingCount - 1; i >= 0 && _pendingTimingCount < requestedFrames; i--)
            {
                ref readonly var timing = ref _frameTimings[i];
                if (_timingTimestampsAvailable)
                {
                    var timestamp = FrameTimingTimestamp(in timing);
                    if (timestamp <= _lastFrameTimingTimestamp) continue;
                    _lastFrameTimingTimestamp = timestamp;
                }

                AccumulateFrameTiming(in timing);
            }

            if (_pendingTimingCount < requestedFrames) return false;

            _latest[0] = _fpsDisplayElapsedSec > 0f ? _fpsDisplayFrameCount / _fpsDisplayElapsedSec : 0f;
            for (var i = 1; i < MetricCount; i++) _latest[i] = _pendingTimingSum[i] / _pendingTimingCount;

            LatestFps = _latest[0];
            LatestCpuFrameTimeMs = _latest[1];
            LatestCpuMainThreadFrameTimeMs = _latest[2];
            LatestCpuPresentWaitTimeMs = _latest[3];
            LatestCpuRenderThreadFrameTimeMs = _latest[4];
            LatestGpuFrameTimeMs = _latest[5];

            _fpsDisplayFrameCount = 0;
            _fpsDisplayElapsedSec = 0f;
            ResetPendingTimings();
            _framesUntilTimingRead = requestedFrames;
            return true;
        }

        private static ulong FrameTimingTimestamp(in FrameTiming timing)
        {
            if (timing.frameStartTimestamp != 0) return timing.frameStartTimestamp;
            if (timing.firstSubmitTimestamp != 0) return timing.firstSubmitTimestamp;
            if (timing.cpuTimePresentCalled != 0) return timing.cpuTimePresentCalled;
            return timing.cpuTimeFrameComplete;
        }

        private void AccumulateFrameTiming(in FrameTiming timing)
        {
            _pendingTimingSum[1] += timing.cpuFrameTime;
            _pendingTimingSum[2] += timing.cpuMainThreadFrameTime;
            _pendingTimingSum[3] += timing.cpuMainThreadPresentWaitTime;
            _pendingTimingSum[4] += timing.cpuRenderThreadFrameTime;
            _pendingTimingSum[5] += timing.gpuFrameTime;

            _sum[1] += timing.cpuFrameTime;
            _sum[2] += timing.cpuMainThreadFrameTime;
            _sum[3] += timing.cpuMainThreadPresentWaitTime;
            _sum[4] += timing.cpuRenderThreadFrameTime;
            _sum[5] += timing.gpuFrameTime;
            _pendingTimingCount++;
            _sampleCount++;
        }

        private void ResetPendingTimings()
        {
            for (var i = 1; i < MetricCount; i++) _pendingTimingSum[i] = 0.0;
            _pendingTimingCount = 0;
        }

        private double AvgOf(int metric)
        {
            return _hasAvg ? _avg[metric] : 0.0;
        }

        // The shader resolves the final position as:
        // pixel = vertex.xy + _AnchorParams.xy * screenSize + _AnchorParams.zw
        private void ApplyAnchor()
        {
            float anchorX, anchorY, offsetX, offsetY;
            switch (anchor)
            {
                case ScreenCorner.TopRight:
                    anchorX = 1f;
                    anchorY = 1f;
                    offsetX = -screenMargin.x - _blockWidthPx;
                    offsetY = -screenMargin.y;
                    break;
                case ScreenCorner.BottomLeft:
                    anchorX = 0f;
                    anchorY = 0f;
                    offsetX = screenMargin.x;
                    offsetY = screenMargin.y + _blockHeightPx;
                    break;
                case ScreenCorner.BottomRight:
                    anchorX = 1f;
                    anchorY = 0f;
                    offsetX = -screenMargin.x - _blockWidthPx;
                    offsetY = screenMargin.y + _blockHeightPx;
                    break;
                default:
                    anchorX = 0f;
                    anchorY = 1f;
                    offsetX = screenMargin.x;
                    offsetY = -screenMargin.y;
                    break;
            }
            _material.SetVector(AnchorParamsId, new Vector4(anchorX, anchorY, offsetX, offsetY));
        }

        private void ApplyColors()
        {
            Color32 text = textColor;
            FillColors(_staticColors, text);
            FillColors(_dynamicColors, text);
            Color32 background = backgroundColor;
            _staticColors[0] = _staticColors[1] = _staticColors[2] = _staticColors[3] = background;
            var staticVertexCount = _staticQuadCursor * 4;
            _mesh.SetVertexBufferData(_staticColors, 0, 0, staticVertexCount, 2, FastUpload);
            _mesh.SetVertexBufferData(_dynamicColors, 0, staticVertexCount, _dynamicQuadCursor * 4, 2, FastUpload);
        }

        private static void FillColors(Color32[] colors, Color32 text)
        {
            for (var v = 0; v < colors.Length; v++) colors[v] = text;
        }

        // Glyphs sit at the top-left of each cell; cells start at atlasOrigin and
        // repeat every cellSize pixels. Refilled on every layout rebuild so font
        // metrics can be tuned live in the Inspector.
        private void FillGlyphUvTable()
        {
            var texelU = 1f / fontTexture.width;
            var texelV = 1f / fontTexture.height;
            for (var g = 0; g < GlyphCount; g++)
            {
                var cellX = atlasOrigin.x + g % atlasColumns * cellSize.x;
                var cellTop = atlasOrigin.y + g / atlasColumns * cellSize.y;
                var u0 = cellX * texelU;
                var u1 = (cellX + glyphSize.x) * texelU;
                var v1 = 1f - cellTop * texelV;
                var v0 = v1 - glyphSize.y * texelV;
                var i = g * 4;
                _glyphUvs[i] = new Vector2(u0, v1);
                _glyphUvs[i + 1] = new Vector2(u1, v1);
                _glyphUvs[i + 2] = new Vector2(u0, v0);
                _glyphUvs[i + 3] = new Vector2(u1, v0);
            }
        }

        private static Mesh CreateMesh(int maxQuads)
        {
            var vertexCount = maxQuads * 4;
            var indices = new ushort[maxQuads * 6];
            for (var q = 0; q < maxQuads; q++)
            {
                var v = (ushort)(q * 4);
                var i = q * 6;
                indices[i] = v;
                indices[i + 1] = (ushort)(v + 1);
                indices[i + 2] = (ushort)(v + 2);
                indices[i + 3] = (ushort)(v + 2);
                indices[i + 4] = (ushort)(v + 1);
                indices[i + 5] = (ushort)(v + 3);
            }

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            // Independent streams make UV-only partial uploads possible while a
            // single mesh keeps the hot render path to one draw call.
            mesh.SetVertexBufferParams(vertexCount,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 1),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 2));
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, FastUpload);
            mesh.subMeshCount = 1;
            // A large fixed bounds avoids RecalculateBounds on every rebuild.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100000f));
            return mesh;
        }

        // Layout is rebuilt only when settings change. Header/labels and digit
        // slots occupy separate ranges of one mesh; NOW and AVG are contiguous
        // sub-ranges so ordinary refreshes upload only the NOW UVs.
        private void RebuildLayout()
        {
            // Quad 0 of the static mesh is reserved for the background panel.
            _staticQuadCursor = 1;
            _dynamicQuadCursor = 0;
            _nowQuadCursor = 0;
            _fieldCount = 0;
            _penX = 0;
            _penY = 0;
            _advanceX = glyphSize.x + letterSpacing;
            _blockWidthPx = 0f;
            _maxDisplayValue = Math.Pow(10, integerDigits) - 0.01;
            FillGlyphUvTable();

            _warningThresholds[0] = fpsWarningBelow;
            _errorThresholds[0] = fpsErrorBelow;
            _warningThresholds[1] = cpuTotalWarningAboveMs;
            _errorThresholds[1] = cpuTotalErrorAboveMs;
            _warningThresholds[2] = cpuMainThreadWarningAboveMs;
            _errorThresholds[2] = cpuMainThreadErrorAboveMs;
            _warningThresholds[3] = cpuPresentWaitWarningAboveMs;
            _errorThresholds[3] = cpuPresentWaitErrorAboveMs;
            _warningThresholds[4] = cpuRenderThreadWarningAboveMs;
            _errorThresholds[4] = cpuRenderThreadErrorAboveMs;
            _warningThresholds[5] = gpuWarningAboveMs;
            _errorThresholds[5] = gpuErrorAboveMs;
            // Force severity colors to be reapplied after ApplyColors resets them.
            for (var i = 0; i < _fieldSeverity.Length; i++) _fieldSeverity[i] = byte.MaxValue;

            // Column layout in character cells: a left-aligned label column
            // followed by two right-aligned value columns (NOW / AVG).
            var labelChars = fpsLabel.Length;
            if (showCpuTotalFrameTime) labelChars = Math.Max(labelChars, cpuTotalLabel.Length);
            if (showCpuMainThreadFrameTime) labelChars = Math.Max(labelChars, cpuMainThreadLabel.Length);
            if (showCpuMainThreadPresentWaitTime) labelChars = Math.Max(labelChars, cpuPresentWaitLabel.Length);
            if (showCpuRenderThreadFrameTime) labelChars = Math.Max(labelChars, cpuRenderThreadLabel.Length);
            if (showGpuFrameTime) labelChars = Math.Max(labelChars, gpuLabel.Length);
            var valueChars = integerDigits + 3;
            var nowColumn = labelChars + ColumnGapChars;
            var avgColumn = nowColumn + valueChars + ColumnGapChars;

            var rowCount = 1;
            if (showCpuTotalFrameTime) rowCount++;
            if (showCpuMainThreadFrameTime) rowCount++;
            if (showCpuMainThreadPresentWaitTime) rowCount++;
            if (showCpuRenderThreadFrameTime) rowCount++;
            if (showGpuFrameTime) rowCount++;
            _averageFirstQuad = rowCount * valueChars;
            _averageQuadCursor = _averageFirstQuad;

            BuildHeaderLine(nowColumn, avgColumn, valueChars);
            BuildLine(fpsLabel, 0, nowColumn, avgColumn, false);
            if (showCpuTotalFrameTime) BuildLine(cpuTotalLabel, 1, nowColumn, avgColumn, true);
            if (showCpuMainThreadFrameTime) BuildLine(cpuMainThreadLabel, 2, nowColumn, avgColumn, true);
            if (showCpuMainThreadPresentWaitTime) BuildLine(cpuPresentWaitLabel, 3, nowColumn, avgColumn, true);
            if (showCpuRenderThreadFrameTime) BuildLine(cpuRenderThreadLabel, 4, nowColumn, avgColumn, true);
            if (showGpuFrameTime) BuildLine(gpuLabel, 5, nowColumn, avgColumn, true);
            _dynamicQuadCursor = _averageQuadCursor;

            // Last baseline minus one line advance, plus the glyph height.
            _blockHeightPx = (_penY - lineHeight + glyphSize.y) * textScale;

            WriteBackgroundQuad();

            ZeroQuads(_staticVertices, _staticQuadCursor, _prevStaticQuadCount);
            ZeroQuads(_dynamicVertices, _dynamicQuadCursor, _prevDynamicQuadCount);
            _prevStaticQuadCount = _staticQuadCursor;
            _prevDynamicQuadCount = _dynamicQuadCursor;

            // Keep the preallocated index buffers, but draw only the quads used
            // by the current layout. Unused capacity must not reach the GPU as
            // degenerate triangles.
            SetActiveQuadCount(_mesh, _staticQuadCursor + _dynamicQuadCursor);

            var staticVertexCount = _staticQuadCursor * 4;
            var dynamicVertexCount = _dynamicQuadCursor * 4;
            _mesh.SetVertexBufferData(_staticVertices, 0, 0, staticVertexCount, 0, FastUpload);
            _mesh.SetVertexBufferData(_dynamicVertices, 0, staticVertexCount, dynamicVertexCount, 0, FastUpload);
            _mesh.SetVertexBufferData(_staticUvs, 0, 0, staticVertexCount, 1, FastUpload);
            _mesh.SetVertexBufferData(_dynamicUvs, 0, staticVertexCount, dynamicVertexCount, 1, FastUpload);

            ApplyColors();
            ApplyAnchor();
            _drawCommands.Clear();
            _drawCommands.DrawMesh(_mesh, Matrix4x4.identity, _material, 0, 0);
        }

        private static void SetActiveQuadCount(Mesh mesh, int quadCount)
        {
            var vertexCount = quadCount * 4;
            var subMesh = new SubMeshDescriptor(0, quadCount * 6, MeshTopology.Triangles)
            {
                bounds = mesh.bounds,
                firstVertex = 0,
                vertexCount = vertexCount,
            };
            mesh.SetSubMesh(0, subMesh, FastUpload);
        }

        // The background panel fills the text block plus padding. Negative UVs
        // tell the shader to output the vertex color without sampling the font.
        private void WriteBackgroundQuad()
        {
            if (backgroundColor.a <= 0f)
            {
                _staticVertices[0] = _staticVertices[1] = _staticVertices[2] = _staticVertices[3] = Vector3.zero;
            }
            else
            {
                float pad = backgroundPadding;
                _staticVertices[0] = new Vector3(-pad, pad, 0f);
                _staticVertices[1] = new Vector3(_blockWidthPx + pad, pad, 0f);
                _staticVertices[2] = new Vector3(-pad, -(_blockHeightPx + pad), 0f);
                _staticVertices[3] = new Vector3(_blockWidthPx + pad, -(_blockHeightPx + pad), 0f);
            }
            _staticUvs[0] = _staticUvs[1] = _staticUvs[2] = _staticUvs[3] = new Vector2(-1f, -1f);
        }

        private void BuildHeaderLine(int nowColumn, int avgColumn, int valueChars)
        {
            SetPenColumn(nowColumn + Math.Max(valueChars - nowHeaderLabel.Length, 0));
            AddStaticString(nowHeaderLabel);
            SetPenColumn(avgColumn + Math.Max(valueChars - avgHeaderLabel.Length, 0));
            AddStaticString(avgHeaderLabel);
            EndLine();
        }

        private void BuildLine(string label, int metric, int nowColumn, int avgColumn, bool isMilliseconds)
        {
            AddStaticString(label);
            SetPenColumn(nowColumn);
            AddValueField(metric, false);
            SetPenColumn(avgColumn);
            AddValueField(metric, true);
            if (isMilliseconds) AddStaticString(" ms");
            EndLine();
        }

        private void EndLine()
        {
            // Track the widest line for right-side anchoring
            // (the trailing advance includes the letter spacing, hence the subtraction).
            var lineWidthPx = (_penX - letterSpacing) * textScale;
            if (lineWidthPx > _blockWidthPx) _blockWidthPx = lineWidthPx;
            _penX = 0;
            _penY += lineHeight;
        }

        private void SetPenColumn(int column)
        {
            _penX = column * _advanceX;
        }

        private void AddValueField(int metric, bool isAverage)
        {
            var firstQuad = isAverage ? _averageQuadCursor : _nowQuadCursor;
            _fields[_fieldCount] = new ValueField
            {
                Metric = metric,
                IsAverage = isAverage,
                FirstQuad = firstQuad,
            };
            _fieldCount++;

            // All slots are dynamic (including the decimal point) so the whole
            // value can change color based on thresholds.
            var slotCount = integerDigits + 3;
            for (var i = 0; i < slotCount; i++)
            {
                var quad = firstQuad + i;
                if (quad < MaxDynamicQuads) WriteQuadPositions(_dynamicVertices, quad, _penX, _penY);
                _penX += _advanceX;
            }
            if (isAverage) _averageQuadCursor += slotCount;
            else _nowQuadCursor += slotCount;
            // The decimal point glyph never changes; write its UVs once here.
            SetSlotGlyph(firstQuad + integerDigits, '.');
        }

        private void AddStaticString(string text)
        {
            for (var i = 0; i < text.Length; i++) AddStaticChar(text[i]);
        }

        private void AddStaticChar(char c)
        {
            if (c > FirstChar && c <= LastChar && _staticQuadCursor < MaxStaticQuads)
            {
                WriteQuadPositions(_staticVertices, _staticQuadCursor, _penX, _penY);
                WriteQuadUvs(_staticUvs, _staticQuadCursor, c - FirstChar);
                _staticQuadCursor++;
            }
            _penX += _advanceX;
        }

        private void WriteQuadPositions(Vector3[] vertices, int quad, int fontX, int fontY)
        {
            var s = textScale;
            var x0 = fontX * s;
            var x1 = (fontX + glyphSize.x) * s;
            var y0 = -fontY * s;
            var y1 = -(fontY + glyphSize.y) * s;
            var vi = quad * 4;
            vertices[vi] = new Vector3(x0, y0, 0f);
            vertices[vi + 1] = new Vector3(x1, y0, 0f);
            vertices[vi + 2] = new Vector3(x0, y1, 0f);
            vertices[vi + 3] = new Vector3(x1, y1, 0f);
        }

        private void WriteQuadUvs(Vector2[] uvs, int quad, int glyph)
        {
            var vi = quad * 4;
            var gi = glyph * 4;
            uvs[vi] = _glyphUvs[gi];
            uvs[vi + 1] = _glyphUvs[gi + 1];
            uvs[vi + 2] = _glyphUvs[gi + 2];
            uvs[vi + 3] = _glyphUvs[gi + 3];
        }

        // Refresh path: rewrites only the changed NOW/AVG UV range, and colors
        // only when a value crosses a threshold. Zero GC allocations.
        private void UpdateValues(bool updateNow, bool updateAverage)
        {
            var colorsDirty = false;
            for (var i = 0; i < _fieldCount; i++)
            {
                ref readonly var field = ref _fields[i];
                if (field.IsAverage ? !updateAverage : !updateNow) continue;
                var value = field.IsAverage ? AvgOf(field.Metric) : _latest[field.Metric];
                WriteFieldDigits(in field, value);

                var severity = SeverityOf(field.Metric, value);
                if (!field.IsAverage)
                {
                    if (!nowCanWarn && severity == 1) severity = 0;
                    if (!nowCanError && severity == 2) severity = 0;
                }
                if (severity != _fieldSeverity[i])
                {
                    _fieldSeverity[i] = severity;
                    WriteFieldColors(in field, severity);
                    colorsDirty = true;
                }
            }
            var dynamicVertexStart = _staticQuadCursor * 4;
            if (updateNow)
            {
                var nowVertexCount = _averageFirstQuad * 4;
                _mesh.SetVertexBufferData(_dynamicUvs, 0, dynamicVertexStart, nowVertexCount, 1, FastUpload);
            }
            if (updateAverage)
            {
                var averageVertexStart = _averageFirstQuad * 4;
                var averageVertexCount = (_dynamicQuadCursor - _averageFirstQuad) * 4;
                _mesh.SetVertexBufferData(_dynamicUvs, averageVertexStart, dynamicVertexStart + averageVertexStart, averageVertexCount, 1, FastUpload);
            }
            if (colorsDirty) _mesh.SetVertexBufferData(_dynamicColors, 0, dynamicVertexStart, _dynamicQuadCursor * 4, 2, FastUpload);
        }

        // 0 = normal, 1 = warning, 2 = error. FPS triggers below its thresholds,
        // frame times above theirs. A threshold of 0 is disabled.
        private byte SeverityOf(int metric, double value)
        {
            // Zero means that no usable sample has been received yet (or that
            // this timing is unavailable on the current platform). It is not a
            // performance failure and must keep the normal text color.
            if (value <= 0.0) return 0;

            var warning = _warningThresholds[metric];
            var error = _errorThresholds[metric];
            if (metric == 0)
            {
                if (error > 0f && value < error) return 2;
                if (warning > 0f && value < warning) return 1;
            }
            else
            {
                if (error > 0f && value > error) return 2;
                if (warning > 0f && value > warning) return 1;
            }
            return 0;
        }

        private void WriteFieldColors(in ValueField field, byte severity)
        {
            Color32 color = severity == 2 ? errorColor : severity == 1 ? warningColor : textColor;
            var slotCount = integerDigits + 3;
            for (var s = 0; s < slotCount; s++)
            {
                var vi = (field.FirstQuad + s) * 4;
                _dynamicColors[vi] = _dynamicColors[vi + 1] = _dynamicColors[vi + 2] = _dynamicColors[vi + 3] = color;
            }
        }

        private void WriteFieldDigits(in ValueField field, double value)
        {
            if (value < 0.0) value = 0.0;
            if (value > _maxDisplayValue) value = _maxDisplayValue;
            var scaled = (long)(value * 100.0 + 0.5);
            var intPart = scaled / 100;
            var fracPart = (int)(scaled % 100);

            SetSlotGlyph(field.FirstQuad + integerDigits + 1, (char)('0' + fracPart / 10));
            SetSlotGlyph(field.FirstQuad + integerDigits + 2, (char)('0' + fracPart % 10));

            for (var d = integerDigits - 1; d >= 0; d--)
            {
                var quad = field.FirstQuad + d;
                if (d == integerDigits - 1 || intPart > 0)
                {
                    SetSlotGlyph(quad, (char)('0' + (int)(intPart % 10)));
                    intPart /= 10;
                }
                else
                {
                    SetSlotGlyph(quad, ' ');
                }
            }
        }

        private void SetSlotGlyph(int quad, char c)
        {
            WriteQuadUvs(_dynamicUvs, quad, c - FirstChar);
        }

        private static void ZeroQuads(Vector3[] vertices, int from, int to)
        {
            for (var q = from; q < to; q++)
            {
                var v = q * 4;
                vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = Vector3.zero;
            }
        }
#endif
    }
}
