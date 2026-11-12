using UnityEngine;

/// <summary>망치 작업 공간의 원형 나무판과 테두리를 외부 이미지 없이 그린다.</summary>
public sealed class SpellHammerGraphic : UnityEngine.UI.MaskableGraphic
{
    private const int SEGMENTS = 64;
    private static readonly Color RIM_COLOR = new Color(0.52f, 0.43f, 0.32f);
    private static readonly Color WOOD_COLOR = new Color(0.16f, 0.105f, 0.075f);

    /// <summary>원형 테두리와 나뭇결 선분을 작업 공간 크기에 맞춰 생성한다.</summary>
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        Vector2 center = rectTransform.rect.center;
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 2;
        Disc(vh, center, radius, RIM_COLOR);
        Disc(vh, center, radius - 8, WOOD_COLOR);
        float inner = radius - 12;
        for (int i = -4; i <= 4; i++)
        {
            float x = i * inner / 5;
            float height = Mathf.Sqrt(inner * inner - x * x);
            Quad(vh, center + new Vector2(x - 1, -height), center + new Vector2(x + 1, height), new Color(0.21f, 0.14f, 0.10f));
        }
    }

    /// <summary>중심과 반지름, 색으로 채운 원을 메시 끝에 추가한다.</summary>
    private static void Disc(UnityEngine.UI.VertexHelper vh, Vector2 center, float radius, Color color)
    {
        int start = vh.currentVertCount;
        Vertex(vh, center, color);
        for (int i = 0; i <= SEGMENTS; i++)
        {
            float angle = i * Mathf.PI * 2 / SEGMENTS;
            Vertex(vh, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color);
            if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    /// <summary>두 모서리와 색으로 나뭇결 사각형을 메시 끝에 추가한다.</summary>
    private static void Quad(UnityEngine.UI.VertexHelper vh, Vector2 min, Vector2 max, Color color)
    {
        int start = vh.currentVertCount;
        Vertex(vh, min, color); Vertex(vh, new Vector2(min.x, max.y), color);
        Vertex(vh, max, color); Vertex(vh, new Vector2(max.x, min.y), color);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>지정 위치와 색의 UI 정점을 메시 끝에 추가한다.</summary>
    private static void Vertex(UnityEngine.UI.VertexHelper vh, Vector2 position, Color color)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position; vertex.color = color; vh.AddVert(vertex);
    }
}

