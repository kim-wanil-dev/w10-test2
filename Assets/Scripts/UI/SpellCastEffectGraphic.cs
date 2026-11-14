using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using D = SpellDraw;

/// <summary>전투 화면 위에 도안 시전 연출을 절차적 2D 도형(원, 고리, 선, 다각형)과 빛 번짐으로 그린다. 입력은 받지 않는다.
/// 빛처럼 보여야 하는 부분은 가산 혼합으로 더해 어두운 바탕 위에서 번지게 하고, 돌·벽돌·카드처럼 단단한 것은 보통 혼합으로 그린다.</summary>
public sealed class SpellCastEffectGraphic : MaskableGraphic
{
    public const float HIT_STAGGER = 0.09f;
    private const float HIT_DURATION = 0.75f;
    private const float SELF_DURATION = 0.9f;
    private const float TRAIT_DURATION = 0.85f;
    // 파편 씰링 연출이 시작된 뒤 적에 닿기까지의 시간이다(연출 길이의 40%).
    public const float SHARD_IMPACT_SECONDS = TRAIT_DURATION * 0.4f;
    private static readonly Color HOT_COLOR = new Color(1f, 0.62f, 0.25f);
    private static readonly Color BOLT_COLOR = new Color(0.75f, 0.9f, 1f);

    /// <summary>코팅·씰링이 발동할 때 그리는 연출 종류다. AmplifyHit·CogHit은 강화된 타격을 맞은 적 위에 그린다.</summary>
    public enum Trait { None, Guard, Amplify, AmplifyHit, Cog, CogHit, Shard, Refill, Recall, Overpaint }

    /// <summary>시전이 아닌 순간의 작은 반응 연출이다. Place는 조각을 놓았을 때, Complete는 도안이 완성됐을 때, Hurt는 플레이어가 맞았을 때 쓴다.</summary>
    public enum Fx { None, Place, Complete, Hurt }

    /// <summary>연출 하나의 종류, 시작 시각, 길이, 시전자·대상 위치(로컬), 색과 무작위 씨앗이다. TraitKind·FxKind가 None이 아니면 그 연출이다.</summary>
    private struct Burst
    {
        public Trait TraitKind;
        public Fx FxKind;
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

    /// <summary>도안 연출이 시작된 뒤 대상에 닿기까지의 시간(초)이다. 적이 맞는 반응과 체력 감소를 이 순간에 맞춘다.</summary>
    public static float ImpactDelay(SpellGame.CastEffect effect)
    {
        switch (effect)
        {
            case SpellGame.CastEffect.Ember: return HIT_DURATION * 0.45f;
            case SpellGame.CastEffect.Meteor: return HIT_DURATION * 0.5f;
            case SpellGame.CastEffect.ShieldBash: return HIT_DURATION * 0.4f;
            case SpellGame.CastEffect.Buckshot:
            case SpellGame.CastEffect.Barrier:
            case SpellGame.CastEffect.IronWall: return HIT_DURATION * 0.35f;
            case SpellGame.CastEffect.Slash: return 0.1f;
            case SpellGame.CastEffect.Lightning:
            case SpellGame.CastEffect.Strike: return 0.04f;
            default: return 0.05f;
        }
    }

    /// <summary>여러 대상을 차례로 칠 때 타격 사이의 간격(초)이다.</summary>
    public static float HitStagger(SpellGame.CastEffect effect)
    {
        return effect == SpellGame.CastEffect.Meteor ? HIT_STAGGER * 1.4f : HIT_STAGGER;
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        if (SpellStyle.GlowMaterial != null) material = SpellStyle.GlowMaterial;
    }

    /// <summary>시전 연출을 delay초 뒤에 시작한다. caster는 시전자, hitTargets는 실제로 맞은 적의 화면 RectTransform을 타격 순서대로 담는다. gainedBlock이면 시전자에게 방어 연출을 더한다.</summary>
    public void Play(SpellGame.CastEffect effect, RectTransform caster, IReadOnlyList<RectTransform> hitTargets, bool gainedBlock, Color color, float delay = 0)
    {
        if (effect == SpellGame.CastEffect.None || effect == SpellGame.CastEffect.Auto) return;
        float now = Time.unscaledTime + delay;
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
        float stagger = HitStagger(hitKind);
        for (int i = 0; i < hitTargets.Count; i++)
        {
            if (hitTargets[i] == null) continue;
            _bursts.Add(new Burst { Kind = hitKind, Start = now + i * stagger, Duration = HIT_DURATION, From = from, To = LocalCenter(hitTargets[i]), Color = color, Seed = _nextSeed++ });
        }
        SetVerticesDirty();
    }

    /// <summary>코팅·씰링 연출 하나를 delay초 뒤에 시작한다. from에서 to로 향하며, to가 없으면 from 자리에서 재생하고 handFallback이면 화면 아래 손패 쪽을 향한다.</summary>
    public void PlayTrait(Trait kind, RectTransform from, RectTransform to, float delay, Color color, bool handFallback = false)
    {
        if (kind == Trait.None || from == null) return;
        Vector2 start = LocalCenter(from);
        Vector2 end = to != null ? LocalCenter(to) : handFallback ? new Vector2(rectTransform.rect.center.x, rectTransform.rect.yMin + 110f) : start;
        _bursts.Add(new Burst { TraitKind = kind, Start = Time.unscaledTime + delay, Duration = TRAIT_DURATION, From = start, To = end, Color = color, Seed = _nextSeed++ });
        SetVerticesDirty();
    }

    /// <summary>작은 반응 연출을 대상 RectTransform 중심에서 delay초 뒤에 재생한다.</summary>
    public void PlayFx(Fx kind, RectTransform target, Color color, float delay = 0)
    {
        if (kind == Fx.None || target == null) return;
        AddFx(kind, LocalCenter(target), color, delay);
    }

    /// <summary>작은 반응 연출을 화면 좌표에서 delay초 뒤에 재생한다. 3D 판 위 조각처럼 UI가 아닌 대상에 쓴다.</summary>
    public void PlayFxAtScreen(Fx kind, Vector2 screenPoint, Color color, float delay = 0)
    {
        if (kind == Fx.None) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, null, out Vector2 local);
        AddFx(kind, local, color, delay);
    }

    private void AddFx(Fx kind, Vector2 at, Color color, float delay)
    {
        float duration = kind == Fx.Complete ? 0.8f : kind == Fx.Hurt ? 0.45f : 0.42f;
        _bursts.Add(new Burst { FxKind = kind, Start = Time.unscaledTime + delay, Duration = duration, From = at, To = at, Color = color, Seed = _nextSeed++ });
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
        D.Additive = 0;
        foreach (Burst burst in _bursts)
        {
            float t = (now - burst.Start) / burst.Duration;
            if (t < 0 || t > 1) continue;
            if (burst.FxKind != Fx.None) { DrawFx(vh, burst, t); continue; }
            if (burst.TraitKind != Trait.None) { DrawTrait(vh, burst, t); continue; }
            switch (burst.Kind)
            {
                case SpellGame.CastEffect.Ember: DrawEmber(vh, burst, t); break;
                case SpellGame.CastEffect.Lightning: DrawLightning(vh, burst, t); break;
                case SpellGame.CastEffect.Meteor: DrawMeteor(vh, burst, t); break;
                case SpellGame.CastEffect.Slash: DrawSlash(vh, burst, t); break;
                case SpellGame.CastEffect.Strike: DrawStrike(vh, burst.To, burst.Color, t, burst.Seed); break;
                case SpellGame.CastEffect.Buckshot: DrawBuckshot(vh, burst, t); break;
                case SpellGame.CastEffect.ShieldBash: DrawShieldBash(vh, burst, t); break;
                case SpellGame.CastEffect.Barrier: DrawBarrier(vh, burst.From, burst.Color, t, burst.Seed); break;
                case SpellGame.CastEffect.IronWall: DrawIronWall(vh, burst.From, burst.Color, t); break;
            }
        }
        D.Additive = 0;
    }

    /// <summary>불씨: 불덩이가 빛나는 꼬리를 끌며 날아가 대상에서 불꽃으로 터진다.</summary>
    private static void DrawEmber(VertexHelper vh, Burst b, float t)
    {
        Color hot = Color.Lerp(b.Color, HOT_COLOR, 0.6f);
        if (t < 0.45f)
        {
            float u = D.EaseIn(t / 0.45f);
            Vector2 head = Vector2.Lerp(b.From, b.To, u);
            D.Glow(vh, head, 78f, D.Alpha(hot, 0.6f));
            D.Additive = 1;
            for (int i = 5; i >= 0; i--)
            {
                Vector2 p = Vector2.Lerp(b.From, b.To, Mathf.Max(0, u - i * 0.05f));
                D.Circle(vh, p, 20f - i * 2.5f, D.Alpha(hot, 0.7f - i * 0.11f));
            }
            D.Circle(vh, head, 9f, D.Alpha(Color.white, 0.95f));
            D.Additive = 0;
            return;
        }
        float v = (t - 0.45f) / 0.55f;
        float burst = (1 - v) * (1 - v);
        D.Glow(vh, b.To, 110f + 150f * v, D.Alpha(hot, 0.85f * burst));
        D.Additive = 1;
        D.Circle(vh, b.To, 26f + 34f * v, D.Alpha(Color.Lerp(hot, Color.white, 0.5f), 0.7f * burst));
        D.Ring(vh, b.To, 24f + 100f * D.EaseOut(v), 9f * (1 - v) + 1f, D.Alpha(hot, 1 - v));
        for (int i = 0; i < 12; i++)
        {
            float angle = D.Hash(b.Seed, i) * Mathf.PI * 2, reach = 0.6f + 0.4f * D.Hash(b.Seed, i + 50);
            Vector2 dir = D.Direction(angle);
            // 불똥은 밖으로 튀다가 중력에 끌려 아래로 휜다.
            Vector2 at = b.To + dir * (20f + 130f * D.EaseOut(v) * reach) - new Vector2(0, 70f * v * v);
            D.Line(vh, at - dir * (14f * (1 - v) + 2f), at, 4f * (1 - v) + 1f, D.Alpha(Color.Lerp(hot, Color.yellow, 0.45f), 1 - v));
        }
        D.Additive = 0;
    }

    /// <summary>번개: 화면 위에서 대상까지 갈라지는 번개가 깜빡이며 내리꽂히고 주변을 밝힌다.</summary>
    private void DrawLightning(VertexHelper vh, Burst b, float t)
    {
        float flicker = (Mathf.FloorToInt(t * 14) % 2 == 0 ? 1f : 0.55f) * (1 - t);
        Vector2 top = new Vector2(b.To.x + (D.Hash(b.Seed, 1) - 0.5f) * 120f, rectTransform.rect.yMax);
        int frame = Mathf.FloorToInt(t * 10), segments = 9;
        D.Glow(vh, b.To, 190f, D.Alpha(BOLT_COLOR, 0.7f * flicker));
        Vector2 previous = top;
        for (int i = 1; i <= segments; i++)
        {
            Vector2 point = Vector2.Lerp(top, b.To, i / (float)segments);
            if (i < segments) point.x += (D.Hash(b.Seed + frame, i) - 0.5f) * 70f;
            D.GlowLine(vh, previous, point, 64f, D.Alpha(b.Color, 0.5f * flicker));
            D.Additive = 1;
            D.Line(vh, previous, point, 10f, D.Alpha(b.Color, 0.5f * flicker));
            D.Line(vh, previous, point, 3.5f, D.Alpha(Color.white, flicker));
            // 가운데쯤에서 옆으로 갈라지는 잔가지 하나를 낸다.
            if (i == 4 || i == 6)
            {
                Vector2 branch = point + new Vector2((D.Hash(b.Seed + frame, i + 30) - 0.5f) * 180f, -60f - 50f * D.Hash(b.Seed, i + 40));
                D.Line(vh, point, branch, 2.5f, D.Alpha(BOLT_COLOR, 0.8f * flicker));
            }
            D.Additive = 0;
            previous = point;
        }
        D.Additive = 1;
        D.Circle(vh, b.To, 70f * (0.6f + 0.4f * t), D.Alpha(BOLT_COLOR, 0.35f * (1 - t)));
        D.Ring(vh, b.To, 30f + 120f * D.EaseOut(t), 5f * (1 - t) + 0.5f, D.Alpha(BOLT_COLOR, 0.8f * (1 - t)));
        D.Additive = 0;
    }

    /// <summary>유성: 오른쪽 위에서 불붙은 돌이 떨어져 대상에서 폭발한다.</summary>
    private static void DrawMeteor(VertexHelper vh, Burst b, float t)
    {
        Vector2 start = b.To + new Vector2(260f, 420f);
        Color hot = Color.Lerp(b.Color, HOT_COLOR, 0.5f);
        if (t < 0.5f)
        {
            float u = D.EaseIn(t / 0.5f);
            Vector2 head = Vector2.Lerp(start, b.To, u);
            D.Glow(vh, head, 110f, D.Alpha(hot, 0.6f));
            D.Additive = 1;
            for (int i = 6; i >= 1; i--) D.Circle(vh, Vector2.Lerp(start, b.To, Mathf.Max(0, u - i * 0.045f)), 28f - i * 3f, D.Alpha(hot, 0.7f - i * 0.1f));
            D.Additive = 0;
            D.Circle(vh, head, 18f, D.Alpha(new Color(0.35f, 0.25f, 0.2f), 1f));
            D.Additive = 1;
            D.Ring(vh, head, 20f, 5f, D.Alpha(Color.Lerp(hot, Color.white, 0.4f), 0.9f));
            D.Additive = 0;
            return;
        }
        float v = (t - 0.5f) / 0.5f;
        float burst = (1 - v) * (1 - v);
        D.Glow(vh, b.To, 150f + 190f * v, D.Alpha(hot, 0.95f * burst));
        D.Additive = 1;
        D.Circle(vh, b.To, 60f * (0.5f + v), D.Alpha(Color.Lerp(hot, Color.white, 0.4f), 0.6f * burst));
        D.Ring(vh, b.To, 30f + 150f * D.EaseOut(v), 13f * (1 - v) + 1f, D.Alpha(Color.Lerp(hot, Color.white, 0.3f), 1 - v));
        D.Ring(vh, b.To, 16f + 90f * D.EaseOut(v), 5f * (1 - v) + 0.5f, D.Alpha(Color.white, 0.7f * (1 - v)));
        D.Additive = 0;
        for (int i = 0; i < 9; i++)
        {
            float angle = Mathf.PI * (0.08f + 0.84f * D.Hash(b.Seed, i)), distance = 30f + 130f * D.EaseOut(v) * (0.6f + 0.4f * D.Hash(b.Seed, i + 9));
            D.Quad(vh, b.To + D.Direction(angle) * distance - new Vector2(0, 90f * v * v), new Vector2(11f, 11f) * (1 - v * 0.6f), v * 6f + i, D.Alpha(new Color(0.45f, 0.35f, 0.3f), 1 - v));
        }
    }

    /// <summary>참격: 대상 위로 세 줄기 베기 선이 차례로 빛을 남기며 긋고 사라진다.</summary>
    private static void DrawSlash(VertexHelper vh, Burst b, float t)
    {
        float[] angles = { 35f, -28f, 78f };
        for (int i = 0; i < angles.Length; i++)
        {
            float u = (t - i * 0.14f) / 0.55f;
            if (u <= 0 || u >= 1) continue;
            Vector2 dir = D.Direction(angles[i] * Mathf.Deg2Rad);
            float grow = Mathf.Clamp01(u * 2.5f), fade = 1 - Mathf.Clamp01((u - 0.4f) / 0.6f);
            Vector2 a = b.To - dir * 130f, end = Vector2.Lerp(a, b.To + dir * 130f, grow);
            D.GlowLine(vh, a, end, 78f * fade + 8f, D.Alpha(b.Color, 0.55f * fade));
            D.Additive = 1;
            D.Line(vh, a, end, 12f * fade, D.Alpha(b.Color, 0.6f * fade));
            D.Line(vh, a, end, 4f * fade + 0.5f, D.Alpha(Color.white, fade));
            D.Additive = 0;
        }
    }

    /// <summary>강타: 대상에서 섬광과 충격 고리, 방사형 선이 퍼진다.</summary>
    private static void DrawStrike(VertexHelper vh, Vector2 at, Color color, float t, int seed)
    {
        float v = D.EaseOut(t);
        D.Glow(vh, at, 120f + 110f * v, D.Alpha(Color.Lerp(color, Color.white, 0.3f), 0.9f * (1 - t) * (1 - t)));
        D.Additive = 1;
        if (t < 0.2f) D.Circle(vh, at, 60f, D.Alpha(Color.white, 0.6f * (1 - t / 0.2f)));
        D.Ring(vh, at, 30f + 120f * v, 10f * (1 - t) + 1f, D.Alpha(color, 1 - t));
        for (int i = 0; i < 8; i++)
        {
            Vector2 dir = D.Direction(i * Mathf.PI / 4 + D.Hash(seed, i) * 0.3f);
            D.Line(vh, at + dir * (30f + 40f * v), at + dir * (70f + 100f * v), 6f * (1 - t) + 0.5f, D.Alpha(Color.Lerp(color, Color.white, 0.5f), 1 - t));
        }
        D.Additive = 0;
    }

    /// <summary>산탄: 작은 탄환이 시전자에서 대상으로 날아가 불똥을 튀긴다.</summary>
    private static void DrawBuckshot(VertexHelper vh, Burst b, float t)
    {
        Vector2 spread = new Vector2((D.Hash(b.Seed, 3) - 0.5f) * 50f, (D.Hash(b.Seed, 4) - 0.5f) * 50f);
        Vector2 target = b.To + spread;
        if (t < 0.35f)
        {
            float u = t / 0.35f;
            Vector2 p = Vector2.Lerp(b.From, target, u);
            D.Glow(vh, p, 34f, D.Alpha(b.Color, 0.6f));
            D.Additive = 1;
            D.Line(vh, Vector2.Lerp(b.From, target, Mathf.Max(0, u - 0.18f)), p, 4f, D.Alpha(b.Color, 0.7f));
            D.Circle(vh, p, 7f, D.Alpha(Color.Lerp(b.Color, Color.white, 0.6f), 1f));
            D.Additive = 0;
            return;
        }
        float v = (t - 0.35f) / 0.65f;
        D.Glow(vh, target, 46f + 50f * v, D.Alpha(b.Color, 0.7f * (1 - v) * (1 - v)));
        D.Additive = 1;
        D.Ring(vh, target, 10f + 38f * D.EaseOut(v), 4f * (1 - v) + 0.5f, D.Alpha(b.Color, 1 - v));
        for (int i = 0; i < 6; i++)
        {
            Vector2 dir = D.Direction(D.Hash(b.Seed, i + 10) * Mathf.PI * 2);
            D.Line(vh, target + dir * (8f + 22f * v), target + dir * (16f + 46f * v), 3f * (1 - v) + 0.5f, D.Alpha(Color.Lerp(b.Color, Color.white, 0.45f), 1 - v));
        }
        D.Additive = 0;
    }

    /// <summary>방패 밀치기·강타: 공격이면 방패가 대상으로 날아가 충격파를 내고, 방어면 시전자 앞에서 방패가 커지며 빛난다.</summary>
    private static void DrawShieldBash(VertexHelper vh, Burst b, float t)
    {
        if (b.IsSelf)
        {
            float scale = 0.7f + 0.5f * D.EaseOut(t);
            Vector2 at = b.From + new Vector2(70f, 0);
            D.Glow(vh, at, 130f, D.Alpha(b.Color, 0.45f * (1 - t)));
            D.Shield(vh, at, 60f * scale, D.Alpha(b.Color, 0.5f * (1 - t)), D.Alpha(Color.white, 0.9f * (1 - t)));
            D.Additive = 1;
            D.Ring(vh, at, 50f + 70f * t, 6f * (1 - t) + 0.5f, D.Alpha(b.Color, 1 - t));
            D.Additive = 0;
            return;
        }
        if (t < 0.4f)
        {
            float u = D.EaseIn(t / 0.4f);
            Vector2 p = Vector2.Lerp(b.From, b.To, u);
            D.Glow(vh, p, 80f, D.Alpha(b.Color, 0.5f));
            D.Shield(vh, p, 46f, D.Alpha(b.Color, 0.8f), D.Alpha(Color.white, 0.95f));
            return;
        }
        float v = (t - 0.4f) / 0.6f;
        D.Glow(vh, b.To, 110f + 120f * v, D.Alpha(b.Color, 0.8f * (1 - v) * (1 - v)));
        D.Shield(vh, b.To, 46f * (1 + 0.3f * v), D.Alpha(b.Color, 0.6f * (1 - v)), D.Alpha(Color.white, 1 - v));
        D.Additive = 1;
        D.Ring(vh, b.To, 40f + 130f * D.EaseOut(v), 10f * (1 - v) + 1f, D.Alpha(b.Color, 1 - v));
        D.Additive = 0;
    }

    /// <summary>방벽: 시전자 주위로 빛나는 육각 보호막이 펼쳐졌다 옅어지고, 꼭짓점에서 빛 조각이 흩어진다.</summary>
    private static void DrawBarrier(VertexHelper vh, Vector2 at, Color color, float t, int seed)
    {
        float v = D.EaseOut(Mathf.Clamp01(t * 1.6f)), fade = t < 0.6f ? 1 : 1 - (t - 0.6f) / 0.4f;
        float radius = 70f + 44f * v;
        D.Glow(vh, at, radius * 1.9f, D.Alpha(color, 0.4f * fade));
        D.Additive = 1;
        D.Polygon(vh, at, radius, 6, Mathf.PI / 6, D.Alpha(color, 0.16f * fade));
        D.Outline(vh, at, radius, 6, Mathf.PI / 6, 6f, D.Alpha(color, 0.95f * fade));
        D.Outline(vh, at, radius * 0.75f, 6, Mathf.PI / 6, 2.5f, D.Alpha(Color.white, 0.6f * fade));
        for (int i = 0; i < 6; i++)
        {
            Vector2 dir = D.Direction(Mathf.PI / 6 + i * Mathf.PI / 3);
            Vector2 spark = at + dir * (radius + 26f * t * (0.5f + D.Hash(seed, i)));
            D.Polygon(vh, spark, 6f * fade, 4, t * 4f + i, D.Alpha(Color.Lerp(color, Color.white, 0.6f), fade));
        }
        D.Additive = 0;
    }

    /// <summary>철벽: 시전자 앞에 벽돌이 아래에서부터 쌓여 올라왔다 사라진다.</summary>
    private static void DrawIronWall(VertexHelper vh, Vector2 at, Color color, float t)
    {
        float fade = t < 0.65f ? 1 : 1 - (t - 0.65f) / 0.35f;
        Vector2 brick = new Vector2(46f, 22f), origin = at + new Vector2(80f, -55f);
        D.Glow(vh, origin + new Vector2(0, 40f), 170f, D.Alpha(color, 0.38f * fade));
        for (int row = 0; row < 4; row++)
        {
            float rise = D.EaseOut(Mathf.Clamp01((t - row * 0.08f) * 3f));
            float offset = row % 2 == 0 ? 0 : brick.x / 2;
            for (int column = -1; column <= 1; column++)
            {
                Vector2 center = origin + new Vector2(column * (brick.x + 3f) + offset - brick.x / 4, row * (brick.y + 3f)) - new Vector2(0, 40f * (1 - rise));
                D.Box(vh, center, brick, D.Alpha(Color.Lerp(color, new Color(0.55f, 0.6f, 0.68f), 0.5f), rise * fade));
                D.Box(vh, center + new Vector2(0, brick.y * 0.3f), new Vector2(brick.x, 3f), D.Alpha(Color.white, 0.45f * rise * fade));
            }
        }
    }

    /// <summary>코팅·씰링 연출을 종류별로 그린다.</summary>
    private static void DrawTrait(VertexHelper vh, Burst b, float t)
    {
        switch (b.TraitKind)
        {
            case Trait.Guard: DrawGuard(vh, b, t); break;
            case Trait.Amplify: DrawAmplify(vh, b, t); break;
            case Trait.AmplifyHit: DrawAmplifyHit(vh, b, t); break;
            case Trait.Cog: DrawCog(vh, b.From, b.Color, t, 34f, 1f); break;
            case Trait.CogHit: DrawCog(vh, b.To + new Vector2(38f, 38f), b.Color, t, 20f, -1.6f); break;
            case Trait.Shard: DrawShard(vh, b, t); break;
            case Trait.Refill: DrawFlyingCard(vh, b, t, false); break;
            case Trait.Recall: DrawFlyingCard(vh, b, t, true); break;
            case Trait.Overpaint: DrawOverpaint(vh, b, t); break;
        }
    }

    /// <summary>수호 코팅: 작은 방패가 도안에서 시전자에게 날아가 붙으며 반짝인다.</summary>
    private static void DrawGuard(VertexHelper vh, Burst b, float t)
    {
        Vector2 target = b.To + new Vector2(-40f, 30f);
        if (t < 0.45f)
        {
            float u = D.EaseOut(t / 0.45f);
            Vector2 p = Vector2.Lerp(b.From, target, u) + Vector2.up * Mathf.Sin(u * Mathf.PI) * 70f;
            D.Glow(vh, p, 44f, D.Alpha(b.Color, 0.5f));
            D.Shield(vh, p, 20f, D.Alpha(b.Color, 0.8f), D.Alpha(Color.white, 0.95f));
            return;
        }
        float v = (t - 0.45f) / 0.55f;
        D.Glow(vh, target, 60f + 30f * v, D.Alpha(b.Color, 0.6f * (1 - v)));
        D.Shield(vh, target, 20f + 12f * D.EaseOut(v), D.Alpha(b.Color, 0.6f * (1 - v)), D.Alpha(Color.white, 1 - v));
        D.Additive = 1;
        D.Ring(vh, target, 24f + 50f * v, 5f * (1 - v) + 0.5f, D.Alpha(b.Color, 1 - v));
        D.Additive = 0;
    }

    /// <summary>증폭 코팅: 도안 자리에서 위쪽 화살 무늬가 겹겹이 솟고 고리가 퍼진다.</summary>
    private static void DrawAmplify(VertexHelper vh, Burst b, float t)
    {
        float fade = 1 - Mathf.Clamp01((t - 0.55f) / 0.45f);
        D.Glow(vh, b.From + new Vector2(0, 30f), 90f, D.Alpha(b.Color, 0.45f * fade));
        D.Additive = 1;
        D.Ring(vh, b.From, 26f + 60f * D.EaseOut(t), 6f * (1 - t) + 0.5f, D.Alpha(b.Color, 0.9f * (1 - t)));
        for (int i = 0; i < 3; i++)
        {
            float u = Mathf.Clamp01((t - i * 0.12f) / 0.6f);
            if (u <= 0) continue;
            Vector2 tip = b.From + new Vector2(0, 10f + 70f * D.EaseOut(u) + i * 4f);
            float alpha = fade * (1 - u * 0.5f);
            D.Line(vh, tip + new Vector2(-24f, -16f), tip, 7f, D.Alpha(b.Color, alpha));
            D.Line(vh, tip, tip + new Vector2(24f, -16f), 7f, D.Alpha(b.Color, alpha));
            D.Line(vh, tip + new Vector2(-18f, -12f), tip, 2.5f, D.Alpha(Color.white, alpha));
            D.Line(vh, tip, tip + new Vector2(18f, -12f), 2.5f, D.Alpha(Color.white, alpha));
        }
        D.Additive = 0;
    }

    /// <summary>증폭 타격: 맞은 적 위에 마름모 테두리와 십자 섬광이 커지며 강화된 피해를 보여 준다.</summary>
    private static void DrawAmplifyHit(VertexHelper vh, Burst b, float t)
    {
        float v = D.EaseOut(t);
        D.Glow(vh, b.To, 90f + 60f * v, D.Alpha(b.Color, 0.5f * (1 - t)));
        D.Additive = 1;
        D.Outline(vh, b.To, 40f + 50f * v, 4, 0, 5f * (1 - t) + 0.5f, D.Alpha(b.Color, 1 - t));
        float arm = 30f + 40f * v;
        D.Line(vh, b.To + Vector2.left * arm, b.To + Vector2.right * arm, 4f * (1 - t) + 0.5f, D.Alpha(Color.white, 0.9f * (1 - t)));
        D.Line(vh, b.To + Vector2.down * arm, b.To + Vector2.up * arm, 4f * (1 - t) + 0.5f, D.Alpha(Color.white, 0.9f * (1 - t)));
        D.Additive = 0;
    }

    /// <summary>톱니 코팅: 톱니바퀴가 커지며 돈다. spin이 음수면 반대로 돈다.</summary>
    private static void DrawCog(VertexHelper vh, Vector2 at, Color color, float t, float radius, float spin)
    {
        float scale = D.EaseOut(Mathf.Clamp01(t * 2.5f)), fade = 1 - Mathf.Clamp01((t - 0.6f) / 0.4f);
        float r = radius * scale, angle = t * Mathf.PI * 2.2f * spin;
        D.Glow(vh, at, r * 2.4f, D.Alpha(color, 0.4f * fade));
        Color body = D.Alpha(Color.Lerp(color, new Color(0.55f, 0.55f, 0.6f), 0.35f), 0.9f * fade);
        D.Circle(vh, at, r, body);
        for (int i = 0; i < 8; i++)
        {
            Vector2 dir = D.Direction(angle + i * Mathf.PI / 4);
            D.Line(vh, at + dir * r * 0.8f, at + dir * r * 1.35f, r * 0.42f, body);
        }
        D.Circle(vh, at, r * 0.42f, D.Alpha(new Color(0.08f, 0.09f, 0.12f), 0.9f * fade));
        D.Ring(vh, at, r * 0.42f, 2.5f, D.Alpha(Color.white, 0.7f * fade));
    }

    /// <summary>파편 씰링: 길쭉한 수정 조각이 도안에서 적에게 날아가 산산이 부서진다.</summary>
    private static void DrawShard(VertexHelper vh, Burst b, float t)
    {
        Vector2 target = b.To + new Vector2((D.Hash(b.Seed, 1) - 0.5f) * 40f, (D.Hash(b.Seed, 2) - 0.5f) * 40f);
        Vector2 dir = (target - b.From).sqrMagnitude > 1 ? (target - b.From).normalized : Vector2.right;
        if (t < 0.4f)
        {
            float u = D.EaseIn(t / 0.4f);
            Vector2 p = Vector2.Lerp(b.From, target, u);
            D.Glow(vh, p, 40f, D.Alpha(b.Color, 0.6f));
            D.GlowLine(vh, Vector2.Lerp(b.From, target, Mathf.Max(0, u - 0.2f)), p, 22f, D.Alpha(b.Color, 0.6f));
            D.Crystal(vh, p, dir, 22f, 8f, D.Alpha(Color.Lerp(b.Color, Color.white, 0.45f), 1f));
            return;
        }
        float v = (t - 0.4f) / 0.6f;
        D.Glow(vh, target, 60f + 50f * v, D.Alpha(b.Color, 0.7f * (1 - v) * (1 - v)));
        D.Additive = 1;
        D.Circle(vh, target, 26f * (1 - v), D.Alpha(Color.white, 0.6f * (1 - v)));
        D.Additive = 0;
        for (int i = 0; i < 7; i++)
        {
            Vector2 out_ = D.Direction(D.Hash(b.Seed, i + 20) * Mathf.PI * 2);
            D.Crystal(vh, target + out_ * (12f + 70f * D.EaseOut(v)) - new Vector2(0, 30f * v * v), out_, 10f * (1 - v * 0.5f), 4f, D.Alpha(Color.Lerp(b.Color, Color.white, 0.25f), 1 - v));
        }
    }

    /// <summary>보충·회수 씰링: 카드가 호를 그리며 손패 쪽으로 날아간다. 회수는 출발점에 되돌림 고리를 함께 그린다.</summary>
    private static void DrawFlyingCard(VertexHelper vh, Burst b, float t, bool isRecall)
    {
        if (isRecall)
        {
            float spin = t * Mathf.PI * 3;
            float fade = 1 - Mathf.Clamp01(t / 0.6f);
            D.Additive = 1;
            for (int i = 0; i < 12; i++)
            {
                float a0 = spin + i * Mathf.PI * 1.5f / 12, a1 = spin + (i + 1) * Mathf.PI * 1.5f / 12;
                D.Line(vh, b.From + D.Direction(a0) * 40f, b.From + D.Direction(a1) * 40f, 5f, D.Alpha(b.Color, fade));
            }
            Vector2 head = b.From + D.Direction(spin + Mathf.PI * 1.5f) * 40f, tangent = D.Direction(spin + Mathf.PI * 2f);
            D.Polygon(vh, head + tangent * 6f, 11f, 3, spin + Mathf.PI * 2f, D.Alpha(b.Color, fade));
            D.Additive = 0;
        }
        float u = D.EaseOut(Mathf.Clamp01((t - (isRecall ? 0.15f : 0f)) / 0.7f));
        if (u <= 0) return;
        Vector2 control = (b.From + b.To) / 2 + Vector2.up * 160f;
        Vector2 p = Vector2.Lerp(Vector2.Lerp(b.From, control, u), Vector2.Lerp(control, b.To, u), u);
        float alpha = 1 - Mathf.Clamp01((u - 0.8f) / 0.2f);
        D.Glow(vh, p, 50f, D.Alpha(b.Color, 0.5f * alpha));
        D.Additive = 1;
        for (int i = 1; i <= 4; i++)
        {
            float w = Mathf.Max(0, u - i * 0.06f);
            Vector2 trail = Vector2.Lerp(Vector2.Lerp(b.From, control, w), Vector2.Lerp(control, b.To, w), w);
            D.Circle(vh, trail, 6f - i, D.Alpha(b.Color, 0.5f * alpha * (1 - i * 0.2f)));
        }
        D.Additive = 0;
        float tilt = (1 - u) * 0.6f * (b.To.x >= b.From.x ? -1 : 1);
        D.Quad(vh, p, new Vector2(34f, 46f), tilt, D.Alpha(isRecall ? Color.Lerp(b.Color, Color.white, 0.2f) : b.Color, 0.9f * alpha));
        D.Quad(vh, p, new Vector2(24f, 36f), tilt, D.Alpha(new Color(0.1f, 0.11f, 0.15f), 0.85f * alpha));
        if (u > 0.85f)
        {
            D.Additive = 1;
            D.Ring(vh, b.To, 20f + 120f * (u - 0.85f), 3f, D.Alpha(b.Color, (1 - u) * 5f));
            D.Additive = 0;
        }
    }

    /// <summary>덧칠 씰링: 굵은 붓질 세 줄이 도안 자리를 가로질러 칠해지고 물감 방울이 떨어진다.</summary>
    private static void DrawOverpaint(VertexHelper vh, Burst b, float t)
    {
        float fade = 1 - Mathf.Clamp01((t - 0.6f) / 0.4f);
        for (int stroke = 0; stroke < 3; stroke++)
        {
            float u = Mathf.Clamp01((t - stroke * 0.1f) / 0.4f);
            if (u <= 0) continue;
            float y = (stroke - 1) * 22f, segments = 10;
            Vector2 previous = b.From + new Vector2(-70f, y);
            for (int i = 1; i <= segments * u; i++)
            {
                float x = -70f + 140f * i / segments;
                Vector2 point = b.From + new Vector2(x, y + Mathf.Sin(i * 0.9f + stroke) * 6f);
                D.Line(vh, previous, point, 16f - stroke * 2f, D.Alpha(b.Color, 0.75f * fade));
                previous = point;
            }
        }
        for (int i = 0; i < 4; i++)
        {
            float drop = Mathf.Clamp01((t - 0.35f - i * 0.05f) / 0.5f);
            if (drop <= 0) continue;
            Vector2 at = b.From + new Vector2(-50f + 33f * i + (D.Hash(b.Seed, i) - 0.5f) * 14f, -30f - 50f * D.EaseIn(drop));
            D.Circle(vh, at, 5f * (1 - drop * 0.4f), D.Alpha(b.Color, fade));
        }
    }

    /// <summary>작은 반응 연출을 종류별로 그린다.</summary>
    private static void DrawFx(VertexHelper vh, Burst b, float t)
    {
        switch (b.FxKind)
        {
            case Fx.Place: DrawPlace(vh, b, t); break;
            case Fx.Complete: DrawComplete(vh, b, t); break;
            case Fx.Hurt: DrawHurt(vh, b, t); break;
        }
    }

    /// <summary>조각 놓기: 놓은 자리에서 가는 고리와 짧은 불똥이 퍼진다.</summary>
    private static void DrawPlace(VertexHelper vh, Burst b, float t)
    {
        float v = D.EaseOut(t);
        D.Glow(vh, b.From, 60f + 40f * v, D.Alpha(b.Color, 0.45f * (1 - t) * (1 - t)));
        D.Additive = 1;
        D.Ring(vh, b.From, 22f + 58f * v, 3.5f * (1 - t) + 0.5f, D.Alpha(b.Color, 0.9f * (1 - t)));
        for (int i = 0; i < 6; i++)
        {
            Vector2 dir = D.Direction(i * Mathf.PI / 3 + D.Hash(b.Seed, i) * 0.8f);
            D.Line(vh, b.From + dir * (26f + 40f * v), b.From + dir * (36f + 58f * v), 2.5f * (1 - t) + 0.5f, D.Alpha(Color.Lerp(b.Color, Color.white, 0.5f), 1 - t));
        }
        D.Additive = 0;
    }

    /// <summary>도안 완성: 두 겹 고리가 퍼지고 빛 조각이 위로 흩어져 시전할 수 있음을 알린다.</summary>
    private static void DrawComplete(VertexHelper vh, Burst b, float t)
    {
        float v = D.EaseOut(t), fade = 1 - t;
        D.Glow(vh, b.From, 120f + 90f * v, D.Alpha(b.Color, 0.6f * fade * fade));
        D.Additive = 1;
        D.Ring(vh, b.From, 30f + 110f * v, 5f * fade + 0.5f, D.Alpha(b.Color, fade));
        float second = Mathf.Clamp01((t - 0.15f) / 0.85f);
        if (second > 0) D.Ring(vh, b.From, 20f + 90f * D.EaseOut(second), 3f * (1 - second) + 0.5f, D.Alpha(Color.Lerp(b.Color, Color.white, 0.5f), 0.8f * (1 - second)));
        for (int i = 0; i < 10; i++)
        {
            float delay = D.Hash(b.Seed, i + 60) * 0.3f, u = Mathf.Clamp01((t - delay) / (1 - delay));
            if (u <= 0) continue;
            Vector2 at = b.From + new Vector2((D.Hash(b.Seed, i) - 0.5f) * 170f, -30f + 150f * D.EaseOut(u) * (0.5f + 0.5f * D.Hash(b.Seed, i + 30)));
            float size = 7f * (1 - u) + 1f;
            // 네 갈래 반짝임 모양이다.
            D.Line(vh, at + Vector2.left * size, at + Vector2.right * size, 2f, D.Alpha(Color.Lerp(b.Color, Color.white, 0.6f), 1 - u));
            D.Line(vh, at + Vector2.down * size, at + Vector2.up * size, 2f, D.Alpha(Color.Lerp(b.Color, Color.white, 0.6f), 1 - u));
        }
        D.Additive = 0;
    }

    /// <summary>피격: 대상 위로 세 줄기 할퀸 자국이 빠르게 그어졌다 사라진다.</summary>
    private static void DrawHurt(VertexHelper vh, Burst b, float t)
    {
        float grow = Mathf.Clamp01(t * 4f), fade = 1 - Mathf.Clamp01((t - 0.3f) / 0.7f);
        Vector2 dir = D.Direction(-55f * Mathf.Deg2Rad);
        D.Glow(vh, b.From, 140f, D.Alpha(b.Color, 0.5f * fade));
        for (int i = -1; i <= 1; i++)
        {
            Vector2 side = new Vector2(-dir.y, dir.x) * i * 34f;
            Vector2 a = b.From + side - dir * 86f, end = Vector2.Lerp(a, b.From + side + dir * 86f, grow);
            D.GlowLine(vh, a, end, 40f * fade + 4f, D.Alpha(b.Color, 0.6f * fade));
            D.Additive = 1;
            D.Line(vh, a, end, 5f * fade + 0.5f, D.Alpha(Color.Lerp(b.Color, Color.white, 0.6f), fade));
            D.Additive = 0;
        }
    }
}
