using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 枝と葉を選択する
public class BranchSelector : MonoBehaviour
{
    private TreeManager tm;

    private enum SelectMode { Branch, Leaf }
    private SelectMode currentMode = SelectMode.Branch;

    [Header("幹")]
    public BranchMesh trunk;

    [Header("レイヤー")]
    public LayerMask branchLayer;
    public LayerMask controlPointLayer;
    public LayerMask leafLayer;

    [Header("制御点選択距離のしきい値")]
    public float selectThreshold = 1.0f;

    private BranchMesh lookingBranch;
    private BranchMesh selectedBranch;
    private Transform selectedControlPoint;
    private Camera mainCam;

    private BranchMesh highlightedBranch;
    private GameObject highlightedLeaf;
    private Color originalColor;

    [Header("削除確認UI")]
    public GameObject deleteBranchConfirmUI;
    public GameObject deleteLeafConfirmUI;
    public GameObject cantDeleteTrunkUI;

    private bool isDeleteBranchConfirmActive = false;
    private bool isDeleteLeafConfirmActive = false;
    private bool isCantDeleteTrunkActive = false;

    private float[] controlPointDistances;

    public GameObject Ttext;
    public GameObject Ttext2;

    public GameObject Tetlis;
    public GameObject Slot;
    public GameObject Set;

    // 初期化
    void Start()
    {
        mainCam = Camera.main;
        tm = FindObjectOfType<TreeManager>();
    }

    // 入力処理
    void Update()
    {
        if (Tetlis.activeSelf || Slot.activeSelf || Set.activeSelf) return;

        // モード切替
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            currentMode = currentMode == SelectMode.Branch ? SelectMode.Leaf : SelectMode.Branch;
            UnityEngine.Debug.Log($"モード切替: {currentMode}");
            ClearHighlight();
            DeselectBranch();
            deleteBranchConfirmUI.SetActive(false);
            deleteLeafConfirmUI.SetActive(false);
            cantDeleteTrunkUI.SetActive(false);
            if (currentMode == SelectMode.Branch)
            {
                Ttext.SetActive(true);
                Ttext2.SetActive(false);
                MoneyManager.Instance.SetExp(0);
            }
            else
            {
                Ttext.SetActive(false);
                Ttext2.SetActive(true);
                MoneyManager.Instance.SetExp(2);
            }
        }

        lookingBranch = null;
        UpdateHighlight();

        // 左クリック
        if (Input.GetMouseButtonDown(0) && currentMode == SelectMode.Branch)
        {
            if (selectedBranch == null)
            {
                if (lookingBranch != null)
                {
                    if (MoneyManager.Instance.CurrentMoney < 100)
                    {
                        SystemNotification.Instance.ShowNotification("100円が必要です");
                    }
                    else
                    {
                        MoneyManager.Instance.SpendMoney(100);

                        SelectBranch(lookingBranch);
                        MoneyManager.Instance.SetExp(1);
                    }
                }
            }
            else
            {
                TrySelectCentralControlPoint();
            }
        }

        // 制御点ドラッグ
        if (selectedBranch != null && selectedControlPoint != null && Input.GetMouseButton(0))
        {
            if (selectedControlPoint.name.Contains("CP_0")) return;

            Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(-mainCam.transform.forward, selectedControlPoint.position);

            if (plane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                Vector3 local = mainCam.transform.InverseTransformPoint(hitPoint);
                local.z = mainCam.transform.InverseTransformPoint(selectedControlPoint.position).z;
                Vector3 candidatePos = mainCam.transform.TransformPoint(local);

                int selectedIndex = System.Array.IndexOf(selectedBranch.controlPoints, selectedControlPoint);
                if (selectedIndex > 0)
                {
                    Transform prevCP = selectedBranch.controlPoints[selectedIndex - 1];
                    float maxDist = controlPointDistances[selectedIndex];

                    // 元の制御点間距離を超えないようにする
                    Vector3 dir = candidatePos - prevCP.position;
                    if (dir.magnitude > maxDist)
                    {
                        dir = dir.normalized * maxDist;
                    }

                    Vector3 constrainedPos = prevCP.position + dir;
                    Vector3 delta = constrainedPos - selectedControlPoint.position;

                    selectedControlPoint.position = constrainedPos;

                    // 後ろの制御点も動かして枝先の形を保つ
                    for (int i = selectedIndex + 1; i < selectedBranch.controlPoints.Length; i++)
                    {
                        if (selectedBranch.controlPoints[i] != null)
                        {
                            selectedBranch.controlPoints[i].position += delta;
                        }
                    }
                }

                selectedBranch.GenerateTube();

                if (tm != null)
                {
                    tm.UpdateBranchPositions();
                    tm.UpdateLeafPositions();
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            selectedControlPoint = null;
        }

        // 選択解除
        if (Input.GetKeyDown(KeyCode.Space) && !isDeleteBranchConfirmActive && !isDeleteLeafConfirmActive)
        {
            DeselectBranch();
            MoneyManager.Instance.SetExp(0);
        }

        // 枝削除
        if (currentMode == SelectMode.Branch)
        {
            if (Input.GetMouseButtonDown(1) && selectedBranch != null)
            {
                if (selectedBranch != trunk)
                {
                    if (!isDeleteBranchConfirmActive)
                    {
                        if (deleteBranchConfirmUI != null) deleteBranchConfirmUI.SetActive(true);
                        isDeleteBranchConfirmActive = true;
                    }
                    else
                    {
                        if (MoneyManager.Instance.CurrentMoney < 100)
                        {
                            SystemNotification.Instance.ShowNotification("100円が必要です");
                        }
                        else
                        {
                            MoneyManager.Instance.SpendMoney(100);

                            tm.DeleteBranch(selectedBranch.gameObject);
                            DeselectBranch();
                            if (deleteBranchConfirmUI != null) deleteBranchConfirmUI.SetActive(false);
                            isDeleteBranchConfirmActive = false;
                        }
                    }
                }
                else
                {
                    if (!isCantDeleteTrunkActive)
                    {
                        if (cantDeleteTrunkUI != null) cantDeleteTrunkUI.SetActive(true);
                        isCantDeleteTrunkActive = true;

                        StartCoroutine(HideCantDeleteTrunkUIAfterDelay(2f));
                    }
                }
            }
        }
        else if (currentMode == SelectMode.Leaf)
        {
            if (Input.GetMouseButtonDown(1) && highlightedLeaf != null)
            {
                if (!isDeleteLeafConfirmActive)
                {
                    if (deleteLeafConfirmUI != null) deleteLeafConfirmUI.SetActive(true);
                    isDeleteLeafConfirmActive = true;
                }
                else
                {
                    if (MoneyManager.Instance.CurrentMoney < 100)
                    {
                        SystemNotification.Instance.ShowNotification("100円が必要です");
                    }
                    else
                    {
                        MoneyManager.Instance.SpendMoney(100);

                        tm.DeleteLeaf(highlightedLeaf);
                        if (deleteLeafConfirmUI != null) deleteLeafConfirmUI.SetActive(false);
                        isDeleteLeafConfirmActive = false;
                    }
                }
            }
        }

        // 削除キャンセル
        if ((isDeleteBranchConfirmActive || isDeleteLeafConfirmActive) && Input.GetKeyDown(KeyCode.Space))
        {
            if (deleteBranchConfirmUI != null) deleteBranchConfirmUI.SetActive(false);
            if (deleteLeafConfirmUI != null) deleteLeafConfirmUI.SetActive(false);
            isDeleteBranchConfirmActive = false;
            isDeleteLeafConfirmActive = false;
        }
    }

    // 幹削除不可UIを閉じる
    IEnumerator HideCantDeleteTrunkUIAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (cantDeleteTrunkUI != null)
            cantDeleteTrunkUI.SetActive(false);

        isCantDeleteTrunkActive = false;
    }

    MeshRenderer highlightedRenderer;

    // ハイライト更新
    void UpdateHighlight()
    {
        if (currentMode == SelectMode.Branch && selectedBranch != null) return;
        if (currentMode == SelectMode.Leaf && isDeleteLeafConfirmActive) return;

        Ray ray = mainCam.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2));

        LayerMask mask = currentMode == SelectMode.Branch ? branchLayer : leafLayer;

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, mask))
        {
            MeshRenderer mr = hit.collider.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (currentMode == SelectMode.Branch)
                {
                    highlightedBranch = hit.collider.GetComponent<BranchMesh>();
                    lookingBranch = highlightedBranch;
                }
                else
                {
                    highlightedLeaf = hit.collider.gameObject;
                }

                // ハイライト変更時は前の色を戻す
                if (highlightedRenderer != null && highlightedRenderer != mr)
                {
                    highlightedRenderer.material.color = originalColor;
                }

                // 今の対象を青くする
                if (highlightedRenderer != mr)
                {
                    originalColor = mr.material.color;
                    mr.material.color = Color.Lerp(originalColor, Color.blue, 0.3f);
                    highlightedRenderer = mr;
                }
            }
        }
        else
        {
            ClearHighlight();
        }
    }

    // ハイライト解除
    void ClearHighlight()
    {
        if (highlightedRenderer != null)
        {
            highlightedRenderer.material.color = originalColor;
            highlightedRenderer = null;
        }
        highlightedBranch = null;
        highlightedLeaf = null;
    }

    // 中央の制御点を選ぶ
    void TrySelectCentralControlPoint()
    {
        if (highlightedBranch == null) return;

        Ray ray = mainCam.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2));
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, controlPointLayer)) return;

        // 他の枝の制御点は掴まない
        foreach (var cp in selectedBranch.controlPoints)
        {
            if (cp == hit.transform)
            {
                selectedControlPoint = hit.transform;
                return;
            }
        }
    }

    // 枝を選択する
    void SelectBranch(BranchMesh branch)
    {
        if (selectedBranch != null) DeselectBranch();

        selectedBranch = branch;

        var renderer = branch.GetComponent<MeshRenderer>();
        Color c = renderer.material.color;
        c.a = 0.3f;
        renderer.material.color = c;

        foreach (var cp in branch.controlPoints)
        {
            if (cp.name.Contains("CP_0")) continue;
            if (cp != null)
            {
                var mr = cp.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = true;
            }
        }

        // ドラッグ制限用に元の距離を保存
        int n = branch.controlPoints.Length;
        controlPointDistances = new float[n];
        controlPointDistances[0] = 0f;
        for (int i = 1; i < n; i++)
        {
            if (branch.controlPoints[i] != null && branch.controlPoints[i - 1] != null)
            {
                controlPointDistances[i] = Vector3.Distance(branch.controlPoints[i].position,
                                                            branch.controlPoints[i - 1].position);
            }
        }
    }

    // 選択を外す
    void DeselectBranch()
    {
        if (selectedBranch == null) return;

        var renderer = selectedBranch.GetComponent<MeshRenderer>();
        Color c = renderer.material.color;
        c.a = 1f;
        renderer.material.color = c;

        foreach (var cp in selectedBranch.controlPoints)
        {
            if (cp != null)
            {
                var mr = cp.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
            }
        }

        selectedBranch = null;
        selectedControlPoint = null;

        if (deleteBranchConfirmUI != null) deleteBranchConfirmUI.SetActive(false);
        isDeleteBranchConfirmActive = false;
    }
}
