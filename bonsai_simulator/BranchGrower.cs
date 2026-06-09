using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// 枝を伸ばして増やす
public class BranchGrower : MonoBehaviour
{
    private TreeManager tm;

    [Header("幹")]
    public GameObject trunkObj;
    private BranchMesh trunk;

    [Header("親枝に対する新枝の比率")]
    [Range(0.05f, 0.95f)] public float branchRadiusFactor = 0.5f; // 太さ倍率
    [Range(0.05f, 0.90f)] public float branchLengthFactor = 0.4f; // 長さ倍率
    public Vector2 radiusRandomRange = new Vector2(0.9f, 1.1f);    // 太さの揺れ
    public Vector2 lengthRandomRange = new Vector2(0.8f, 1.2f);    // 長さの揺れ

    [Header("枝設定")]
    public int controlPointsPerBranch = 4;
    public float branchLength = 2f;
    public float branchRandomness = 0.5f;
    [Range(0f, 1f)]
    public float upwardBias = 0.5f;

    [Header("生成した枝")]
    public List<BranchMesh> branches = new List<BranchMesh>();

    [Header("枝用マテリアル")]
    public Material branchMaterial;

    List<float> usedT = new List<float>();
    float minSpacing = 0.1f;
    float goldenAngle = 137.5f;

    [Header("葉っぱ設定")]
    public GameObject leafPrefab;
    public int leavesPerBranch = 3;
    public Vector2 leafOffsetRange = new Vector2(0.7f, 0.98f); // 葉を付ける範囲

    // 初期化
    void Start()
    {
        tm = FindObjectOfType<TreeManager>();

        trunk = trunkObj.GetComponent<BranchMesh>();
        CreateLeaf(trunkObj, trunk, new Vector2(0.5f, 0.95f), leavesPerBranch);
    }

    // 日付が進んだら成長
    void Update()
    {
        if (MoneyManager.Instance.NextDay)
        {
            MoneyManager.Instance.NextDay = false;
            if (trunk == null)
            {
                UnityEngine.Debug.LogError("幹のBranchMeshが指定されていません。");
                return;
            }

            // 成長中に枝が増えるのでコピーしてから回す
            List<TreeManager.BranchInfo> currentBranches = new List<TreeManager.BranchInfo>(tm.allBranches);

            foreach (var b in currentBranches)
            {
                GrowBranch(b.tube.gameObject, b.tube, extraLength: 1f, thicknessFactor: 1.1f);
                tm.RecalculateChildT(b);
            }

            foreach (var parentInfo in currentBranches)
            {
                GameObject parentObj = parentInfo.tube.gameObject;
                if (!parentObj) continue;
                GenerateBranches(parentObj, UnityEngine.Random.Range(1, 4));
            }
        }
    }

    // 枝を生成する
    public void GenerateBranches(GameObject mainBrunchObj, int branchCount)
    {
        BranchMesh mainBrunch = mainBrunchObj.GetComponent<BranchMesh>();

        var mainBrunchPoints = mainBrunch.controlPoints;
        if (mainBrunchPoints == null || mainBrunchPoints.Length < 2) return;

        for (int i = 0; i < branchCount; i++)
        {
            float t;
            int attempt = 0;
            do
            {
                t = UnityEngine.Random.Range(0.5f, 0.95f);
                attempt++;
            } while (usedT.Exists(prev => Mathf.Abs(prev - t) < minSpacing) && attempt < 20);
            usedT.Add(t);

            // 生える位置
            Vector3 basePos = mainBrunch.GetPointOnCurve(t);
            Vector3 tangent = mainBrunch.GetTangentOnCurve(t);

            Vector3 upOnPlane = Vector3.ProjectOnPlane(Vector3.up, tangent);
            if (upOnPlane.sqrMagnitude < 1e-6f)
                upOnPlane = Vector3.ProjectOnPlane(Vector3.right, tangent);
            upOnPlane.Normalize();

            // 黄金角で分岐方向を散らす
            float angle = goldenAngle * i;
            Vector3 rotatedDir = Quaternion.AngleAxis(angle, tangent) * upOnPlane;

            // upwardBiasで上向きに寄せる
            Vector3 direction = Vector3.Slerp(rotatedDir, Vector3.up, Mathf.Clamp01(upwardBias)).normalized;

            if (Vector3.Dot(direction, Vector3.up) < 0f)
                direction = -direction;

            GameObject branchObj = new GameObject("Branch_" + i);
            branchObj.layer = LayerMask.NameToLayer("branchLayer");
            branchObj.transform.parent = this.transform;

            var renderer = branchObj.AddComponent<MeshRenderer>();
            if (branchMaterial != null)
                renderer.material = new Material(branchMaterial);

            var col = branchObj.AddComponent<MeshCollider>();
            col.convex = false;

            var branchTube = branchObj.AddComponent<BranchMesh>();
            tm.RegisterBranch(branchObj, mainBrunchObj, t);

            // 親枝より太くならないように太さを決める
            float radiusScaleJitter = Mathf.Clamp01(branchRadiusFactor) *
                                      UnityEngine.Random.Range(radiusRandomRange.x, radiusRandomRange.y);
            float mainBrunchTaperRatio = (mainBrunch.startRadius > 1e-6f) ? (mainBrunch.endRadius / mainBrunch.startRadius) : 0f;
            float branchStartR = Mathf.Max(1e-5f, mainBrunch.startRadius * radiusScaleJitter);
            float branchEndR = Mathf.Max(0f, branchStartR * Mathf.Clamp01(mainBrunchTaperRatio));
            branchStartR = Mathf.Min(branchStartR, Mathf.Max(1e-5f, mainBrunch.startRadius * 0.999f));
            branchEndR = Mathf.Min(branchEndR, Mathf.Max(0f, mainBrunch.endRadius * 0.999f));

            // 親枝より短くなるように長さを決める
            float mainBrunchLen = EstimateCurveLength(mainBrunch, 96);
            float lengthScaleJitter = Mathf.Clamp01(branchLengthFactor) *
                                      Mathf.Clamp(UnityEngine.Random.Range(lengthRandomRange.x, lengthRandomRange.y), 0.01f, 10f);
            float branchLen = Mathf.Max(0.01f, mainBrunchLen * lengthScaleJitter);
            branchLen = Mathf.Min(branchLen, Math.Max(0.01f, mainBrunchLen * 0.95f));

            branchTube.curveResolution = mainBrunch.curveResolution;
            branchTube.radialSegments = mainBrunch.radialSegments;
            branchTube.startRadius = branchStartR;
            branchTube.endRadius = branchEndR;

            Transform[] cps = new Transform[controlPointsPerBranch];

            // 編集用に制御点をSphereで持つ
            Transform CreateCP(string name, Vector3 worldPos)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.layer = LayerMask.NameToLayer("controlPointLayer");
                go.name = name;
                go.transform.parent = branchObj.transform;
                go.transform.position = worldPos;
                go.transform.localScale = Vector3.one * 0.3f;
                var sp = go.GetComponent<SphereCollider>(); if (sp) sp.isTrigger = true;
                var mr = go.GetComponent<MeshRenderer>(); if (mr) mr.enabled = false;
                return go.transform;
            }

            cps[0] = CreateCP("CP_0", basePos);

            float step = branchLen / Mathf.Max(1, (controlPointsPerBranch - 1));
            float liftPerStep = step * 0.25f * Mathf.Clamp01(upwardBias);

            Vector3 current = basePos;
            for (int j = 1; j < controlPointsPerBranch; j++)
            {
                // 接線に垂直な方向へ少し曲げる
                Vector3 randomOnPlane = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, tangent).normalized;
                Vector3 bend = randomOnPlane * (branchRandomness * 0.5f);
                current = current + direction * step + Vector3.up * liftPerStep + bend;

                cps[j] = CreateCP($"CP_{j}", current);
            }

            branchTube.controlPoints = cps;
            branchTube.GenerateTube();

            branches.Add(branchTube);

            CreateLeaf(branchObj, branchTube, leafOffsetRange, UnityEngine.Random.Range(1, 3));
        }
    }

    // 枝の長さを測る
    float EstimateCurveLength(BranchMesh tube, int samples = 64)
    {
        Vector3 prev = tube.GetPointOnCurve(0f);
        float sum = 0f;
        for (int s = 1; s <= samples; s++)
        {
            float tt = s / (float)samples;
            Vector3 p = tube.GetPointOnCurve(tt);
            sum += Vector3.Distance(prev, p);
            prev = p;
        }
        return sum;
    }

    // 葉を作る
    void CreateLeaf(GameObject branchObj, BranchMesh branchTube, Vector2 leafOffsetRange, int x)
    {
        if (leafPrefab != null)
        {
            for (int l = 0; l < x; l++)
            {
                // 葉の位置
                float tLeaf = UnityEngine.Random.Range(leafOffsetRange.x, leafOffsetRange.y);
                Vector3 pos = branchTube.GetPointOnCurve(tLeaf);
                Vector3 tangentDir = branchTube.GetTangentOnCurve(tLeaf);

                // 枝の向きに合わせる
                Quaternion rot = Quaternion.LookRotation(tangentDir, Vector3.up) * Quaternion.Euler(90, 0, 0);

                GameObject leafObj = Instantiate(leafPrefab, pos, rot, branchObj.transform);
                leafObj.layer = LayerMask.NameToLayer("leafLayer");
                tm.RegisterLeaf(leafObj, branchObj, tLeaf);

                // 見た目を少し散らす
                leafObj.transform.Rotate(Vector3.up, UnityEngine.Random.Range(0f, 360f), Space.Self);
                leafObj.transform.Rotate(Vector3.forward, UnityEngine.Random.Range(-20f, 20f), Space.Self);
            }
        }
    }

    // 枝を伸ばす
    public void GrowBranch(GameObject branchObj, BranchMesh branchTube, float extraLength, float thicknessFactor)
    {
        if (branchTube == null || branchTube.controlPoints == null || branchTube.controlPoints.Length < 2)
            return;

        branchTube.startRadius *= thicknessFactor;
        branchTube.endRadius *= thicknessFactor;

        Transform lastCP = branchTube.controlPoints[branchTube.controlPoints.Length - 1];

        // 末端数点の平均で伸びる向きを決める
        int lookBack = Mathf.Min(4, branchTube.controlPoints.Length - 1);
        Vector3 avgDir = Vector3.zero;
        for (int i = 0; i < lookBack; i++)
        {
            Vector3 dirSegment = (branchTube.controlPoints[branchTube.controlPoints.Length - 1 - i].position -
                                  branchTube.controlPoints[branchTube.controlPoints.Length - 2 - i].position).normalized;
            avgDir += dirSegment;
        }
        avgDir.Normalize();

        Vector3 randomBend = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, avgDir) * (extraLength * 0.3f);
        Vector3 newPos = lastCP.position + avgDir * extraLength + randomBend;

        // 追加した制御点も編集対象にする
        GameObject newCP = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        newCP.layer = LayerMask.NameToLayer("controlPointLayer");
        newCP.name = $"CP_{branchTube.controlPoints.Length}";
        newCP.transform.parent = branchTube.transform;
        newCP.transform.position = newPos;
        newCP.transform.localScale = Vector3.one * 0.3f;

        var sp = newCP.GetComponent<SphereCollider>(); if (sp) sp.isTrigger = true;
        var mr = newCP.GetComponent<MeshRenderer>(); if (mr) mr.enabled = false;

        var cps = new List<Transform>(branchTube.controlPoints);
        cps.Add(newCP.transform);
        branchTube.controlPoints = cps.ToArray();

        branchTube.GenerateTube();
        CreateLeaf(branchObj, branchTube, tm.GetEndSegmentT(branchTube), UnityEngine.Random.Range(1, 3));
    }
}
