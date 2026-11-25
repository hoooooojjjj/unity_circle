using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralCurtainMesh : MonoBehaviour
{
    [SerializeField] private float width = 1.65f;
    [SerializeField] private float height = 3.0f;
    [SerializeField] private int columns = 48;
    [SerializeField] private int rows = 24;
    [SerializeField] private float foldAmplitude = 0.065f;
    [SerializeField] private float foldFrequency = 4.5f;
    [SerializeField] private float topSag = 0.14f;
    [SerializeField] private float bottomWave = 0.015f;
    [SerializeField] private float seedOffset = 0.0f;

    [Header("개별 커튼 투명도 (0 = 완전 투명, 1 = 기본)")]
    [Range(0f, 1f)]
    [SerializeField] private float opacity = 1.0f;

    private static readonly int OpacityPropId = Shader.PropertyToID("_Opacity");
    private Mesh _mesh;
    private MaterialPropertyBlock _mpb;

    public float Opacity
    {
        get => opacity;
        set
        {
            opacity = Mathf.Clamp01(value);
            ApplyOpacity();
        }
    }

    private void OnEnable()
    {
        Rebuild();
        ApplyOpacity();
    }

    private void OnValidate()
    {
        Rebuild();
        ApplyOpacity();
    }

    public void ApplyOpacity()
    {
        var mr = GetComponent<MeshRenderer>();
        if (mr == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(_mpb);
        _mpb.SetFloat(OpacityPropId, opacity);
        mr.SetPropertyBlock(_mpb);
    }

    public void Rebuild()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null) return;

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "ProceduralCurtainMesh" };
            _mesh.hideFlags = HideFlags.DontSave;
        }

        int cols = Mathf.Max(8, columns);
        int rws = Mathf.Max(4, rows);
        int vertCount = (cols + 1) * (rws + 1);

        var verts = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var tris = new int[cols * rws * 6];

        for (int r = 0; r <= rws; r++)
        {
            float v = (float)r / rws; // 0 = bottom, 1 = top
            for (int c = 0; c <= cols; c++)
            {
                float u = (float)c / cols; // 0..1 across width
                float centered = u - 0.5f;

                // Natural slight inward taper near top rod, gentle spread toward floor
                float spread = Mathf.Lerp(1.03f, 0.98f, v);
                float x = centered * width * spread;

                // Natural catenary sag along top edge (like draped hemp cloth hung from two corners)
                float catenary = Mathf.Sin(u * Mathf.PI);
                float sag = catenary * topSag * Mathf.Pow(v, 1.3f);

                // Organic slightly uneven bottom hem (like raw woven hemp)
                float hemRipple = (Mathf.Sin((u * 7.3f + seedOffset) * Mathf.PI) * 0.55f
                                 + Mathf.Sin((u * 15.1f + seedOffset * 2.1f) * Mathf.PI) * 0.45f)
                                 * bottomWave * (1.0f - v);

                float y = v * height - sag + hemRipple;

                // Organic irregular vertical drape folds (multi-harmonic, not rigid accordion)
                float foldWeight = Mathf.Lerp(1.0f, 0.25f, v);
                float f1 = Mathf.Sin((u * foldFrequency + seedOffset) * Mathf.PI * 2f);
                float f2 = Mathf.Sin((u * (foldFrequency * 1.73f) + seedOffset * 1.9f + 0.7f) * Mathf.PI * 2f) * 0.52f;
                float f3 = Mathf.Sin((u * (foldFrequency * 3.1f) + seedOffset * 0.6f + 1.4f) * Mathf.PI * 2f) * 0.22f;
                // Gentle vertical billow
                float billow = Mathf.Sin(v * Mathf.PI) * 0.025f * catenary;
                float z = (f1 + f2 + f3) * (foldAmplitude * 0.65f) * foldWeight + billow;

                int idx = r * (cols + 1) + c;
                verts[idx] = new Vector3(x, y, z);
                uvs[idx] = new Vector2(u, v);
            }
        }

        int t = 0;
        for (int r = 0; r < rws; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int bl = r * (cols + 1) + c;
                int br = bl + 1;
                int tl = bl + (cols + 1);
                int tr = tl + 1;

                tris[t++] = bl;
                tris[t++] = tl;
                tris[t++] = br;

                tris[t++] = br;
                tris[t++] = tl;
                tris[t++] = tr;
            }
        }

        _mesh.Clear();
        _mesh.vertices = verts;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        filter.sharedMesh = _mesh;
    }
}
