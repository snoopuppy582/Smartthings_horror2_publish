using UnityEngine;

// 보이지 않는(메시 없는) 계단 콜라이더 생성기.
// 빈 오브젝트에 붙이고 인스펙터 우클릭 > "Generate Stairs" 로 생성.
// ※ NavMesh 굽기 전에 에디터에서 생성해야 killer도 올라갈 수 있음.
public class InvisibleStairs : MonoBehaviour
{
    [Header("계단 규격")]
    [Min(1)] public int stepCount = 8;     // 단 개수
    public float stepWidth = 2.0f;         // 폭(가로)
    public float stepHeight = 0.25f;       // 한 단 높이 — 플레이어 Step Offset 이하로!
    public float stepDepth = 0.35f;        // 한 단 깊이(앞 방향 길이)
    public float stepThickness = 0.6f;     // 콜라이더 두께(아래로) — 단 사이 빈틈 방지

    [ContextMenu("Generate Stairs")]
    public void Generate()
    {
        Clear();
        for (int i = 0; i < stepCount; i++)
        {
            var step = new GameObject($"Step_{i}");
            step.transform.SetParent(transform, false);

            // i단: 위로 (i+1)*height, 앞으로 i*depth
            float y = (i + 1) * stepHeight - stepThickness * 0.5f;
            float z = i * stepDepth + stepDepth * 0.5f;
            step.transform.localPosition = new Vector3(0f, y, z);

            var box = step.AddComponent<BoxCollider>();
            box.size = new Vector3(stepWidth, stepThickness, stepDepth);
            // MeshRenderer 없음 → 완전 투명. 콜라이더만 존재.
        }
        Debug.Log($"[InvisibleStairs] {stepCount}단 생성 (한 단 {stepHeight}m, 총 높이 {stepCount * stepHeight:F2}m)");
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
    }

    // 보이지 않으니 위치 잡기용 시각화
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        Gizmos.matrix = transform.localToWorldMatrix;
        for (int i = 0; i < stepCount; i++)
        {
            float y = (i + 1) * stepHeight - stepThickness * 0.5f;
            float z = i * stepDepth + stepDepth * 0.5f;
            Gizmos.DrawWireCube(new Vector3(0, y, z), new Vector3(stepWidth, stepThickness, stepDepth));
        }
    }
}