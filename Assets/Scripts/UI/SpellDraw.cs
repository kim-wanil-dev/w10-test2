using UnityEngine;
using UnityEngine.UI;

/// <summary>절차적 2D 연출이 함께 쓰는 도형 그리기 도구다. 정점 색만으로 원·고리·선·빛 번짐을 만든다.
/// Additive가 0이면 보통의 알파 혼합, 1이면 SpellStyle.GlowMaterial에서 빛처럼 더해진다.</summary>
public static class SpellDraw
{
    public const int CIRCLE_SIDES = 20;

    /// <summary>지금부터 추가하는 정점의 가산 혼합 정도(0~1)다. 빛 번짐 함수는 안에서 잠시 1로 바꿨다가 되돌린다.</summary>
    public static float Additive;

    /// <summary>정점 하나를 위치와 색으로 추가한다.</summary>
    public static void AddVert(VertexHelper vh, Vector2 position, Color color)
    {
        UIVertex vertex = UIVertex.simpleVert; vertex.position = position; vertex.color = color; vertex.uv0 = new Vector4(Additive, 0, 0, 0); vh.AddVert(vertex);
    }

    /// <summary>가운데가 밝고 가장자리로 갈수록 사라지는 둥근 빛을 더한다. 색의 알파가 가운데 밝기다.</summary>
    public static void Glow(VertexHelper vh, Vector2 at, float radius, Color color)
    {
        if (color.a <= 0.003f || radius <= 0) return;
        float previous = Additive; Additive = 1;
        // 가운데 → 중간 고리 → 바깥 고리로 밝기를 두 번 꺾어 부드러운 감쇠를 흉내 낸다.
        Color middle = Alpha(color, color.a * 0.38f), rim = Alpha(color, 0);
        int start = vh.currentVertCount;
        AddVert(vh, at, color);
        for (int i = 0; i < CIRCLE_SIDES; i++) AddVert(vh, at + Direction(i * Mathf.PI * 2 / CIRCLE_SIDES) * radius * 0.42f, middle);
        for (int i = 0; i < CIRCLE_SIDES; i++) AddVert(vh, at + Direction(i * Mathf.PI * 2 / CIRCLE_SIDES) * radius, rim);
        for (int i = 0; i < CIRCLE_SIDES; i++)
        {
            int a = start + 1 + i, b = start + 1 + (i + 1) % CIRCLE_SIDES, c = a + CIRCLE_SIDES, d = b + CIRCLE_SIDES;
            vh.AddTriangle(start, a, b);
            vh.AddTriangle(a, c, d); vh.AddTriangle(a, d, b);
        }
        Additive = previous;
    }

    /// <summary>가운데 줄이 밝고 양옆으로 사라지는 빛줄기를 더한다.</summary>
    public static void GlowLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        if (color.a <= 0.003f || width <= 0 || (b - a).sqrMagnitude < 0.01f) return;
        float previous = Additive; Additive = 1;
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width / 2;
        Color edge = Alpha(color, 0);
        int start = vh.currentVertCount;
        AddVert(vh, a + n, edge); AddVert(vh, b + n, edge); AddVert(vh, a, color); AddVert(vh, b, color); AddVert(vh, a - n, edge); AddVert(vh, b - n, edge);
        vh.AddTriangle(start, start + 1, start + 3); vh.AddTriangle(start, start + 3, start + 2);
        vh.AddTriangle(start + 2, start + 3, start + 5); vh.AddTriangle(start + 2, start + 5, start + 4);
        Additive = previous;
    }

    /// <summary>채운 원을 추가한다.</summary>
    public static void Circle(VertexHelper vh, Vector2 at, float radius, Color color) { Polygon(vh, at, radius, CIRCLE_SIDES, 0, color); }

    /// <summary>채운 정다각형을 추가한다.</summary>
    public static void Polygon(VertexHelper vh, Vector2 at, float radius, int sides, float rotation, Color color)
    {
        if (color.a <= 0.003f || radius <= 0) return;
        int start = vh.currentVertCount;
        AddVert(vh, at, color);
        for (int i = 0; i < sides; i++) AddVert(vh, at + Direction(rotation + i * Mathf.PI * 2 / sides) * radius, color);
        for (int i = 0; i < sides; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % sides);
    }

    /// <summary>정다각형 테두리를 지정 두께로 추가한다.</summary>
    public static void Outline(VertexHelper vh, Vector2 at, float radius, int sides, float rotation, float width, Color color)
    {
        for (int i = 0; i < sides; i++)
            Line(vh, at + Direction(rotation + i * Mathf.PI * 2 / sides) * radius, at + Direction(rotation + (i + 1) * Mathf.PI * 2 / sides) * radius, width, color);
    }

    /// <summary>지정 두께의 고리를 추가한다.</summary>
    public static void Ring(VertexHelper vh, Vector2 at, float radius, float width, Color color)
    {
        if (color.a <= 0.003f || width <= 0) return;
        int start = vh.currentVertCount;
        for (int i = 0; i <= CIRCLE_SIDES; i++)
        {
            Vector2 dir = Direction(i * Mathf.PI * 2 / CIRCLE_SIDES);
            AddVert(vh, at + dir * (radius + width / 2), color); AddVert(vh, at + dir * Mathf.Max(0, radius - width / 2), color);
        }
        for (int i = 0; i < CIRCLE_SIDES; i++)
        {
            int a = start + i * 2;
            vh.AddTriangle(a, a + 1, a + 2); vh.AddTriangle(a + 1, a + 3, a + 2);
        }
    }

    /// <summary>두 점 사이에 두께가 있는 선을 추가한다.</summary>
    public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        if (color.a <= 0.003f || width <= 0 || (b - a).sqrMagnitude < 0.01f) return;
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width / 2;
        int start = vh.currentVertCount;
        AddVert(vh, a + n, color); AddVert(vh, b + n, color); AddVert(vh, b - n, color); AddVert(vh, a - n, color);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>중심과 크기로 축 정렬 사각형을 추가한다.</summary>
    public static void Box(VertexHelper vh, Vector2 center, Vector2 size, Color color)
    {
        if (color.a <= 0.003f) return;
        Vector2 h = size / 2;
        int start = vh.currentVertCount;
        AddVert(vh, center + new Vector2(-h.x, -h.y), color); AddVert(vh, center + new Vector2(-h.x, h.y), color);
        AddVert(vh, center + new Vector2(h.x, h.y), color); AddVert(vh, center + new Vector2(h.x, -h.y), color);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>중심·크기·회전(라디안)으로 돌린 사각형을 채운다.</summary>
    public static void Quad(VertexHelper vh, Vector2 center, Vector2 size, float angle, Color color)
    {
        if (color.a <= 0.003f) return;
        Vector2 x = Direction(angle) * size.x / 2, y = Direction(angle + Mathf.PI / 2) * size.y / 2;
        int start = vh.currentVertCount;
        AddVert(vh, center - x - y, color); AddVert(vh, center - x + y, color); AddVert(vh, center + x + y, color); AddVert(vh, center + x - y, color);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>세 꼭짓점으로 삼각형을 채운다.</summary>
    public static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        if (color.a <= 0.003f) return;
        int start = vh.currentVertCount;
        AddVert(vh, a, color); AddVert(vh, b, color); AddVert(vh, c, color);
        vh.AddTriangle(start, start + 1, start + 2);
    }

    /// <summary>방향 dir로 길쭉한 마름모(수정) 모양을 채운다.</summary>
    public static void Crystal(VertexHelper vh, Vector2 at, Vector2 dir, float length, float width, Color color)
    {
        if (color.a <= 0.003f) return;
        Vector2 n = new Vector2(-dir.y, dir.x) * width;
        int start = vh.currentVertCount;
        AddVert(vh, at + dir * length, color); AddVert(vh, at + n, color); AddVert(vh, at - dir * length * 0.6f, color); AddVert(vh, at - n, color);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>위가 평평하고 아래가 뾰족한 방패 모양을 채우고 테두리를 그린다.</summary>
    public static void Shield(VertexHelper vh, Vector2 at, float size, Color fill, Color edge)
    {
        Vector2[] points = { new Vector2(-0.8f, 0.9f), new Vector2(0.8f, 0.9f), new Vector2(0.8f, 0.1f), new Vector2(0, -1f), new Vector2(-0.8f, 0.1f) };
        int start = vh.currentVertCount;
        AddVert(vh, at, fill);
        foreach (Vector2 p in points) AddVert(vh, at + p * size, fill);
        for (int i = 0; i < points.Length; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % points.Length);
        for (int i = 0; i < points.Length; i++) Line(vh, at + points[i] * size, at + points[(i + 1) % points.Length] * size, 4f, edge);
    }

    /// <summary>각도(라디안)의 단위 방향을 반환한다.</summary>
    public static Vector2 Direction(float angle) { return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); }

    /// <summary>색의 알파만 바꾼 색을 반환한다.</summary>
    public static Color Alpha(Color color, float alpha) { color.a = Mathf.Clamp01(alpha); return color; }

    /// <summary>천천히 시작해 빨라지는 보간 값이다.</summary>
    public static float EaseIn(float t) { return t * t; }

    /// <summary>빠르게 시작해 느려지는 보간 값이다.</summary>
    public static float EaseOut(float t) { return 1 - (1 - t) * (1 - t); }

    /// <summary>씨앗과 번호로 0~1 사이의 고정 난수를 반환한다. 같은 연출은 프레임마다 같은 모양을 유지한다.</summary>
    public static float Hash(int seed, int index)
    {
        unchecked
        {
            uint h = (uint)(seed * 73856093) ^ (uint)(index * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
