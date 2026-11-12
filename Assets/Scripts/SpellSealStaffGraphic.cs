using UnityEngine;

/// <summary>봉인된 주문 도안에 내려꽂히는 긴 막대기를 외부 이미지 없이 그리고, 낙하·고정·철수 상태를 스스로 갱신한다.</summary>
public sealed class SpellSealStaffGraphic : UnityEngine.UI.MaskableGraphic
{
    private const float DROP_SECONDS = 0.26f;
    private const float WITHDRAW_SECONDS = 0.18f;
    private const float DROP_DISTANCE = 380f;
    private const float SHAFT_HEIGHT = 250f;
    private const float HEAD_CENTER = 262f;
    private const float IMPACT_SECONDS = 0.5f;
    private const int RING_SEGMENTS = 28;

    private enum Phase { Hidden, Drop, Stuck, Withdraw }

    private Phase _phase;
    private float _phaseStart;
    private float _lift;
    private float _fade;
    private float _impactAge = -1f;
    private Color _staffColor = Color.white;

    /// <summary>막대기 기본 색을 지정하고 포인터 입력을 받지 않도록 설정한다.</summary>
    public void Setup(Color color)
    {
        _staffColor = color;
        raycastTarget = false;
        SetVerticesDirty();
    }

    /// <summary>위에서 내려꽂는 낙하 상태로 초기화하고 화면에 표시한다.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        _phase = Phase.Drop;
        _phaseStart = Time.unscaledTime;
        _lift = DROP_DISTANCE;
        _fade = 1f;
        _impactAge = -1f;
        SetVerticesDirty();
    }

    /// <summary>봉인 해제 시 막대기를 위로 빼 올리며 서서히 지운다.</summary>
    public void Withdraw()
    {
        if (_phase == Phase.Hidden) { HideNow(); return; }
        _phase = Phase.Withdraw;
        _phaseStart = Time.unscaledTime;
        _fade = 1f;
        SetVerticesDirty();
    }

    /// <summary>애니메이션 없이 즉시 숨긴다.</summary>
    public void HideNow()
    {
        _phase = Phase.Hidden;
        _lift = 0f;
        _fade = 0f;
        _impactAge = -1f;
        SetVerticesDirty();
        gameObject.SetActive(false);
    }

    /// <summary>기준점을 옮겨 도안 이동·회전을 따라가게 한다.</summary>
    public void MoveTo(Vector2 anchoredPosition)
    {
        rectTransform.anchoredPosition = anchoredPosition;
    }

    void Update()
    {
        if (_phase == Phase.Hidden) return;
        float elapsed = Time.unscaledTime - _phaseStart;
        switch (_phase)
        {
            case Phase.Drop:
                float drop = Mathf.Clamp01(elapsed / DROP_SECONDS);
                _lift = Mathf.Lerp(DROP_DISTANCE, 0f, 1f - Mathf.Pow(1f - drop, 3f));
                if (drop >= 1f) { _phase = Phase.Stuck; _impactAge = 0f; }
                break;
            case Phase.Stuck:
                _lift = 0f;
                if (_impactAge >= 0f) _impactAge += Time.unscaledDeltaTime;
                break;
            case Phase.Withdraw:
                float withdraw = Mathf.Clamp01(elapsed / WITHDRAW_SECONDS);
                _lift = Mathf.Lerp(0f, DROP_DISTANCE * 0.6f, withdraw * withdraw);
                _fade = 1f - withdraw;
                if (withdraw >= 1f) { HideNow(); return; }
                break;
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        float alpha = color.a * _fade;
        if (alpha <= 0.001f) return;

        Color body = Tint(_staffColor, alpha);
        Color highlight = Tint(Color.Lerp(_staffColor, Color.white, 0.55f), alpha * 0.55f);
        Color dark = Tint(Color.Lerp(_staffColor, Color.black, 0.4f), alpha);
        Color glow = Tint(Color.Lerp(_staffColor, Color.white, 0.6f), alpha);
        Color shadow = new Color(0f, 0f, 0f, 0.32f * alpha * Mathf.Clamp01(1f - _lift / 300f));
        float sway = _phase == Phase.Stuck ? Mathf.Sin(Time.unscaledTime * 2.2f) * 1.2f : 0f;

        Vector2 At(float x, float y)
        {
            return Rotate(new Vector2(x, y + _lift), sway);
        }

        if (shadow.a > 0.001f) Ellipse(vh, new Vector2(0f, 1f), 24f, 6f, shadow);

        Quad(vh, At(-3.2f, 0f), At(3.2f, 0f), At(2.4f, SHAFT_HEIGHT), At(-2.4f, SHAFT_HEIGHT), body);
        Quad(vh, At(-2.6f, 8f), At(-1.4f, 8f), At(-1.2f, SHAFT_HEIGHT - 6f), At(-2.2f, SHAFT_HEIGHT - 6f), highlight);
        Quad(vh, At(-4.6f, 30f), At(4.6f, 30f), At(4.6f, 36f), At(-4.6f, 36f), dark);
        Quad(vh, At(-4.4f, 88f), At(4.4f, 88f), At(4.4f, 94f), At(-4.4f, 94f), dark);

        Vector2 head = At(0f, HEAD_CENTER);
        Diamond(vh, head, 13f, 22f, dark);
        Diamond(vh, head, 6f, 11f, highlight);

        if (_impactAge >= 0f && _impactAge < IMPACT_SECONDS)
        {
            float impact = Mathf.Clamp01(_impactAge / IMPACT_SECONDS);
            float radius = Mathf.Lerp(10f, 52f, 1f - Mathf.Pow(1f - impact, 2f));
            Color ring = Tint(glow, alpha * (1f - impact));
            Ring(vh, new Vector2(0f, 0f), radius, radius - 3f, ring);
            for (int i = 0; i < 5; i++)
            {
                float angle = (20f + i * 68f) * Mathf.Deg2Rad;
                float inner = 8f, outer = 18f + impact * 22f;
                Vector2 from = new Vector2(Mathf.Cos(angle) * inner, Mathf.Sin(angle) * inner);
                Vector2 to = new Vector2(Mathf.Cos(angle) * outer, Mathf.Sin(angle) * outer);
                Line(vh, from, to, 1.6f, ring);
            }
        }
    }

    /// <summary>그래픽 색조와 알파를 곱한 표시 색을 반환한다.</summary>
    private Color Tint(Color source, float alpha)
    {
        return new Color(source.r * color.r, source.g * color.g, source.b * color.b, alpha);
    }

    /// <summary>기준점을 중심으로 지정 각도만큼 회전한 좌표를 반환한다.</summary>
    private static Vector2 Rotate(Vector2 point, float degrees)
    {
        if (degrees == 0f) return point;
        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
        return new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
    }

    /// <summary>네 모서리와 색으로 사각형을 메시 끝에 추가한다.</summary>
    private static void Quad(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int start = vh.currentVertCount;
        Vertex(vh, a, color); Vertex(vh, b, color); Vertex(vh, c, color); Vertex(vh, d, color);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>중심과 반지름, 색으로 채운 타원을 메시 끝에 추가한다.</summary>
    private static void Ellipse(UnityEngine.UI.VertexHelper vh, Vector2 center, float radiusX, float radiusY, Color color)
    {
        int start = vh.currentVertCount;
        Vertex(vh, center, color);
        for (int i = 0; i <= RING_SEGMENTS; i++)
        {
            float angle = i * Mathf.PI * 2f / RING_SEGMENTS;
            Vertex(vh, center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY), color);
            if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    /// <summary>바깥·안쪽 반지름과 색으로 고리띠를 메시 끝에 추가한다.</summary>
    private static void Ring(UnityEngine.UI.VertexHelper vh, Vector2 center, float outer, float inner, Color color)
    {
        int start = vh.currentVertCount;
        for (int i = 0; i <= RING_SEGMENTS; i++)
        {
            float angle = i * Mathf.PI * 2f / RING_SEGMENTS;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vertex(vh, center + direction * inner, color);
            Vertex(vh, center + direction * outer, color);
            if (i > 0)
            {
                int p = start + (i - 1) * 2;
                vh.AddTriangle(p, p + 1, p + 2);
                vh.AddTriangle(p + 1, p + 3, p + 2);
            }
        }
    }

    /// <summary>중심과 반너비·반높이로 마름모를 메시 끝에 추가한다.</summary>
    private static void Diamond(UnityEngine.UI.VertexHelper vh, Vector2 center, float halfWidth, float halfHeight, Color color)
    {
        Quad(vh, center + new Vector2(0f, halfHeight), center + new Vector2(halfWidth, 0f), center + new Vector2(0f, -halfHeight), center + new Vector2(-halfWidth, 0f), color);
    }

    /// <summary>두 점과 두께, 색으로 선분을 메시 끝에 추가한다.</summary>
    private static void Line(UnityEngine.UI.VertexHelper vh, Vector2 from, Vector2 to, float width, Color color)
    {
        Vector2 direction = (to - from).normalized;
        Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
        Quad(vh, from + normal, to + normal, to - normal, from - normal, color);
    }

    /// <summary>지정 위치와 색의 UI 정점을 메시 끝에 추가한다.</summary>
    private static void Vertex(UnityEngine.UI.VertexHelper vh, Vector2 position, Color color)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vh.AddVert(vertex);
    }
}
