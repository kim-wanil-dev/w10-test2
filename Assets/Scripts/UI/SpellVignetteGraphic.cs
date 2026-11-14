using UnityEngine;
using UnityEngine.UI;

/// <summary>영역 가운데는 비우고 가장자리로 갈수록 색이 짙어지는 테두리 그늘을 그린다. 화면에 깊이를 주거나, 맞았을 때 가장자리를 붉게 번쩍이는 데 쓴다. 입력은 받지 않는다.</summary>
public sealed class SpellVignetteGraphic : MaskableGraphic
{
    private const int COLUMNS = 24;
    private const int ROWS = 14;

    // 가운데에서 이 비율(0~1, 모서리가 1)까지는 완전히 투명하다.
    [SerializeField, Range(0f, 1f)] private float _inner = 0.45f;
    // 클수록 가장자리 가까이에서만 짙어진다.
    [SerializeField, Min(0.1f)] private float _power = 1.7f;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>투명한 가운데 범위와 짙어지는 곡선을 바꾼다.</summary>
    public void SetShape(float inner, float power)
    {
        _inner = Mathf.Clamp01(inner); _power = Mathf.Max(0.1f, power);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        Color tint = color;
        if (tint.a <= 0.003f) return;
        for (int y = 0; y <= ROWS; y++) for (int x = 0; x <= COLUMNS; x++)
        {
            float u = x / (float)COLUMNS, v = y / (float)ROWS;
            // 가운데가 0, 네 모서리가 1이 되는 거리다.
            float distance = new Vector2(u * 2 - 1, v * 2 - 1).magnitude / 1.41421f;
            float amount = Mathf.Pow(Mathf.Clamp01((distance - _inner) / Mathf.Max(0.001f, 1 - _inner)), _power);
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, u), Mathf.Lerp(rect.yMin, rect.yMax, v));
            vertex.color = new Color(tint.r, tint.g, tint.b, tint.a * amount);
            vh.AddVert(vertex);
        }
        for (int y = 0; y < ROWS; y++) for (int x = 0; x < COLUMNS; x++)
        {
            int a = y * (COLUMNS + 1) + x, b = a + 1, c = a + COLUMNS + 1, d = c + 1;
            vh.AddTriangle(a, c, d); vh.AddTriangle(a, d, b);
        }
    }
}
