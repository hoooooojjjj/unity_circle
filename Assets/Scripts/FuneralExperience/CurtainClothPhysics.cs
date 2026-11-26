using System;
using UnityEngine;

/// <summary>
/// 묵직하고 안정적인 VR 커튼 물리 시뮬레이터 (Explicit-Velocity XPBD + Heavy Damping + Smooth Fold Filtering).
/// - 초기 프레임 응력(0mm) 보장 + 미세 속도 슬립(Sleep)으로 가만히 있을 때 떨림·팔락임이 전혀 없습니다.
/// - 묵직한 삼베/리넨 무게감(강한 감쇠 + 부드러운 전단/굽힘 비율 + 내부 버텍스 스무딩)으로
///   종이처럼 각지게 접히지 않고 부드럽고 둥근 천 주름을 형성합니다.
/// - 상단 커튼링은 늘어나지 않으면서(stretchOnly) 레일 마찰·링 적층(stacking)으로 밀어 둔 자리에 안정적으로 멈춥니다.
/// - 양손 3D 핀치 잡기 + 손/몸 스침 충돌을 지원합니다.
/// </summary>
[RequireComponent(typeof(ProceduralCurtainMesh), typeof(MeshFilter))]
public class CurtainClothPhysics : MonoBehaviour
{
    [Header("물리 안정성 & 무게감 (묵직한 커튼)")]
    [Range(2, 6)] public int substeps = 4;
    [Range(2, 6)] public int constraintIterations = 3;
    [Tooltip("중력 (약간 차분하게 설정해 천이 묵직하게 가라앉도록 함)")]
    public float gravity = -7.5f;
    [Tooltip("공기·섬유 내부 감쇠 (높을수록 팔락이지 않고 묵직하게 멈춤)")]
    [Range(2f, 16f)] public float clothDamping = 9.5f;
    [Tooltip("이 속도(m/s) 이하의 미세 떨림은 완전히 정지 처리")]
    public float sleepSpeed = 0.008f;
    public float maxVertexSpeed = 3.2f;

    [Header("천 재질 강성 (종이 질감 방지 -> 부드러운 중량 천)")]
    [Tooltip("세로 신축 유연도 (매우 작아 중력에 늘어지지 않음)")]
    public float verticalCompliance = 0.00001f;
    [Tooltip("가로 신축 유연도")]
    public float horizontalCompliance = 0.00015f;
    [Tooltip("대각선 전단 유연도 (너무 뻣뻣하면 종이처럼 접히므로 부드럽게 설정)")]
    public float shearCompliance = 0.0025f;
    [Tooltip("굽힘 유연도 (부드러운 둥근 주름 유지)")]
    public float bendCompliance = 0.0018f;
    [Tooltip("각진 종이 접힘을 방지하는 내부 주름 스무딩 강도")]
    [Range(0f, 0.35f)] public float foldSmoothStrength = 0.14f;

    [Header("상단 커튼링 레일 물리")]
    [Range(0.08f, 0.30f)] public float minStackedWidthRatio = 0.14f;
    [Tooltip("레일 정지 마찰 (높을수록 놓았을 때 그 자리에 묵직하게 고정)")]
    [Range(0.2f, 4.0f)] public float railStaticFriction = 1.4f;
    [Range(0.3f, 0.95f)] public float railSlideDamping = 0.78f;
    [Tooltip("접힐 때 자연스럽게 생기는 둥근 3D 물결 주름 깊이 (m)")]
    [Range(0.02f, 0.14f)] public float pleatDepth = 0.065f;
    [Range(3f, 8f)] public float pleatWaves = 4.5f;

    [Header("자연 바람 (기본 0 = 정적·고요함)")]
    [Range(0f, 0.25f)] public float windStrength = 0.0f;
    public float windSpeed = 0.5f;

    [Header("핀치 잡기 & 충돌 설정")]
    public float grabPatchRadius = 0.28f;
    [Range(0f, 1f)] public float railPullAssist = 0.75f;
    public float floorY = 0.015f;
    public float handColliderRadius = 0.08f;
    public float bodyColliderRadius = 0.18f;

    public bool SimulationActive { get; private set; }
    public float OpenProgress { get; private set; }
    public float CurrentRightEdgeX => _initialized ? _pos[_rows * _stride + _cols].x : transform.position.x;
    public float LeftAnchorX => _rodMinX;
    public float InitialRightEdgeX => _rodMaxX;

    private struct DistanceConstraint
    {
        public int a;
        public int b;
        public float restLength;
        public float compliance;
        public bool stretchOnly;
    }

    private struct GrabSlot
    {
        public bool active;
        public Vector3 handPos;
        public Vector3 smoothedHandPos;
        public int centerVertex;
        public int centerCol;
        public int[] vertices;
        public float[] weights;
        public Vector3[] offsets;
        public int count;
        public float lastRailX;
        public float hapticAccum;
    }

    private ProceduralCurtainMesh _pcm;
    private Mesh _mesh;
    private int _cols;
    private int _rows;
    private int _stride;
    private int _vertCount;

    private Vector3[] _pos;
    private Vector3[] _prevPos;
    private Vector3[] _vel;
    private Vector3[] _smoothScratch;
    private Vector3[] _localPosBuffer;
    private Vector3[] _localNormalBuffer;
    private float[] _invMass;
    private float[] _pleatSign;
    private float[] _rodYPerCol;
    private float[] _prevRailX;

    private DistanceConstraint[] _constraints;
    private int _constraintCount;

    private float _rodMinX;
    private float _rodMaxX;
    private float _rodZ;
    private float _restRingSpacing;
    private float _minRingSpacing;

    private bool _initialized;
    private GrabSlot[] _grabs;

    private bool _hasLeftHandCol;
    private Vector3 _leftHandColPos;
    private bool _hasRightHandCol;
    private Vector3 _rightHandColPos;
    private bool _hasBodyCol;
    private Vector3 _bodyTopPos;
    private Vector3 _bodyBotPos;

    private bool _editorAutoPulling;

    private void Awake()
    {
        _pcm = GetComponent<ProceduralCurtainMesh>();
        EnsureGrabSlots();
    }

    private void EnsureGrabSlots()
    {
        if (_grabs != null && _grabs.Length == 3 && _grabs[0].vertices != null) return;
        _grabs = new GrabSlot[3];
        for (int i = 0; i < _grabs.Length; i++)
        {
            _grabs[i].vertices = new int[96];
            _grabs[i].weights = new float[96];
            _grabs[i].offsets = new Vector3[96];
        }
    }

    private void Start()
    {
        InitializeCloth();
    }

    public void InitializeCloth()
    {
        EnsureGrabSlots();
        if (_pcm == null) _pcm = GetComponent<ProceduralCurtainMesh>();
        if (_pcm == null) return;

        if (_pcm.RuntimeMesh == null) _pcm.Rebuild();
        _mesh = _pcm.RuntimeMesh;
        if (_mesh == null) return;

        _cols = _pcm.Columns;
        _rows = _pcm.Rows;
        _stride = _cols + 1;
        _vertCount = (_cols + 1) * (_rows + 1);

        Vector3[] srcVerts = _mesh.vertices;
        if (srcVerts == null || srcVerts.Length != _vertCount)
        {
            _pcm.Rebuild();
            _mesh = _pcm.RuntimeMesh;
            srcVerts = _mesh.vertices;
        }

        _pos = new Vector3[_vertCount];
        _prevPos = new Vector3[_vertCount];
        _vel = new Vector3[_vertCount];
        _smoothScratch = new Vector3[_vertCount];
        _localPosBuffer = new Vector3[_vertCount];
        _localNormalBuffer = new Vector3[_vertCount];
        _invMass = new float[_vertCount];
        _pleatSign = new float[_stride];
        _rodYPerCol = new float[_stride];
        _prevRailX = new float[_stride];

        // 1) 초기 월드 좌표를 읽고, 상단 링 행의 초기 높이를 보존해 프레임 0 응력을 0mm로 유지
        for (int i = 0; i < _vertCount; i++)
        {
            Vector3 wp = transform.TransformPoint(srcVerts[i]);
            _pos[i] = wp;
            _prevPos[i] = wp;
            _vel[i] = Vector3.zero;
            _localPosBuffer[i] = srcVerts[i];
        }

        int topRowStart = _rows * _stride;
        _rodMinX = _pos[topRowStart].x;
        _rodMaxX = _pos[topRowStart + _cols].x;
        if (_rodMaxX < _rodMinX)
        {
            float tmp = _rodMinX;
            _rodMinX = _rodMaxX;
            _rodMaxX = tmp;
        }
        _rodZ = transform.position.z;

        // 상단 행 Z를 정확히 봉 선(_rodZ)에 정렬한 뒤 rest length를 계산 -> 초기 튐·떨림 완전 제거!
        for (int c = 0; c <= _cols; c++)
        {
            int idx = topRowStart + c;
            _pos[idx].z = _rodZ;
            _prevPos[idx].z = _rodZ;
            _rodYPerCol[c] = _pos[idx].y;
            _prevRailX[c] = _pos[idx].x;

            float u = (float)c / _cols;
            _pleatSign[c] = Mathf.Sin(u * pleatWaves * Mathf.PI * 2f);
        }

        float totalWidth = Mathf.Max(0.1f, _rodMaxX - _rodMinX);
        _restRingSpacing = totalWidth / _cols;
        _minRingSpacing = (totalWidth * minStackedWidthRatio) / _cols;

        // 질량 분포: 상단 좌측 끝 고정(0), 상단 링 묵직함(0.35), 아랫단 무게추(0.60) -> 안정적 수직 드레이프
        for (int r = 0; r <= _rows; r++)
        {
            float v = (float)r / _rows; // 0 = bottom, 1 = top
            for (int c = 0; c <= _cols; c++)
            {
                int idx = r * _stride + c;
                if (r == _rows)
                {
                    _invMass[idx] = (c == 0) ? 0f : 0.35f;
                }
                else
                {
                    _invMass[idx] = Mathf.Lerp(0.60f, 1.0f, v);
                }
            }
        }

        BuildConstraints();
        _mesh.MarkDynamic();
        OpenProgress = 0f;
        _initialized = true;
    }

    private void BuildConstraints()
    {
        int maxCount = _vertCount * 6;
        _constraints = new DistanceConstraint[maxCount];
        _constraintCount = 0;

        // 1. 세로 구조 제약 (중력에 의한 늘어짐 방지)
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c <= _cols; c++)
            {
                AddConstraint(r * _stride + c, (r + 1) * _stride + c, verticalCompliance, stretchOnly: false);
            }
        }

        // 2. 가로 구조 제약 (상단 레일 링은 stretchOnly=true -> 모일 때 탄성 반발 0)
        for (int r = 0; r <= _rows; r++)
        {
            bool isTopRail = (r == _rows);
            for (int c = 0; c < _cols; c++)
            {
                AddConstraint(r * _stride + c, r * _stride + (c + 1), horizontalCompliance, stretchOnly: isTopRail);
            }
        }

        // 3. 대각선 전단 제약 (부드러운 전단 강성 -> 종이 구김 대신 부드러운 사선 드레이프)
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                int bl = r * _stride + c;
                int br = bl + 1;
                int tl = (r + 1) * _stride + c;
                int tr = tl + 1;
                AddConstraint(bl, tr, shearCompliance, stretchOnly: false);
                AddConstraint(br, tl, shearCompliance, stretchOnly: false);
            }
        }

        // 4. 2-hop 굽힘 제약 (둥글고 묵직한 주름 유지; 상단 행 제외)
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols - 1; c++)
            {
                AddConstraint(r * _stride + c, r * _stride + (c + 2), bendCompliance, stretchOnly: true);
            }
        }
        for (int r = 0; r < _rows - 1; r++)
        {
            for (int c = 0; c <= _cols; c++)
            {
                AddConstraint(r * _stride + c, (r + 2) * _stride + c, bendCompliance * 0.7f, stretchOnly: false);
            }
        }
    }

    private void AddConstraint(int a, int b, float compliance, bool stretchOnly)
    {
        if (_constraintCount >= _constraints.Length) return;
        float dist = Vector3.Distance(_pos[a], _pos[b]);
        _constraints[_constraintCount++] = new DistanceConstraint
        {
            a = a,
            b = b,
            restLength = Mathf.Max(0.005f, dist),
            compliance = compliance,
            stretchOnly = stretchOnly
        };
    }

    public void SetSimulationActive(bool active)
    {
        if (!_initialized) InitializeCloth();
        SimulationActive = active;
        if (_pcm != null)
        {
            _pcm.SetShaderWindAmplitude(active ? 0f : 0.012f);
        }
    }

    public void StartEditorPhysicalAutoOpen()
    {
        if (!SimulationActive) SetSimulationActive(true);
        _editorAutoPulling = true;
    }

    public void SetExternalColliders(
        bool hasLeftHand, Vector3 leftHandPos,
        bool hasRightHand, Vector3 rightHandPos,
        bool hasBody, Vector3 headWorldPos)
    {
        _hasLeftHandCol = hasLeftHand;
        _leftHandColPos = leftHandPos;
        _hasRightHandCol = hasRightHand;
        _rightHandColPos = rightHandPos;
        _hasBodyCol = hasBody;
        if (hasBody)
        {
            _bodyTopPos = new Vector3(headWorldPos.x, Mathf.Max(floorY + 0.25f, headWorldPos.y - 0.05f), headWorldPos.z);
            _bodyBotPos = new Vector3(headWorldPos.x, floorY + 0.12f, headWorldPos.z);
        }
    }

    public bool FindClosestVertex(Vector3 worldPos, out int closestIndex, out float closestDist)
    {
        closestIndex = -1;
        closestDist = float.MaxValue;
        if (!_initialized) return false;

        float bestSqr = float.MaxValue;
        for (int i = 0; i < _vertCount; i++)
        {
            float d2 = (_pos[i] - worldPos).sqrMagnitude;
            if (d2 < bestSqr)
            {
                bestSqr = d2;
                closestIndex = i;
            }
        }
        closestDist = Mathf.Sqrt(bestSqr);
        return closestIndex >= 0;
    }

    public bool FindClosestVertexToRay(Ray ray, float maxRayDist, out int closestIndex, out Vector3 hitPoint)
    {
        closestIndex = -1;
        hitPoint = Vector3.zero;
        if (!_initialized) return false;

        float bestDistToRay = maxRayDist;
        for (int i = 0; i < _vertCount; i++)
        {
            Vector3 toPt = _pos[i] - ray.origin;
            float proj = Vector3.Dot(toPt, ray.direction);
            if (proj <= 0.05f || proj > 8f) continue;
            Vector3 closestOnRay = ray.origin + ray.direction * proj;
            float dist = Vector3.Distance(_pos[i], closestOnRay);
            if (dist < bestDistToRay)
            {
                bestDistToRay = dist;
                closestIndex = i;
                hitPoint = _pos[i];
            }
        }
        return closestIndex >= 0;
    }

    public bool TryBeginGrab(int slotIndex, Vector3 worldHandPos, float maxGrabRadius)
    {
        if (!_initialized || slotIndex < 0 || slotIndex >= _grabs.Length) return false;
        if (!FindClosestVertex(worldHandPos, out int centerIdx, out float dist)) return false;
        if (dist > maxGrabRadius) return false;

        BeginGrabAtVertex(slotIndex, centerIdx, worldHandPos);
        return true;
    }

    public void BeginGrabAtVertex(int slotIndex, int centerIdx, Vector3 worldHandPos)
    {
        if (!_initialized || slotIndex < 0 || slotIndex >= _grabs.Length) return;
        if (centerIdx < 0 || centerIdx >= _vertCount) return;

        ref GrabSlot slot = ref _grabs[slotIndex];
        slot.active = true;
        slot.handPos = worldHandPos;
        slot.smoothedHandPos = worldHandPos;
        slot.centerVertex = centerIdx;
        slot.centerCol = centerIdx % _stride;
        slot.count = 0;
        slot.lastRailX = _pos[_rows * _stride + slot.centerCol].x;
        slot.hapticAccum = 0f;

        Vector3 centerPos = _pos[centerIdx];
        float sigma = Mathf.Max(0.06f, grabPatchRadius * 0.55f);
        float invTwoSigmaSq = 1f / (2f * sigma * sigma);
        float maxRadSq = grabPatchRadius * grabPatchRadius;

        for (int i = 0; i < _vertCount && slot.count < slot.vertices.Length; i++)
        {
            if (_invMass[i] <= 0f) continue;
            float d2 = (_pos[i] - centerPos).sqrMagnitude;
            if (d2 <= maxRadSq)
            {
                float w = Mathf.Exp(-d2 * invTwoSigmaSq);
                if (w < 0.06f) continue;
                int k = slot.count++;
                slot.vertices[k] = i;
                slot.weights[k] = w;
                slot.offsets[k] = _pos[i] - worldHandPos;
            }
        }

        if (slot.count == 0 && _invMass[centerIdx] > 0f)
        {
            slot.vertices[0] = centerIdx;
            slot.weights[0] = 1f;
            slot.offsets[0] = _pos[centerIdx] - worldHandPos;
            slot.count = 1;
        }
    }

    public float UpdateGrab(int slotIndex, Vector3 worldHandPos)
    {
        if (slotIndex < 0 || slotIndex >= _grabs.Length) return 0f;
        ref GrabSlot slot = ref _grabs[slotIndex];
        if (!slot.active) return 0f;

        slot.handPos = worldHandPos;
        float currentRailX = _pos[_rows * _stride + slot.centerCol].x;
        float moved = Mathf.Abs(currentRailX - slot.lastRailX);
        slot.lastRailX = currentRailX;
        slot.hapticAccum += moved;

        if (slot.hapticAccum >= 0.045f)
        {
            float pulse = slot.hapticAccum;
            slot.hapticAccum = 0f;
            return pulse;
        }
        return 0f;
    }

    public void EndGrab(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _grabs.Length) return;
        _grabs[slotIndex].active = false;
        _grabs[slotIndex].count = 0;
    }

    public bool IsSlotGrabbing(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < _grabs.Length && _grabs[slotIndex].active;
    }

    private void Update()
    {
        if (!_initialized || !SimulationActive) return;

        float dt = Mathf.Min(Time.deltaTime, 1f / 50f);
        if (dt <= 1e-5f) return;

        StepSimulation(dt);
        UpdateMeshAndNormals();
        UpdateOpenProgress();
    }

    private void StepSimulation(float dt)
    {
        int steps = Mathf.Max(1, substeps);
        float subDt = dt / steps;
        float subDtSq = subDt * subDt;
        float velDamp = Mathf.Exp(-clothDamping * subDt);
        float sleepSq = sleepSpeed * sleepSpeed;
        float maxSpdSq = maxVertexSpeed * maxVertexSpeed;
        float timeNow = Time.time;

        // Smooth grab targets gently so fast hand micro-jitter never whips the cloth
        float handFollow = 1f - Mathf.Exp(-28f * dt);
        for (int g = 0; g < _grabs.Length; g++)
        {
            if (_grabs[g].active)
            {
                _grabs[g].smoothedHandPos = Vector3.Lerp(_grabs[g].smoothedHandPos, _grabs[g].handPos, handFollow);
            }
        }

        for (int s = 0; s < steps; s++)
        {
            for (int c = 0; c <= _cols; c++)
            {
                _prevRailX[c] = _pos[_rows * _stride + c].x;
            }

            // 1. Explicit-Velocity XPBD Prediction
            for (int r = 0; r <= _rows; r++)
            {
                float vNorm = (float)r / _rows;
                float windWeight = (1f - vNorm) * (1f - vNorm * 0.5f);

                for (int c = 0; c <= _cols; c++)
                {
                    int i = r * _stride + c;
                    _prevPos[i] = _pos[i];
                    if (_invMass[i] <= 0f) continue;

                    Vector3 v = _vel[i];
                    if (r < _rows)
                    {
                        v.y += gravity * subDt;
                        if (windStrength > 0.0001f)
                        {
                            float phase = timeNow * windSpeed + _pos[i].x * 1.8f;
                            v.z += Mathf.Sin(phase) * windStrength * windWeight * subDt;
                        }
                    }

                    v *= velDamp;
                    float spdSq = v.sqrMagnitude;
                    if (spdSq > maxSpdSq) v *= (maxVertexSpeed / Mathf.Sqrt(spdSq));

                    _pos[i] += v * subDt;
                }
            }

            if (_editorAutoPulling)
            {
                ApplyEditorAutoPull(subDt);
            }

            // 2. Apply soft 3D grab constraints
            ApplyGrabConstraints(subDt, isFirstPass: true);

            // 3. Solve XPBD constraints + top rail + smooth pleat buckling
            for (int iter = 0; iter < constraintIterations; iter++)
            {
                SolveDistanceConstraints(subDtSq);
                SolveTopRailAndPleatBuckling(subDt);
                ApplyGrabConstraints(subDt, isFirstPass: false);
            }

            // 4. Resolve floor & hand/body collisions
            ResolveCollisions();

            // 5. Explicit velocity update from projected positions + heavy damping + sleep cutoff (zero jitter!)
            float invSubDt = 1f / subDt;
            for (int i = 0; i < _vertCount; i++)
            {
                if (_invMass[i] <= 0f)
                {
                    _vel[i] = Vector3.zero;
                    continue;
                }

                Vector3 newVel = (_pos[i] - _prevPos[i]) * invSubDt;
                // Damp out constraint-injected high-frequency oscillation
                newVel *= velDamp;

                float spdSq = newVel.sqrMagnitude;
                if (spdSq < sleepSq)
                {
                    // Micro-jitter sleep: dead-calm heavy fabric when untouched
                    newVel = Vector3.zero;
                    _pos[i] = Vector3.Lerp(_prevPos[i], _pos[i], 0.35f);
                }
                else if (spdSq > maxSpdSq)
                {
                    newVel *= (maxVertexSpeed / Mathf.Sqrt(spdSq));
                }

                _vel[i] = newVel;
            }
        }
    }

    private void ApplyFoldSmoothing()
    {
        // Copy current positions to scratch
        Array.Copy(_pos, _smoothScratch, _vertCount);

        float alpha = foldSmoothStrength;
        // Smooth only horizontal/Z curvature on body rows (r = 0.._rows-1, c = 1.._cols-1)
        // so vertical length stays 100% taut while sharp paper creases melt into silky rounded waves!
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 1; c < _cols; c++)
            {
                int idx = r * _stride + c;
                Vector3 left = _smoothScratch[idx - 1];
                Vector3 right = _smoothScratch[idx + 1];
                Vector3 up = _smoothScratch[(r + 1) * _stride + c];
                Vector3 down = r > 0 ? _smoothScratch[(r - 1) * _stride + c] : _smoothScratch[idx];

                // Weighted neighbor average (primarily smoothing Z & X kinks, preserving Y hang)
                Vector3 avg = (left + right) * 0.35f + (up + down) * 0.15f;
                _pos[idx].x = Mathf.Lerp(_smoothScratch[idx].x, avg.x, alpha * 0.45f);
                _pos[idx].z = Mathf.Lerp(_smoothScratch[idx].z, avg.z, alpha);
            }
        }
    }

    private void ApplyEditorAutoPull(float subDt)
    {
        float targetX = _rodMinX + _minRingSpacing * _cols;
        int rightTop = _rows * _stride + _cols;
        if (_pos[rightTop].x <= targetX + 0.03f)
        {
            _editorAutoPulling = false;
            return;
        }
        float pullStep = 0.85f * subDt;
        for (int c = Mathf.Max(1, _cols - 6); c <= _cols; c++)
        {
            int topIdx = _rows * _stride + c;
            _pos[topIdx].x -= pullStep;
            int midIdx = (_rows / 2) * _stride + c;
            _pos[midIdx].x -= pullStep * 0.7f;
            _pos[midIdx].z -= pullStep * 0.12f;
        }
    }

    private void ApplyGrabConstraints(float subDt, bool isFirstPass)
    {
        float blendStrength = isFirstPass ? 0.72f : 0.38f;

        for (int g = 0; g < _grabs.Length; g++)
        {
            ref GrabSlot slot = ref _grabs[g];
            if (!slot.active || slot.count == 0) continue;

            Vector3 clampedHand = slot.smoothedHandPos;
            clampedHand.x = Mathf.Clamp(clampedHand.x, _rodMinX - 0.12f, _rodMaxX + 0.20f);
            clampedHand.y = Mathf.Clamp(clampedHand.y, floorY + 0.05f, _rodYPerCol[slot.centerCol] + 0.08f);
            clampedHand.z = Mathf.Clamp(clampedHand.z, _rodZ - 0.55f, _rodZ + 0.55f);

            for (int k = 0; k < slot.count; k++)
            {
                int idx = slot.vertices[k];
                float w = slot.weights[k];
                Vector3 desired = clampedHand + slot.offsets[k];
                desired.y = Mathf.Max(floorY + 0.02f, desired.y);
                _pos[idx] = Vector3.Lerp(_pos[idx], desired, Mathf.Clamp01(w * blendStrength));
            }

            if (isFirstPass && railPullAssist > 0.001f)
            {
                int centerTopIdx = _rows * _stride + slot.centerCol;
                float dx = (clampedHand.x + slot.offsets[0].x) - _pos[centerTopIdx].x;
                float assist = dx * railPullAssist * Mathf.Clamp01(subDt * 24f);

                int leftStart = Mathf.Max(1, slot.centerCol - 4);
                int rightEnd = dx < 0f ? _cols : Mathf.Min(_cols, slot.centerCol + 4);
                for (int col = leftStart; col <= rightEnd; col++)
                {
                    float falloff;
                    if (col < slot.centerCol)
                    {
                        falloff = 1f - (slot.centerCol - col) / 5f;
                    }
                    else if (dx < 0f)
                    {
                        falloff = Mathf.Lerp(1f, 0.80f, (col - slot.centerCol) / Mathf.Max(1f, _cols - slot.centerCol));
                    }
                    else
                    {
                        falloff = 1f - (col - slot.centerCol) / 5f;
                    }
                    int rIdx = _rows * _stride + col;
                    _pos[rIdx].x += assist * falloff;
                }
            }
        }
    }

    private void SolveDistanceConstraints(float subDtSq)
    {
        for (int i = 0; i < _constraintCount; i++)
        {
            DistanceConstraint c = _constraints[i];
            float wA = _invMass[c.a];
            float wB = _invMass[c.b];
            float wSum = wA + wB;
            if (wSum <= 0f) continue;

            Vector3 diff = _pos[c.b] - _pos[c.a];
            float dist = diff.magnitude;
            if (dist < 1e-5f) continue;

            float error = dist - c.restLength;
            if (c.stretchOnly && error <= 0f) continue;

            float alpha = c.compliance / subDtSq;
            float lambda = error / (wSum + alpha);
            Vector3 corr = (diff / dist) * lambda;

            if (wA > 0f) _pos[c.a] += corr * wA;
            if (wB > 0f) _pos[c.b] -= corr * wB;
        }
    }

    private void SolveTopRailAndPleatBuckling(float subDt)
    {
        int topRowStart = _rows * _stride;

        // 1. 좌측 끝 고정 앵커
        _pos[topRowStart] = new Vector3(_rodMinX, _rodYPerCol[0], _rodZ);

        // 2. 상단 링 1D 레일 구속 + 묵직한 정지 마찰
        float staticThreshold = railStaticFriction * 0.0018f * (subDt * 280f);
        for (int c = 1; c <= _cols; c++)
        {
            int idx = topRowStart + c;
            float targetX = Mathf.Clamp(_pos[idx].x, _rodMinX + _minRingSpacing * c, _rodMaxX);
            float dx = targetX - _prevRailX[c];

            if (Mathf.Abs(dx) < staticThreshold)
            {
                targetX = _prevRailX[c];
            }
            else
            {
                targetX = _prevRailX[c] + dx * railSlideDamping;
            }

            _pos[idx] = new Vector3(targetX, _rodYPerCol[c], _rodZ);
        }

        // 3. 링 간격 비교차 적층(stacking)
        for (int c = 1; c <= _cols; c++)
        {
            int idx = topRowStart + c;
            float minX = _pos[topRowStart + c - 1].x + _minRingSpacing;
            if (_pos[idx].x < minX) _pos[idx].x = minX;
        }
        for (int c = _cols - 1; c >= 1; c--)
        {
            int idx = topRowStart + c;
            float maxX = _pos[topRowStart + c + 1].x - _minRingSpacing;
            if (_pos[idx].x > maxX) _pos[idx].x = maxX;
            float minX = _rodMinX + _minRingSpacing * c;
            if (_pos[idx].x < minX) _pos[idx].x = minX;
        }
        if (_pos[topRowStart + _cols].x > _rodMaxX) _pos[topRowStart + _cols].x = _rodMaxX;

        // 4. 부드러운 둥근 아코디언 주름 유도 (압축된 구간만 은은하게 ±Z 웨이브)
        for (int c = 1; c < _cols; c++)
        {
            float span = (_pos[topRowStart + c + 1].x - _pos[topRowStart + c - 1].x) * 0.5f;
            float compression = Mathf.Clamp01(1f - span / Mathf.Max(0.005f, _restRingSpacing));
            if (compression < 0.08f) continue;

            float targetZOffset = _pleatSign[c] * pleatDepth * compression;
            _pos[topRowStart + c].z = _rodZ + targetZOffset * 0.22f;

            for (int r = 0; r < _rows; r++)
            {
                int idx = r * _stride + c;
                float vNorm = (float)r / _rows;
                float bodyWeight = Mathf.Lerp(0.85f, 0.30f, vNorm);
                float desiredZ = _rodZ + targetZOffset * bodyWeight;
                _pos[idx].z = Mathf.Lerp(_pos[idx].z, desiredZ, compression * 0.035f);
            }
        }
    }

    private void ResolveCollisions()
    {
        bool leftActive = _hasLeftHandCol && !IsSlotGrabbing(0);
        bool rightActive = _hasRightHandCol && !IsSlotGrabbing(1);
        float handRadSq = handColliderRadius * handColliderRadius;
        float bodyRadSq = bodyColliderRadius * bodyColliderRadius;

        for (int i = 0; i < _vertCount; i++)
        {
            if (_invMass[i] <= 0f) continue;

            Vector3 p = _pos[i];

            if (p.y < floorY)
            {
                p.y = floorY;
                _vel[i].y = Mathf.Max(0f, _vel[i].y);
                _vel[i].x *= 0.7f;
                _vel[i].z *= 0.7f;
            }

            if (leftActive)
            {
                Vector3 d = p - _leftHandColPos;
                float d2 = d.sqrMagnitude;
                if (d2 < handRadSq && d2 > 1e-7f)
                {
                    Vector3 pushed = _leftHandColPos + (d / Mathf.Sqrt(d2)) * handColliderRadius;
                    p = Vector3.Lerp(p, pushed, 0.65f);
                }
            }

            if (rightActive)
            {
                Vector3 d = p - _rightHandColPos;
                float d2 = d.sqrMagnitude;
                if (d2 < handRadSq && d2 > 1e-7f)
                {
                    Vector3 pushed = _rightHandColPos + (d / Mathf.Sqrt(d2)) * handColliderRadius;
                    p = Vector3.Lerp(p, pushed, 0.65f);
                }
            }

            if (_hasBodyCol)
            {
                float clampedY = Mathf.Clamp(p.y, _bodyBotPos.y, _bodyTopPos.y);
                Vector3 closestAxis = new Vector3(_bodyTopPos.x, clampedY, _bodyTopPos.z);
                Vector3 d = p - closestAxis;
                float d2 = d.sqrMagnitude;
                if (d2 < bodyRadSq && d2 > 1e-7f)
                {
                    Vector3 pushed = closestAxis + (d / Mathf.Sqrt(d2)) * bodyColliderRadius;
                    p = Vector3.Lerp(p, pushed, 0.65f);
                }
            }

            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
            {
                p = _prevPos[i];
                _vel[i] = Vector3.zero;
            }

            _pos[i] = p;
        }
    }

    private void UpdateMeshAndNormals()
    {
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
        for (int i = 0; i < _vertCount; i++)
        {
            _localPosBuffer[i] = worldToLocal.MultiplyPoint3x4(_pos[i]);
        }

        // Visual-only 1-pass Laplacian fold smoothing on interior vertices:
        // melts any sharp paper-like kinks into silky rounded heavy fabric WITHOUT injecting energy into physics!
        if (foldSmoothStrength > 0.001f)
        {
            Array.Copy(_localPosBuffer, _smoothScratch, _vertCount);
            float alpha = Mathf.Clamp01(foldSmoothStrength * 1.8f);
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 1; c < _cols; c++)
                {
                    int idx = r * _stride + c;
                    Vector3 left = _smoothScratch[idx - 1];
                    Vector3 right = _smoothScratch[idx + 1];
                    Vector3 up = _smoothScratch[(r + 1) * _stride + c];
                    Vector3 down = r > 0 ? _smoothScratch[(r - 1) * _stride + c] : _smoothScratch[idx];
                    Vector3 avg = (left + right) * 0.35f + (up + down) * 0.15f;
                    _localPosBuffer[idx].x = Mathf.Lerp(_smoothScratch[idx].x, avg.x, alpha * 0.4f);
                    _localPosBuffer[idx].z = Mathf.Lerp(_smoothScratch[idx].z, avg.z, alpha);
                }
            }
        }

        for (int r = 0; r <= _rows; r++)
        {
            int rDown = Mathf.Max(0, r - 1);
            int rUp = Mathf.Min(_rows, r + 1);
            for (int c = 0; c <= _cols; c++)
            {
                int cLeft = Mathf.Max(0, c - 1);
                int cRight = Mathf.Min(_cols, c + 1);

                Vector3 dx = _localPosBuffer[r * _stride + cRight] - _localPosBuffer[r * _stride + cLeft];
                Vector3 dy = _localPosBuffer[rUp * _stride + c] - _localPosBuffer[rDown * _stride + c];
                Vector3 n = Vector3.Cross(dy, dx);
                float sqr = n.sqrMagnitude;
                _localNormalBuffer[r * _stride + c] = sqr > 1e-10f ? (n / Mathf.Sqrt(sqr)) : Vector3.back;
            }
        }

        _mesh.vertices = _localPosBuffer;
        _mesh.normals = _localNormalBuffer;
        _mesh.RecalculateBounds();
    }

    private void UpdateOpenProgress()
    {
        float fullTravel = Mathf.Max(0.05f, (_rodMaxX - _rodMinX) * (1f - minStackedWidthRatio));
        float currentRightX = _pos[_rows * _stride + _cols].x;
        float movedLeft = _rodMaxX - currentRightX;
        OpenProgress = Mathf.Clamp01(movedLeft / fullTravel);
    }
}
