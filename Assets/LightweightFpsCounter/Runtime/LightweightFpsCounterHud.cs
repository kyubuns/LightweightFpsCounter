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
        // when the counter is stripped.
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
        // HUD with Instance.enabled = true. Null when stripped from the build.
        public static LightweightFpsCounterHud Instance { get; private set; }

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
        [Tooltip("Display refresh interval in milliseconds.")]
        [SerializeField, Min(1f)] private float updateIntervalMs = 100f;
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
        [Tooltip("Point keeps pixels sharp at integer Text Scale values. Bilinear smooths edges at fractional scales (e.g. 1.5x) with no extra rendering cost.")]
        [SerializeField] private FilterMode fontFilterMode = FilterMode.Point;

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
        private static readonly int ScreenSizeFlipId = Shader.PropertyToID("_ScreenSizeFlip");
        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();

        private readonly double[] _latest = new double[MetricCount];
        private readonly double[] _sum = new double[MetricCount];
        private readonly double[] _avg = new double[MetricCount];
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
        private readonly ValueField[] _fields = new ValueField[MetricCount * 2];
        private readonly byte[] _fieldSeverity = new byte[MetricCount * 2];
        private readonly float[] _warningThresholds = new float[MetricCount];
        private readonly float[] _errorThresholds = new float[MetricCount];
        // Frame counting for accurate FPS measurement (counts actual rendered frames).
        private int _fpsDisplayFrameCount;
        private float _fpsDisplayElapsedSec;
        private int _fpsAvgFrameCount;

        private int _fieldCount;
        private int _sampleCount;
        private bool _hasAvg;
        private float _avgTimer;
        private float _displayTimer;
        private bool _layoutDirty = true;
        private Coroutine _endOfFrameLoop;

        private Material _material;
        private Mesh _staticMesh;
        private Mesh _dynamicMesh;
        private Vector3[] _staticVertices;
        private Vector2[] _staticUvs;
        private Color32[] _staticColors;
        private Vector3[] _dynamicVertices;
        private Vector2[] _dynamicUvs;
        private Color32[] _dynamicColors;
        private Vector2[] _glyphUvs;
        private int _staticQuadCursor;
        private int _dynamicQuadCursor;
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
            fontTexture.filterMode = fontFilterMode;
            _glyphUvs = new Vector2[GlyphCount * 4];
            _staticMesh = CreateMesh(MaxStaticQuads, out _staticVertices, out _staticUvs, out _staticColors);
            _dynamicMesh = CreateMesh(MaxDynamicQuads, out _dynamicVertices, out _dynamicUvs, out _dynamicColors);
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
            if (_staticMesh != null) DestroyImmediate(_staticMesh);
            if (_dynamicMesh != null) DestroyImmediate(_dynamicMesh);
            if (_material != null) DestroyImmediate(_material);
            _staticMesh = null;
            _dynamicMesh = null;
            _material = null;
        }

        private void OnValidate()
        {
            _layoutDirty = true;
        }

        private void Update()
        {
            // Count this frame for accurate FPS measurement.
            _fpsDisplayFrameCount++;
            _fpsDisplayElapsedSec += Time.unscaledDeltaTime;
            _fpsAvgFrameCount++;

            SampleFrameTimings();

            _avgTimer += Time.unscaledDeltaTime;
            if (_avgTimer >= 1f)
            {
                // FPS AVG: actual frames rendered divided by elapsed time.
                _avg[0] = _fpsAvgFrameCount / _avgTimer;
                _fpsAvgFrameCount = 0;

                for (var i = 1; i < MetricCount; i++)
                {
                    _avg[i] = _sum[i] / _sampleCount;
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
            }

            var refresh = _layoutDirty;
            if (_layoutDirty)
            {
                _layoutDirty = false;
                RebuildLayout();
            }

            _displayTimer += Time.unscaledDeltaTime * 1000f;
            if (_displayTimer >= updateIntervalMs)
            {
                // FPS NOW: actual frames rendered over this display window.
                _latest[0] = _fpsDisplayElapsedSec > 0f ? _fpsDisplayFrameCount / _fpsDisplayElapsedSec : 0f;
                LatestFps = _latest[0];
                _fpsDisplayFrameCount = 0;
                _fpsDisplayElapsedSec = 0f;

                _displayTimer = 0f;
                refresh = true;
            }

            if (refresh) UpdateValues();
        }

        // Runs after every camera and after Screen Space - Overlay UI, drawing
        // straight to the back buffer so the HUD stays on top of everything.
        private IEnumerator EndOfFrameLoop()
        {
            while (true)
            {
                yield return EndOfFrame;
                if (_material == null || _staticQuadCursor == 0) continue;

                _material.SetVector(ScreenSizeFlipId, new Vector4(Screen.width, Screen.height, BackbufferYFlip(), 0f));
                _material.SetPass(0);
                Graphics.DrawMeshNow(_staticMesh, Matrix4x4.identity);
                Graphics.DrawMeshNow(_dynamicMesh, Matrix4x4.identity);
            }
        }

        // Asks Unity whether the current back buffer expects a flipped Y axis.
        private static float BackbufferYFlip()
        {
            var proj = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f), false);
            return proj.m11 > 0f ? 1f : -1f;
        }

        private void SampleFrameTimings()
        {
            FrameTimingManager.CaptureFrameTimings();

            double cpuTotal, cpuMain, cpuWait, cpuRender, gpu;
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) >= 1)
            {
                ref readonly var t = ref _frameTimings[0];
                cpuTotal = t.cpuFrameTime;
                cpuMain = t.cpuMainThreadFrameTime;
                cpuWait = t.cpuMainThreadPresentWaitTime;
                cpuRender = t.cpuRenderThreadFrameTime;
                gpu = t.gpuFrameTime;
            }
            else
            {
                cpuTotal = cpuMain = cpuWait = cpuRender = gpu = 0.0;
            }

            // _latest[0] (FPS) is computed in Update() via frame counting; not set here.
            _latest[1] = cpuTotal;
            _latest[2] = cpuMain;
            _latest[3] = cpuWait;
            _latest[4] = cpuRender;
            _latest[5] = gpu;

            LatestCpuFrameTimeMs = _latest[1];
            LatestCpuMainThreadFrameTimeMs = _latest[2];
            LatestCpuPresentWaitTimeMs = _latest[3];
            LatestCpuRenderThreadFrameTimeMs = _latest[4];
            LatestGpuFrameTimeMs = _latest[5];

            for (var i = 1; i < MetricCount; i++) _sum[i] += _latest[i];
            _sampleCount++;
        }

        private double AvgOf(int metric)
        {
            if (_hasAvg) return _avg[metric];
            return _sampleCount > 0 ? _sum[metric] / _sampleCount : _latest[metric];
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
            _staticMesh.SetColors(_staticColors, 0, _staticQuadCursor * 4, FastUpload);
            _dynamicMesh.SetColors(_dynamicColors, 0, _dynamicQuadCursor * 4, FastUpload);
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

        private static Mesh CreateMesh(int maxQuads, out Vector3[] vertices, out Vector2[] uvs, out Color32[] colors)
        {
            var vertexCount = maxQuads * 4;
            vertices = new Vector3[vertexCount];
            uvs = new Vector2[vertexCount];
            colors = new Color32[vertexCount];

            var indices = new int[maxQuads * 6];
            for (var q = 0; q < maxQuads; q++)
            {
                var v = q * 4;
                var i = q * 6;
                indices[i] = v;
                indices[i + 1] = v + 1;
                indices[i + 2] = v + 2;
                indices[i + 3] = v + 2;
                indices[i + 4] = v + 1;
                indices[i + 5] = v + 3;
            }

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = indices;
            // A large fixed bounds avoids RecalculateBounds on every rebuild.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100000f));
            return mesh;
        }

        // Layout is rebuilt only when settings change. Header and label text live in
        // the static mesh; digit slots live in the dynamic mesh at fixed positions,
        // so a display refresh only rewrites the dynamic mesh's UV array.
        private void RebuildLayout()
        {
            // Quad 0 of the static mesh is reserved for the background panel.
            _staticQuadCursor = 1;
            _dynamicQuadCursor = 0;
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

            BuildHeaderLine(nowColumn, avgColumn, valueChars);
            BuildLine(fpsLabel, 0, nowColumn, avgColumn, false);
            if (showCpuTotalFrameTime) BuildLine(cpuTotalLabel, 1, nowColumn, avgColumn, true);
            if (showCpuMainThreadFrameTime) BuildLine(cpuMainThreadLabel, 2, nowColumn, avgColumn, true);
            if (showCpuMainThreadPresentWaitTime) BuildLine(cpuPresentWaitLabel, 3, nowColumn, avgColumn, true);
            if (showCpuRenderThreadFrameTime) BuildLine(cpuRenderThreadLabel, 4, nowColumn, avgColumn, true);
            if (showGpuFrameTime) BuildLine(gpuLabel, 5, nowColumn, avgColumn, true);

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
            SetActiveQuadCount(_staticMesh, _staticQuadCursor);
            SetActiveQuadCount(_dynamicMesh, _dynamicQuadCursor);

            var staticVertexCount = _staticQuadCursor * 4;
            var dynamicVertexCount = _dynamicQuadCursor * 4;
            _staticMesh.SetVertices(_staticVertices, 0, staticVertexCount, FastUpload);
            _staticMesh.SetUVs(0, _staticUvs, 0, staticVertexCount, FastUpload);
            _dynamicMesh.SetVertices(_dynamicVertices, 0, dynamicVertexCount, FastUpload);
            _dynamicMesh.SetUVs(0, _dynamicUvs, 0, dynamicVertexCount, FastUpload);

            ApplyColors();
            ApplyAnchor();
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
            var firstQuad = _dynamicQuadCursor;
            _fields[_fieldCount] = new ValueField
            {
                Metric = metric,
                IsAverage = isAverage,
                FirstQuad = firstQuad,
            };
            _fieldCount++;

            // All slots are dynamic (including the decimal point) so the whole
            // value can change color based on thresholds.
            for (var i = 0; i < integerDigits + 3; i++) AddDynamicSlot();
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

        private void AddDynamicSlot()
        {
            if (_dynamicQuadCursor < MaxDynamicQuads)
            {
                WriteQuadPositions(_dynamicVertices, _dynamicQuadCursor, _penX, _penY);
                _dynamicQuadCursor++;
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

        // Refresh path: rewrites the dynamic mesh's UV array, and its colors only
        // when a value crosses a threshold. Zero GC allocations.
        private void UpdateValues()
        {
            var colorsDirty = false;
            for (var i = 0; i < _fieldCount; i++)
            {
                ref readonly var field = ref _fields[i];
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
            var activeVertexCount = _dynamicQuadCursor * 4;
            _dynamicMesh.SetUVs(0, _dynamicUvs, 0, activeVertexCount, FastUpload);
            if (colorsDirty) _dynamicMesh.SetColors(_dynamicColors, 0, activeVertexCount, FastUpload);
        }

        // 0 = normal, 1 = warning, 2 = error. FPS triggers below its thresholds,
        // frame times above theirs. A threshold of 0 is disabled.
        private byte SeverityOf(int metric, double value)
        {
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
