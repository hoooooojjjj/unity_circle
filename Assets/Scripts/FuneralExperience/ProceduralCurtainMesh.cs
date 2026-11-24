using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralCurtainMesh : MonoBehaviour
{
    [SerializeField] private float width = 1.6f;
    [SerializeField] private float height = 3.0f;
    [SerializeField] private int columns = 36;
    [SerializeField] private int rows = 16;
    [SerializeField] private float foldAmplitude = 0.08f;
    [SerializeField] private float foldFrequency = 6.0f;
    [SerializeField] private float topSag = 0.08f;

    private Mesh _mesh;

    private void OnEnable() => Rebuild();
    private void OnValidate() => Rebuild();

    public void Rebuild()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null) return;

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "ProceduralCurtainMesh" };
            _mesh.hideFlags = HideFlags.DontSave;
        }

        int cols = Mathf.Max(4, columns);
        int rws = Mathf.Max(2, rows);
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
                float x = (u - 0.5f) * width;
                // Gentle catenary sag along top edge
                float sag = Mathf.Sin(u * Mathf.PI) * topSag * v;
                float y = v * height - sag;
                // Pleated curtain folds stronger toward bottom
                float foldWeight = Mathf.Lerp(1.0f, 0.35f, v);
                float z = Mathf.Sin(u * foldFrequency * Mathf.PI * 2f) * foldAmplitude * foldWeight
                        + Mathf.Sin(u * foldFrequency * 2.3f * Mathf.PI * 2f) * (foldAmplitude * 0.25f) * foldWeight;

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
