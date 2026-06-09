using System.Collections.Generic;
using UnityEngine;

// 枝と葉をまとめて管理する
public class TreeManager : MonoBehaviour
{
    [Header("全体リスト")]
    public List<BranchInfo> allBranches = new List<BranchInfo>();
    public List<LeafInfo> allLeaves = new List<LeafInfo>();

    [Header("ルート幹")]
    public BranchInfo rootBranch;

    public GameObject trunkObj;

    public int totalBranchesCreated = 0;
    public int totalLeavesCreated = 0;
    public int totalBranchesDeleted = 0;
    public int totalLeavesDeleted = 0;

    // 大きさスコアの基準
    public float targetSize = 50f;

    // 幹を登録する
    void Start()
    {
        RegisterBranch(trunkObj);
    }

    // スコア確認用
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.N))
        {
            Debug.Log("size = " + GetTreeSizeScore());
            Debug.Log("efrt = " + GetEffortScore());
        }
    }

    // 枝を登録する
    public BranchInfo RegisterBranch(GameObject branchObj, GameObject parentBranchObj = null, float tOnParent = 0f)
    {
        if (branchObj == null)
        {
            UnityEngine.Debug.LogError("RegisterBranch: branchObj is null.");
            return null;
        }

        // 枝の本体を取得
        BranchMesh tube = branchObj.GetComponent<BranchMesh>();
        if (tube == null)
        {
            UnityEngine.Debug.LogError($"RegisterBranch: BranchMesh not found on branchObj ({branchObj.name})");
            return null;
        }

        BranchInfo parent = null;
        if (parentBranchObj != null)
        {
            BranchMesh parentTube = parentBranchObj.GetComponent<BranchMesh>();
            if (parentTube == null)
            {
                UnityEngine.Debug.LogError($"RegisterBranch: BranchMesh not found on parentBranchObj ({parentBranchObj.name})");
                return null;
            }

            // BranchMeshをキーに親データを探す
            parent = allBranches.Find(b => b.tube == parentTube);
            if (parent == null)
            {
                UnityEngine.Debug.LogError($"RegisterBranch: parent BranchInfo not found ({parentBranchObj.name})");
                return null;
            }
        }

        BranchInfo newBranch = new BranchInfo
        {
            tube = tube,
            parentBranch = parent,
            childBranches = new List<BranchInfo>(),
            leaves = new List<LeafInfo>(),
            tOnParent = Mathf.Clamp01(tOnParent)
        };

        if (parent != null)
        {
            parent.childBranches.Add(newBranch);
        }
        else
        {
            rootBranch = newBranch;
        }

        totalBranchesCreated++;

        allBranches.Add(newBranch);
        return newBranch;
    }

    // 葉を登録する
    public LeafInfo RegisterLeaf(GameObject leaf, GameObject parentBranchObj, float tOnBranch)
    {
        if (leaf == null || parentBranchObj == null)
        {
            UnityEngine.Debug.LogError("RegisterLeaf: leaf or parentBranchObj is null.");
            return null;
        }

        // 親枝を探す
        BranchMesh parentTube = parentBranchObj.GetComponent<BranchMesh>();
        if (parentTube == null)
        {
            UnityEngine.Debug.LogError($"RegisterLeaf: BranchMesh not found on parentBranchObj ({parentBranchObj.name})");
            return null;
        }

        BranchInfo parentBranch = allBranches.Find(b => b.tube == parentTube);
        if (parentBranch == null)
        {
            UnityEngine.Debug.LogError($"RegisterLeaf: parent BranchInfo not found ({parentBranchObj.name})");
            return null;
        }

        LeafInfo leafInfo = new LeafInfo
        {
            leafObject = leaf,
            attachedBranch = parentBranch,
            tOnBranch = Mathf.Clamp01(tOnBranch)
        };

        totalLeavesCreated++;

        allLeaves.Add(leafInfo);
        parentBranch.leaves.Add(leafInfo);

        return leafInfo;
    }

    // 葉の位置を合わせる
    public void UpdateLeafPositions()
    {
        foreach (var leaf in allLeaves)
        {
            if (leaf.attachedBranch != null && leaf.attachedBranch.tube != null)
            {
                // 親枝上のtで追従させる
                Vector3 pos = leaf.attachedBranch.tube.GetPointOnCurve(leaf.tOnBranch);
                leaf.leafObject.transform.position = pos;
            }
        }
    }

    // 子枝の位置を合わせる
    public void UpdateBranchPositions()
    {
        foreach (var branch in allBranches)
        {
            if (branch.parentBranch != null && branch.parentBranch.tube != null)
            {
                Vector3 newPos = branch.parentBranch.tube.GetPointOnCurve(branch.tOnParent);
                Vector3 oldPos = branch.tube.controlPoints[0].position;

                // 根元だけでなく枝全体を平行移動する
                Vector3 offset = newPos - oldPos;

                if (branch.tube.controlPoints != null && branch.tube.controlPoints.Length > 0)
                {
                    for (int i = 0; i < branch.tube.controlPoints.Length; i++)
                    {
                        branch.tube.controlPoints[i].position += offset;
                    }
                }

                branch.tube.GenerateTube();
            }
        }
    }

    // 枝を削除する
    public void DeleteBranch(GameObject branchObj)
    {
        if (branchObj == null) return;

        BranchMesh tube = branchObj.GetComponent<BranchMesh>();
        if (tube == null) return;

        BranchInfo branch = allBranches.Find(b => b.tube == tube);
        if (branch == null) return;

        // 再帰削除なので子枝から消す
        foreach (var child in new List<BranchInfo>(branch.childBranches))
        {
            DeleteBranch(child.tube.gameObject);
        }

        foreach (var leaf in new List<LeafInfo>(branch.leaves))
        {
            DeleteLeaf(leaf.leafObject);
        }

        totalBranchesDeleted++;

        allBranches.Remove(branch);

        // 親からも外す
        if (branch.parentBranch != null)
        {
            branch.parentBranch.childBranches.Remove(branch);
        }
        else if (rootBranch == branch)
        {
            rootBranch = null;
        }

        Destroy(branchObj);
    }

    // 葉を削除する
    public void DeleteLeaf(GameObject leafObj)
    {
        if (leafObj == null) return;

        LeafInfo leaf = allLeaves.Find(l => l.leafObject == leafObj);
        if (leaf == null) return;

        totalLeavesDeleted++;

        allLeaves.Remove(leaf);

        if (leaf.attachedBranch != null)
        {
            leaf.attachedBranch.leaves.Remove(leaf);
        }

        Destroy(leafObj);
    }

    // 子枝と葉のtを更新する
    public void RecalculateChildT(BranchInfo parent)
    {
        if (parent == null || parent.tube == null) return;

        BranchMesh tube = parent.tube;

        foreach (var child in parent.childBranches)
        {
            if (child.tube == null) continue;

            // 子枝は根元CPを基準にする
            Vector3 childPos = child.tube.controlPoints[0].position;
            float newT = FindClosestTOnCurve(tube, childPos);
            child.tOnParent = newT;
        }

        foreach (var leaf in parent.leaves)
        {
            if (leaf.leafObject == null) continue;

            // 葉は現在位置からtを取り直す
            Vector3 leafPos = leaf.leafObject.transform.position;
            float newT = FindClosestTOnCurve(tube, leafPos);
            leaf.tOnBranch = newT;
        }
    }

    // サンプリングで一番近いtを探す
    private float FindClosestTOnCurve(BranchMesh tube, Vector3 targetPos, int samples = 64)
    {
        float bestT = 0f;
        float minDist = float.MaxValue;

        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 p = tube.GetPointOnCurve(t);
            float d = Vector3.SqrMagnitude(p - targetPos);
            if (d < minDist)
            {
                minDist = d;
                bestT = t;
            }
        }

        return bestT;
    }

    // 末端区間のtを返す
    public Vector2 GetEndSegmentT(BranchMesh branchTube)
    {
        if (branchTube == null || branchTube.controlPoints == null || branchTube.controlPoints.Length < 2)
            return Vector2.zero;

        Transform secondLastCP = branchTube.controlPoints[branchTube.controlPoints.Length - 2];
        Transform lastCP = branchTube.controlPoints[branchTube.controlPoints.Length - 1];

        float t1 = FindClosestTOnCurve(branchTube, secondLastCP.position);
        float t2 = FindClosestTOnCurve(branchTube, lastCP.position);

        return new Vector2(t1, t2);
    }

    // 木の状態をログに出す
    public void DebugTree()
    {
        UnityEngine.Debug.Log($"TreeManager: {allBranches.Count} branches, {allLeaves.Count} leaves");

        foreach (var b in allBranches)
        {
            string parentName = b.parentBranch != null ? b.parentBranch.tube.name : "null";
            UnityEngine.Debug.Log($"Branch {b.tube.name}, Parent={parentName}, " +
                      $"Children={b.childBranches.Count}, Leaves={b.leaves.Count}");
        }
    }

    // 大きさスコアを出す
    public float GetTreeSizeScore()
    {
        if (allBranches.Count == 0) return 0f;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var branch in allBranches)
        {
            foreach (var cp in branch.tube.controlPoints)
            {
                Vector3 p = cp.position;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
        }

        float height = maxY - minY;
        float width = maxX - minX;
        float depth = maxZ - minZ;

        float sizeValue = height * (width + depth) * 0.5f;

        return Mathf.Clamp01(sizeValue / targetSize) * 100f;
    }

    // 努力スコアを出す
    public float GetEffortScore()
    {
        int totalCreated = totalBranchesCreated + totalLeavesCreated;
        int totalDeleted = totalBranchesDeleted + totalLeavesDeleted;

        if (totalCreated == 0) return 0f;

        float deletedRatio = (float)totalDeleted / totalCreated;

        if (deletedRatio >= 0.3f)
            return 100f;

        return (deletedRatio / 0.3f) * 100f;
    }

    // 枝の情報
    [System.Serializable]
    public class BranchInfo
    {
        public BranchMesh tube;
        public BranchInfo parentBranch;
        public List<BranchInfo> childBranches;
        public List<LeafInfo> leaves;
        public float tOnParent;
    }

    // 葉の情報
    [System.Serializable]
    public class LeafInfo
    {
        public GameObject leafObject;
        public BranchInfo attachedBranch;
        public float tOnBranch;
    }
}
