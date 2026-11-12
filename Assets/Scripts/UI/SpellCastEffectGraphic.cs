using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>전투 화면 위에 도안 시전 연출을 절차적 2D 도형(원, 고리, 선, 다각형)으로 그린다. 입력은 받지 않는다.</summary>
public sealed class SpellCastEffectGraphic : MaskableGraphic
{
    private const int CIRCLE_SIDES = 20;
    private const float HIT_STAGGER = 0.09f;
    private const float HIT_DURATION = 0.75f;
    private const float SELF_DURATION = 0.9f;
    private static readonly Color HOT_COLOR = new Color(1f, 0.62f, 0.25f);
    private static readonly Color BOLT_COLOR = new Color(0.75f, 0.9f, 1f);

    /// <summary>연출 하나의 종류, 시작 시각, 길이, 시전자·대상 위치(로컬), 색과 무작위 씨앗이다.</summary>
    private struct Burst
    {
        public SpellGame.CastEffect Kind;
        public float Start;
        public float Duration;
        public Vector2 From;
        public Vector2 To;
        public Color Color;
        public int Seed;
        public bool IsSelf;
    }

    private readonly List<Burst> _bursts = new List<Burst>();
    private int _nextSeed = 1;

    public bool IsPlaying { get { return _bursts.Count > 0; } }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>시전 연출을 시작한다. caster는 시전자, hitTargets는 실제로 맞은 적의 화면 RectTransform을 타격 순서대로 담는다. gainedBlock이면 시전자에게 방어 연출을 더한다.</summary>
    public void Play(SpellGame.CastEffect effect, RectTransform caster, IReadOnlyList<RectTransform> hitTargets, bool gainedBlock, Color color)
    {
        if (effect == SpellGame.CastEffect.None || effect == SpellGame.CastEffect.Auto) return;
        float now = Time.unscaledTime;
        Vector2 from = LocalCenter(caster);
        bool isSelfKind = effect == SpellGame.CastEffect.Barrier || effect == SpellGame.CastEffect.IronWall;
        if (gainedBlock || isSelfKind && hitTargets.Count == 0)
        {
            // 방어를 얻으면 시전자 쪽 방어 연출을 먼저 보여 준다. 방패 계열은 그 모양을, 나머지는 방벽을 쓴다.
            SpellGame.CastEffect self = isSelfKind || effect == SpellGame.CastEffect.ShieldBash ? effect : SpellGame.CastEffect.Barrier;
            _bursts.Add(new Burst { Kind = self, Start = now, Duration = SELF_DURATION, From = from, To = from, Color = color, Seed = _nextSeed++, IsSelf = true });
        }
        // 방어 전용 연출 도안이 피해도 주면(키워드 무작위 피해 등) 타격은 산탄 탄환으로 보여 준다.
        SpellGame.CastEffect hitKind = isSelfKind ? SpellGame.CastEffect.Buckshot : effect;
        float stagger = hitKind == SpellGame.CastEffect.Meteor ? HIT_STAGGER * 1.4f : HIT_STAGGER;
        for (int i = 0; i < hitTargets.Count; i++)
        {
            if (hitTargets[i] == null) continue;
            _bursts.Add(new Burst { Kind = hitKind, Start = now + i * stagger, Duration = HIT_DURATION, From = from, To = LocalCenter(hitTargets[i]), Color = color, Seed = _nextSeed++ });
        }
        SetVerticesDirty();
    }

    /// <summary>진행 중인 연출을 모두 지운다.</summary>
    public void Clear() { _bursts.Clear(); SetVerticesDirty(); }

    void Update()
    {
        if (_bursts.Count == 0) return;
        float now = Time.unscaledTime;
        _bursts.RemoveAll(b => now - b.Start > b.Duration);
        SetVerticesDirty();
    }

    /// <summary>대상 RectTransform 중심을 이 그래픽의 로컬 좌표로 바꾼다.</summary>
    private Vector2 LocalCenter(RectTransform target)
    {
        return rectTransform.InverseTransformPoint(target.TransformPoint(target.rect.center));
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float now = Time.unscaledTime;
        foreach (Burst burst in _bursts)
        {
            float t = (now - burst.Start) / burst.Duration;
            if (t < 0 || t > 1) continue;
            switch (burst.Kind)
            {
                case SpellGame.CastEffect.Ember: DrawEmber(vh, burst, t); break;
                case SpellGame.CastEffect.Lightning: DrawLightning(vh, burst, t); break;
                case SpellGame.CastEffect.Meteor: DrawMeteor(vh, burst, t); break;
                case SpellGame.CastEffect.Slash: DrawSlash(vh, burst, t); break;
                case SpellGame.CastEffect.Strike: DrawStrike(vh, burst.To, burst.Color, t, burst.Seed); break;
                case SpellGame.CastEffect.Buckshot: DrawBuckshot(vh, burst, t); break;
                case SpellGame.CastEffect.ShieldBash: DrawShieldBash(vh, burst, t); break;
                case SpellGame.CastEffect.Barrier: DrawBarrier(vh, burst.From, burst.Color, t); break;
                case SpellGame.CastEffect.IronWall: DrawIronWall(vh, burst.From, burst.Color, t); break;
            }
        }
    }

    /// <summary>불씨: 불덩이가 꼬리를 끌며 날아가 대상에서 불꽃으로 터진다.</summary>
    private static void DrawEmber(VertexHelper vh, Burst b, float t)
    {
        Color hot = Color.Lerp(b.Color, HOT_COLOR, 0.6f);
        if (t < 0.45f)
        {
            float u = EaseIn(t / 0.45f);
            for (int i = 5; i >= 0; i--)
            {
                Vector2 p = Vector2.Lerp(b.From, b.To, Mathf.Max(0, u - i * 0.05f));
                Circle(vh, p, 20f - i * 2.5f, Alpha(hot, 0.85f - i * 0.13f));
            }
            Circle(vh, Vector2.Lerp(b.From, b.To, u), 9f, Alpha(Color.white, 0.9f));
            return;
        }
        float v = (t - 0.45f) / 0.55f;
        Circle(vh, b.To, 30f + 40f * v, Alpha(hot, 0.45f * (1 - v)));
        Ring(vh, b.To, 24f + 90f * v, 8f * (1 - v) + 1f, Alpha(hot, 1 - v));
        for (int i = 0; i < 10; i++)
        {
            float angle = Hash(b.Seed, i) * Mathf.PI * 2, distance = 20f + 110f * v * (0.6f + 0.4f * Hash(b.Seed, i + 50));
            Circle(vh, b.To + Direction(angle) * distance, 6f * (1 - v) + 1f, Alpha(Color.Lerp(hot, Color.yellow, 0.4f), 1 - v));
        }
    }

    /// <summary>번개: 화면 위에서 대상까지 갈라지는 번개가 깜빡이며 내리꽂힌다.</summary>
    private void DrawLightning(VertexHelper vh, Burst b, float t)
    {
        float flicker = (Mathf.FloorToInt(t * 14) % 2 == 0 ? 1f : 0.55f) * (1 - t);
        Vector2 top = new Vector2(b.To.x + (Hash(b.Seed, 1) - 0.5f) * 120f, rectTransform.rect.yMax);
        int frame = Mathf.FloorToInt(t * 10), segments = 9;
        Vector2 previous = top;
        for (int i = 1; i <= segments; i++)
        {
            Vector2 point = Vector2.Lerp(top, b.To, i / (float)segments);
            if (i < segments) point.x += (Hash(b.Seed + frame, i) - 0.5f) * 70f;
            Line(vh, previous, point, 10f, Alpha(b.Color, 0.5f * flicker));
            Line(vh, previous, point, 3.5f, Alpha(BOLT_COLOR, flicker));
            previous = point;
        }
        Circle(vh, b.To, 70f * (0.6f + 0.4f * t), Alpha(BOLT_COLOR, 0.35f * (1 - t)));
    }

    /// <summary>유성: 오른쪽 위에서 불붙은 돌이 떨어져 대상에서 폭발한다.</summary>
    private static void DrawMeteor(VertexHelper vh, Burst b, float t)
    {
        Vector2 start = b.To + new Vector2(260f, 420f);
        Color hot = Color.Lerp(b.Color, HOT_COLOR, 0.5f);
        if (t < 0.5f)
        {
            float u = EaseIn(t / 0.5f);
            for (int i = 6; i >= 0; i--) Circle(vh, Vector2.Lerp(start, b.To, Mathf.Max(0, u - i * 0.045f)), 28f - i * 3f, Alpha(hot, 0.8f - i * 0.11f));
            Circle(vh, Vector2.Lerp(start, b.To, u), 16f, Alpha(new Color(0.35f, 0.25f, 0.2f), 1f));
            return;
        }
        float v = (t - 0.5f) / 0.5f;
        Circle(vh, b.To, 60f * (0.5f + v), Alpha(hot, 0.5f * (1 - v)));
        Ring(vh, b.To, 30f + 120f * v, 12f * (1 - v) + 1f, Alpha(Color.Lerp(hot, Color.white, 0.3f), 1 - v));
        for (int i = 0; i < 8; i++)
        {
            float angle = Mathf.PI * (0.1f + 0.8f * Hash(b.Seed, i)), distance = 30f + 100f * v;
            Rect(vh, b.To + Direction(angle) * distance - new Vector2(0, 60f * v * v), new Vector2(9f, 9f) * (1 - v * 0.6f), Alpha(new Color(0.45f, 0.35f, 0.3f), 1 - v));
        }
    }

    /// <summary>참격: 대상 위로 세 줄기 베기 선이 차례로 긋고 사라진다.</summary>
    private static void DrawSlash(VertexHelper vh, Burst b, float t)
    {
        float[] angles = { 35f, -28f, 78f };
        for (int i = 0; i < angles.Length; i++)
        {
            float u = (t - i * 0.14f) / 0.55f;
            if (u <= 0 || u >= 1) continue;
            Vector2 dir = Direction(angles[i] * Mathf.Deg2Rad);
            float grow = Mathf.Clamp01(u * 2.5f), fade = 1 - Mathf.Clamp01((u - 0.4f) / 0.6f);
            Vector2 a = b.To - dir * 120f, end = Vector2.Lerp(a, b.To + dir * 120f, grow);
            Line(vh, a, end, 14f * fade, Alpha(b.Color, 0.45f * fade));
            Line(vh, a, end, 4f * fade + 0.5f, Alpha(Color.white, fade));
        }
    }

    /// <summary>강타: 대상에서 충격 고리와 방사형 선이 퍼진다.</summary>
    private static void DrawStrike(VertexHelper vh, Vector2 at, Color color, float t, int seed)
    {
        float v = EaseOut(t);
        if (t < 0.2f) Circle(vh, at, 60f, Alpha(Color.white, 0.5f * (1 - t / 0.2f)));
        Ring(vh, at, 30f + 110f * v, 10f * (1 - t) + 1f, Alpha(color, 1 - t));
        for (int i = 0; i < 8; i++)
        {
            Vector2 dir = Direction(i * Mathf.PI / 4 + Hash(seed, i) * 0.3f);
            Line(vh, at + dir * (30f + 40f * v), at + dir * (70f + 90f * v), 6f * (1 - t) + 0.5f, Alpha(Color.Lerp(color, Color.white, 0.5f), 1 - t));
        }
    }

    /// <summary>산탄: 작은 탄환이 시전자에서 대상으로 날아가 불똥을 튀긴다.</summary>
    private static void DrawBuckshot(VertexHelper vh, Burst b, float t)
    {
        Vector2 spread = new Vector2((Hash(b.Seed, 3) - 0.5f) * 50f, (Hash(b.Seed, 4) - 0.5f) * 50f);
        Vector2 target = b.To + spread;
        if (t < 0.35f)
        {
            float u = t / 0.35f;
            Vector2 p = Vector2.Lerp(b.From, target, u);
            Line(vh, Vector2.Lerp(b.From, target, Mathf.Max(0, u - 0.15f)), p, 4f, Alpha(b.Color, 0.6f));
            Circle(vh, p, 7f, Alpha(Color.Lerp(b.Color, Color.white, 0.5f), 1f));
            return;
        }
        float v = (t - 0.35f) / 0.65f;
        Ring(vh, target, 10f + 35f * v, 4f * (1 - v) + 0.5f, Alpha(b.Color, 1 - v));
        for (int i = 0; i < 5; i++)
        {
            Vector2 dir = Direction(Hash(b.Seed, i + 10) * Mathf.PI * 2);
            Line(vh, target + dir * (8f + 20f * v), target + dir * (16f + 40f * v), 3f * (1 - v) + 0.5f, Alpha(Color.Lerp(b.Color, Color.white, 0.4f), 1 - v));
        }
    }

    /// <summary>방패 밀치기·강타: 공격이면 방패가 대상으로 날아가 충격파를 내고, 방어면 시전자 앞에서 방패가 커지며 빛난다.</summary>
    private static void DrawShieldBash(VertexHelper vh, Burst b, float t)
    {
        if (b.IsSelf)
        {
            float scale = 0.7f + 0.5f * EaseOut(t);
            Shield(vh, b.From + new Vector2(70f, 0), 60f * scale, Alpha(b.Color, 0.5f * (1 - t)), Alpha(Color.white, 0.9f * (1 - t)));
            Ring(vh, b.From + new Vector2(70f, 0), 50f + 70f * t, 6f * (1 - t) + 0.5f, Alpha(b.Color, 1 - t));
            return;
        }
        if (t < 0.4f)
        {
            float u = EaseIn(t / 0.4f);
            Shield(vh, Vector2.Lerp(b.From, b.To, u), 46f, Alpha(b.Color, 0.75f), Alpha(Color.white, 0.95f));
            return;
        }
        float v = (t - 0.4f) / 0.6f;
        Shield(vh, b.To, 46f * (1 + 0.3f * v), Alpha(b.Color, 0.6f * (1 - v)), Alpha(Color.white, 1 - v));
        Ring(vh, b.To, 40f + 120f * v, 10f * (1 - v) + 1f, Alpha(b.Color, 1 - v));
    }

    /// <summary>방벽: 시전자 주위로 육각 보호막이 펼쳐졌다 옅어진다.</summary>
    private static void DrawBarrier(VertexHelper vh, Vector2 at, Color color, float t)
    {
        float v = EaseOut(Mathf.Clamp01(t * 1.6f)), fade = t < 0.6f ? 1 : 1 - (t - 0.6f) / 0.4f;
        float radius = 70f + 40f * v;
        Polygon(vh, at, radius, 6, Mathf.PI / 6, Alpha(color, 0.18f * fade));
        Outline(vh, at, radius, 6, Mathf.PI / 6, 6f, Alpha(color, 0.9f * fade));
        Outline(vh, at, radius * 0.75f, 6, Mathf.PI / 6, 2.5f, Alpha(Color.white, 0.6f * fade));
    }

    /// <summary>철벽: 시전자 앞에 벽돌이 아래에서부터 쌓여 올라왔다 사라진다.</summary>
    private static void DrawIronWall(VertexHelper vh, Vector2 at, Color color, float t)
    {
        float fade = t < 0.65f ? 1 : 1 - (t - 0.65f) / 0.35f;
        Vector2 brick = new Vector2(46f, 22f), origin = at + new Vector2(80f, -55f);
        for (int row = 0; row < 4; row++)
        {
            float rise = EaseOut(Mathf.Clamp01((t - row * 0.08f) * 3f));
            float offset = row % 2 == 0 ? 0 : brick.x / 2;
            for (int column = -1; column <= 1; column++)
            {
                Vector2 center = origin + new Vector2(column * (brick.x + 3f) + offset - brick.x / 4, row * (brick.y + 3f)) - new Vector2(0, 40f * (1 - rise));
                Rect(vh, center, brick, Alpha(Color.Lerp(color, new Color(0.55f, 0.6f, 0.68f), 0.5f), rise * fade));
                Rect(vh, center + new Vector2(0, brick.y * 0.3f), new Vector2(brick.x, 3f), Alpha(Color.white, 0.4f * rise * fade));
            }
        }
    }

    /// <summary>위가 평평하고 아래가 뾰족한 방패 모양을 채우고 테두리를 그린다.</summary>
    private static void Shield(VertexHelper vh, Vector2 at, float size, Color fill, Color edge)
    {
        Vector2[] points = { new Vector2(-0.8f, 0.9f), new Vector2(0.8f, 0.9f), new Vector2(0.8f, 0.1f), new Vector2(0, -1f), new Vector2(-0.8f, 0.1f) };
        int start = vh.currentVertCount;
        AddVert(vh, at, fill);
        foreach (Vector2 p in points) AddVert(vh, at + p * size, fill);
        for (int i = 0; i < points.Length; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % points.Length);
        for (int i = 0; i < points.Length; i++) Line(vh, at + points[i] * size, at + points[(i + 1) % points.Length] * size, 4f, edge);
    }

    /// <summary>채운 원을 추가한다.</summary>
    private static void Circle(VertexHelper vh, Vector2 at, float radius, Color color) { Polygon(vh, at, radius, CIRCLE_SIDES, 0, color); }

    /// <summary>채운 정다각형을 추가한다.</summary>
    private static void Polygon(VertexHelper vh, Vector2 at, float radius, int sides, float rotation, Color color)
    {
        if (color.a <= 0.003f || radius <= 0) return;
        int start = vh.currentVertCount;
        AddVert(vh, at, color);
        for (int i = 0; i < sides; i++) AddVert(vh, at + Direction(rotation + i * Mathf.PI * 2 / sides) * radius, color);
        for (int i = 0; i < sides; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % sides);
    }

    /// <summary>정다각형 테두리를 지정 두께로 추가한다.</summary>
    private static void Outline(VertexHelper vh, Vector2 at, float radius, int sides, float rotation, float width, Color color)
    {
        for (int i = 0; i < sides; i++)
            Line(vh, at + Direction(rotation + i * Mathf.PI * 2 / sides) * radius, at + Direction(rotation + (i + 1) * Mathf.PI * 2 / sides) * radius, width, color);
    }

    /// <summary>지정 두께의 고리를 추가한다.</summary>
    private static void Ring(VertexHelper vh, Vector2 at, float radius, float width, Color color)
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
    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        if (color.a <= 0.003f || width <= 0 || (b - a).sqrMagnitude < 0.01f) return;
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width / 2;
        int start = vh.currentVertCount;
        AddVert(vh, a + n, color); AddVert(vh, b + n, color); AddVert(vh, b - n, color); AddVert(vh, a - n, color);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>중심과 크기로 축 정렬 사각형을 추가한다.</summary>
    private static void Rect(VertexHelper vh, Vector2 center, Vector2 size, Color color)
    {
        if (color.a <= 0.003f) return;
        Vector2 h = size / 2;
        int start = vh.currentVertCount;
        AddVert(vh, center + new Vector2(-h.x, -h.y), color); AddVert(vh, center + new Vector2(-h.x, h.y), color);
        AddVert(vh, center + new Vector2(h.x, h.y), color); AddVert(vh, center + new Vector2(h.x, -h.y), color);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    /// <summary>정점 하나를 위치와 색으로 추가한다.</summary>
    private static void AddVert(VertexHelper vh, Vector2 position, Color color)
    {
        UIVertex vertex = UIVertex.simpleVert; vertex.position = position; vertex.color = color; vh.AddVert(vertex);
    }

    /// <summary>각도(라디안)의 단위 방향을 반환한다.</summary>
    private static Vector2 Direction(float angle) { return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); }

    /// <summary>색의 알파만 바꾼 색을 반환한다.</summary>
    private static Color Alpha(Color color, float alpha) { color.a = Mathf.Clamp01(alpha); return color; }

    /// <summary>천천히 시작해 빨라지는 보간 값이다.</summary>
    private static float EaseIn(float t) { return t * t; }

    /// <summary>빠르게 시작해 느려지는 보간 값이다.</summary>
    private static float EaseOut(float t) { return 1 - (1 - t) * (1 - t); }

    /// <summary>씨앗과 번호로 0~1 사이의 고정 난수를 반환한다. 같은 연출은 프레임마다 같은 모양을 유지한다.</summary>
    private static float Hash(int seed, int index)
    {
        unchecked
        {
            uint h = (uint)(seed * 73856093) ^ (uint)(index * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
