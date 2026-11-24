using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VolumetricBeamCone : MonoBehaviour
{
    [SerializeField] private float topRadius = 0.14f;
    [SerializeField] private float bottomRadius = 1.35f;
    [SerializeField] private float height = 3.15f;
    [SerializeField] private int segments = 48;

    private Mesh _mesh;

    private void OnEnable()
    {
        RebuildMesh();
    }

    private void OnValidate()
    {
        RebuildMesh();
    }

    public void RebuildMesh()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null) return;

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "VolumetricBeamConeMesh" };
            _mesh.hideFlags = HideFlags.DontSave;
        }

        int seg = Mathf.Max(12, segments);
        int vertCount = (seg + 1) * 2;
        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var triangles = new int[seg * 6];

        float slopeY = (bottomRadius - topRadius) / Mathf.Max(0.01f, height);

        for (int i = 0; i <= seg; i++)
        {
            float u = (float)i / seg;
            float angle = u * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            Vector3 radial = new Vector3(cos, 0f, sin);
            Vector3 normal = (radial + Vector3.up * slopeY).normalized;

            int topIdx = i * 2;
            int botIdx = topIdx + 1;

            vertices[topIdx] = new Vector3(cos * topRadius, 0f, sin * topRadius);
            vertices[botIdx] = new Vector3(cos * bottomRadius, -height, sin * bottomRadius);

            normals[topIdx] = normal;
            normals[botIdx] = normal;

            uvs[topIdx] = new Vector2(u, 1f);
            uvs[botIdx] = new Vector2(u, 0f);

            if (i < seg)
            {
                int tri = i * 6;
                int nextTop = topIdx + 2;
                int nextBot = topIdx + 3;

                triangles[tri + 0] = topIdx;
                triangles[tri + 1] = nextTop;
                triangles[tri + 2] = botIdx;

                triangles[tri + 3] = nextTop;
                triangles[tri + 4] = nextBot;
                triangles[tri + 5] = botIdx;
            }
        }

        _mesh.Clear();
        _mesh.vertices = vertices;
        _mesh.normals = normals;
        _mesh.uv = uvs;
        _mesh.triangles = triangles;
        _mesh.RecalculateBounds();

        filter.sharedMesh = _mesh;
    }
}
