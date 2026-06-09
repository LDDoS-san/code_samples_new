using UnityEngine;
using System.Collections.Generic;

// 枝メッシュを作る
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BranchMesh : MonoBehaviour
{
    private TreeManager tm;

    [Header("制御点")]
    public Transform[] controlPoints;

    [Header("メッシュ解像度")]
    public int curveResolution = 10;   // 曲線の細かさ
    public int radialSegments = 8;     // 断面の分割数

    [Header("半径設定")]
    public float startRadius = 0.1f;   // 根元の太さ
    public float endRadius = 0.0f;     // 先端の太さ

    private Mesh mesh;

    // Inspector変更時に作り直す
    void OnValidate()
    {
        GenerateTube();
    }

    // Sceneビュー用の更新
    void OnDrawGizmos()
    {
        if (!Application.isPlaying)
        {
            GenerateTube();
        }
    }

    // 管理クラスを取得
    void Start()
    {
        tm = FindObjectOfType<TreeManager>();
    }

    // 枝メッシュを生成
    public void GenerateTube()
    {
        if (controlPoints == null || controlPoints.Length < 2) return;

        List<Vector3> curvePoints = new List<Vector3>();

        // Catmull-Romで制御点をなめらかにつなぐ
        for (int i = 0; i < controlPoints.Length - 1; i++)
        {
            Vector3 p0 = (i == 0) ? controlPoints[i].position : controlPoints[i - 1].position;
            Vector3 p1 = controlPoints[i].position;
            Vector3 p2 = controlPoints[i + 1].position;
            Vector3 p3 = (i + 2 < controlPoints.Length) ? controlPoints[i + 2].position : controlPoints[i + 1].position;

            for (int j = 0; j < curveResolution; j++)
            {
                float t = j / (float)curveResolution;
                curvePoints.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
        curvePoints.Add(controlPoints[controlPoints.Length - 1].position);

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        Vector3 prevUp = Vector3.up;

        for (int i = 0; i < curvePoints.Count; i++)
        {
            float tGlobal = i / (float)(curvePoints.Count - 1);

            // smoothstepで先端だけ自然に細くする
            float radius = startRadius;
            int lastSegStart = (controlPoints.Length - 2) * curveResolution;
            if (i >= lastSegStart)
            {
                float localT = (i - lastSegStart) / (float)(curvePoints.Count - 1 - lastSegStart);
                float easedT = localT * localT * (3 - 2 * localT);
                radius = Mathf.Lerp(startRadius, endRadius, easedT);
            }

            // 前回のupを使って断面のねじれを抑える
            Vector3 forward;
            if (i < curvePoints.Count - 1)
                forward = (curvePoints[i + 1] - curvePoints[i]).normalized;
            else
                forward = (curvePoints[i] - curvePoints[i - 1]).normalized;

            Vector3 right = Vector3.Cross(prevUp, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            prevUp = up;

            // 断面リングを作る
            for (int j = 0; j < radialSegments; j++)
            {
                float angle = (j / (float)radialSegments) * Mathf.PI * 2f;
                Vector3 offset = (Mathf.Cos(angle) * right + Mathf.Sin(angle) * up) * radius;
                vertices.Add(curvePoints[i] + offset);

                float u = (float)j / radialSegments;
                float v = tGlobal;
                uvs.Add(new Vector2(u, v));
            }
        }

        // 側面をつなぐ
        for (int i = 0; i < curvePoints.Count - 1; i++)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                int current = i * radialSegments + j;
                int next = i * radialSegments + (j + 1) % radialSegments;
                int currentUp = (i + 1) * radialSegments + j;
                int nextUp = (i + 1) * radialSegments + (j + 1) % radialSegments;

                triangles.Add(current);
                triangles.Add(nextUp);
                triangles.Add(currentUp);

                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(nextUp);
            }
        }

        // 根元をふさぐ
        int startCenterIndex = vertices.Count;
        vertices.Add(curvePoints[0]);
        uvs.Add(new Vector2(0, 0));

        for (int j = 0; j < radialSegments; j++)
        {
            int current = j;
            int next = (j + 1) % radialSegments;
            triangles.Add(startCenterIndex);
            triangles.Add(next);
            triangles.Add(current);
        }

        // 先端をふさぐ
        int endCenterIndex = vertices.Count;
        vertices.Add(curvePoints[curvePoints.Count - 1]);
        uvs.Add(new Vector2(1, 1));

        int ringStart = (curvePoints.Count - 1) * radialSegments;
        for (int j = 0; j < radialSegments; j++)
        {
            int current = ringStart + j;
            int next = ringStart + (j + 1) % radialSegments;
            triangles.Add(endCenterIndex);
            triangles.Add(current);
            triangles.Add(next);
        }

        // Meshを使い回して生成コストを抑える
        if (mesh == null)
        {
            mesh = new Mesh();
            GetComponent<MeshFilter>().mesh = mesh;
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();

        // Raycast用にColliderも同じ形へ更新する
        var collider = GetComponent<MeshCollider>();
        if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
    }

    // Catmull-Romの位置計算
    Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t
        );
    }

    // 曲線上の位置を返す
    public Vector3 GetPointOnCurve(float t)
    {
        if (controlPoints == null || controlPoints.Length < 2)
            return Vector3.zero;

        // 全体tを区間番号と区間内tに分ける
        int segCount = controlPoints.Length - 1;
        float fIndex = t * segCount;
        int i = Mathf.Clamp(Mathf.FloorToInt(fIndex), 0, segCount - 1);
        float localT = fIndex - i;

        Vector3 p0 = (i == 0) ? controlPoints[i].position : controlPoints[i - 1].position;
        Vector3 p1 = controlPoints[i].position;
        Vector3 p2 = controlPoints[i + 1].position;
        Vector3 p3 = (i + 2 < controlPoints.Length) ? controlPoints[i + 2].position : controlPoints[i + 1].position;

        return CatmullRom(p0, p1, p2, p3, localT);
    }

    // 曲線上の向きを返す
    public Vector3 GetTangentOnCurve(float t)
    {
        if (controlPoints == null || controlPoints.Length < 2)
            return Vector3.forward;

        // 位置計算と同じ区間で接線を出す
        int segCount = controlPoints.Length - 1;
        float fIndex = t * segCount;
        int i = Mathf.Clamp(Mathf.FloorToInt(fIndex), 0, segCount - 1);
        float localT = fIndex - i;

        Vector3 p0 = (i == 0) ? controlPoints[i].position : controlPoints[i - 1].position;
        Vector3 p1 = controlPoints[i].position;
        Vector3 p2 = controlPoints[i + 1].position;
        Vector3 p3 = (i + 2 < controlPoints.Length) ? controlPoints[i + 2].position : controlPoints[i + 1].position;

        return CatmullRomTangent(p0, p1, p2, p3, localT).normalized;
    }

    // Catmull-Romの接線計算
    private Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        return 0.5f * (
            (-p0 + p2) +
            2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t +
            3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t * t
        );
    }
}
