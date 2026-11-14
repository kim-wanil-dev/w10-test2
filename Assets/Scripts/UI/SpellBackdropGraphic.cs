using UnityEngine;
using UnityEngine.UI;

/// <summary>화면 바탕의 분위기 연출을 그린다. 전투가 아닌 화면에서는 큰 조각 무늬가 천천히 떠다니고 금빛 알갱이가 피어오르며,
/// 전투 화면에서는 전장 위쪽에만 알갱이를 띄운다. 승리 때는 색종이를 한 번 뿌릴 수 있다. 가장자리 그늘을 자식으로 함께 만든다. 입력은 받지 않는다.</summary>
public sealed class SpellBackdropGraphic : MaskableGraphic
{
    /// <summary>Screen은 전투 밖 화면 전체, Field는 전투 화면의 전장 위쪽이다.</summary>
    public enum Mode { Screen, Field }

    private const int SHAPE_COUNT = 9;
    private const int SCREEN_MOTES = 34;
    private const int FIELD_MOTES = 20;
    private const int CONFETTI_COUNT = 60;
    private const float CONFETTI_SECONDS = 2.4f;
    // 전투 화면에서 전장이 차지하는 위쪽 높이(캔버스 단위)다.
    private const float FIELD_HEIGHT = 432f;

    private Mode _mode;
    private Color _moteColor;
    private float _confettiStart = -1f;
    private Color[] _confettiColors;
    private int _confettiSeed;

    /// <summary>parent를 가득 채우는 바탕 연출과 가장자리 그늘을 만든다.</summary>
    public static SpellBackdropGraphic Create(RectTransform parent, Mode mode)
    {
        RectTransform rect = new GameObject(mode == Mode.Screen ? "Backdrop" : "Field Ambience", typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Stretch(rect);
        SpellBackdropGraphic graphic = rect.gameObject.AddComponent<SpellBackdropGraphic>();
        graphic._mode = mode; graphic._moteColor = SpellStyle.Gold; graphic.raycastTarget = false;
        if (SpellStyle.GlowMaterial != null) graphic.material = SpellStyle.GlowMaterial;
        RectTransform shade = new GameObject("Vignette", typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        shade.SetParent(rect, false);
        Stretch(shade);
        SpellVignetteGraphic vignette = shade.gameObject.AddComponent<SpellVignetteGraphic>();
        vignette.color = SpellStyle.Alpha(SpellStyle.InkDeep, mode == Mode.Screen ? 0.9f : 0.62f);
        vignette.SetShape(mode == Mode.Screen ? 0.3f : 0.42f, mode == Mode.Screen ? 1.5f : 1.9f);
        return graphic;
    }

    /// <summary>알갱이 색을 바꾼다. 화면 분위기(승리는 밝은 금색, 패배는 붉은빛)에 맞춘다.</summary>
    public void SetMoteColor(Color color) { _moteColor = color; }

    /// <summary>화면 위쪽에서 색종이를 한 번 뿌린다.</summary>
    public void Celebrate(params Color[] colors)
    {
        if (colors == null || colors.Length == 0) return;
        _confettiColors = colors; _confettiStart = Time.unscaledTime; _confettiSeed = Random.Range(1, 9999);
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    void Update() { SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        float time = Time.unscaledTime;
        SpellDraw.Additive = 0;
        if (_mode == Mode.Screen)
        {
            DrawShapes(vh, rect, time);
            DrawMotes(vh, rect, time, SCREEN_MOTES, 1f);
            DrawConfetti(vh, rect, time);
        }
        else
        {
            Rect field = Rect.MinMaxRect(rect.xMin, rect.yMax - FIELD_HEIGHT, rect.xMax, rect.yMax);
            DrawMotes(vh, field, time, FIELD_MOTES, 0.75f);
        }
        SpellDraw.Additive = 0;
    }

    /// <summary>큰 삼각형·사각형·평행사변형 무늬를 아주 옅게 그려 천천히 돌리며 흘려보낸다. 화면 밖으로 나가면 반대편에서 다시 들어온다.</summary>
    private static void DrawShapes(VertexHelper vh, Rect rect, float time)
    {
        float margin = 260f, width = rect.width + margin * 2, height = rect.height + margin * 2;
        for (int i = 0; i < SHAPE_COUNT; i++)
        {
            float size = Mathf.Lerp(150f, 430f, SpellDraw.Hash(i, 1));
            // 큰 무늬일수록 느리게 움직여 멀리 있는 것처럼 보인다.
            float speed = Mathf.Lerp(16f, 6f, SpellDraw.Hash(i, 1));
            Vector2 velocity = SpellDraw.Direction(SpellDraw.Hash(i, 2) * Mathf.PI * 2) * speed;
            Vector2 origin = new Vector2(SpellDraw.Hash(i, 3) * width, SpellDraw.Hash(i, 4) * height) + velocity * time;
            Vector2 center = new Vector2(rect.xMin - margin + Mathf.Repeat(origin.x, width), rect.yMin - margin + Mathf.Repeat(origin.y, height));
            float angle = SpellDraw.Hash(i, 5) * Mathf.PI * 2 + time * Mathf.Lerp(-0.05f, 0.05f, SpellDraw.Hash(i, 6));
            Color fill = SpellStyle.Alpha(i % 3 == 0 ? SpellStyle.Button : SpellStyle.Line, Mathf.Lerp(0.05f, 0.11f, SpellDraw.Hash(i, 7)));
            Color edge = SpellStyle.Alpha(SpellStyle.Gold, 0.07f);
            Vector2 x = SpellDraw.Direction(angle) * size / 2, y = SpellDraw.Direction(angle + Mathf.PI / 2) * size / 2;
            Vector2[] points;
            switch (i % 3)
            {
                case 0: points = new[] { center - x - y, center + x - y, center - x + y }; break;
                case 1: points = new[] { center - x - y, center + x - y, center + x + y, center - x + y }; break;
                default: points = new[] { center - x * 1.5f - y * 0.5f, center + x * 0.5f - y * 0.5f, center + x * 1.5f + y * 0.5f, center - x * 0.5f + y * 0.5f }; break;
            }
            if (points.Length == 3) SpellDraw.Triangle(vh, points[0], points[1], points[2], fill);
            else { SpellDraw.Triangle(vh, points[0], points[1], points[2], fill); SpellDraw.Triangle(vh, points[0], points[2], points[3], fill); }
            for (int p = 0; p < points.Length; p++) SpellDraw.Line(vh, points[p], points[(p + 1) % points.Length], 2f, edge);
        }
    }

    /// <summary>작은 빛 알갱이가 흔들리며 천천히 떠오르고 깜빡인다. 맨 위에 닿으면 아래에서 다시 시작한다.</summary>
    private void DrawMotes(VertexHelper vh, Rect area, float time, int count, float strength)
    {
        for (int i = 0; i < count; i++)
        {
            float speed = Mathf.Lerp(9f, 26f, SpellDraw.Hash(i, 11));
            float y = area.yMin + Mathf.Repeat(SpellDraw.Hash(i, 12) * area.height + speed * time, area.height);
            float sway = Mathf.Sin(time * Mathf.Lerp(0.25f, 0.7f, SpellDraw.Hash(i, 13)) + SpellDraw.Hash(i, 14) * 6.28f) * Mathf.Lerp(12f, 40f, SpellDraw.Hash(i, 15));
            float x = area.xMin + SpellDraw.Hash(i, 16) * area.width + sway;
            float twinkle = 0.45f + 0.55f * Mathf.Sin(time * Mathf.Lerp(0.6f, 1.7f, SpellDraw.Hash(i, 17)) + SpellDraw.Hash(i, 18) * 6.28f);
            // 영역 위아래 끝에서는 서서히 나타나고 사라져 툭 끊기지 않게 한다.
            float edge = Mathf.Clamp01((y - area.yMin) / 60f) * Mathf.Clamp01((area.yMax - y) / 60f);
            float alpha = Mathf.Lerp(0.16f, 0.5f, SpellDraw.Hash(i, 19)) * Mathf.Max(0.15f, twinkle) * edge * strength;
            float radius = Mathf.Lerp(7f, 17f, SpellDraw.Hash(i, 20));
            SoftDot(vh, new Vector2(x, y), radius, SpellStyle.Alpha(_moteColor, alpha));
            SpellDraw.Additive = 1;
            SpellDraw.Polygon(vh, new Vector2(x, y), radius * 0.16f, 6, 0, SpellStyle.Alpha(SpellStyle.Parchment, alpha * 1.6f));
            SpellDraw.Additive = 0;
        }
    }

    /// <summary>색종이 조각이 위로 솟았다가 중력에 끌려 떨어지며 빙글 돈다.</summary>
    private void DrawConfetti(VertexHelper vh, Rect rect, float time)
    {
        if (_confettiStart < 0) return;
        float elapsed = time - _confettiStart;
        if (elapsed > CONFETTI_SECONDS) { _confettiStart = -1f; return; }
        Vector2 origin = new Vector2(rect.center.x, rect.yMin + rect.height * 0.62f);
        for (int i = 0; i < CONFETTI_COUNT; i++)
        {
            float angle = Mathf.Lerp(0.18f, 0.82f, SpellDraw.Hash(_confettiSeed, i)) * Mathf.PI;
            float speed = Mathf.Lerp(520f, 1180f, SpellDraw.Hash(_confettiSeed, i + 100));
            Vector2 velocity = SpellDraw.Direction(angle) * speed;
            // 공기 저항으로 옆으로 퍼지는 속도는 줄고, 아래로는 중력이 붙는다.
            float drag = 1 - Mathf.Exp(-1.6f * elapsed);
            Vector2 position = origin + new Vector2(velocity.x * drag / 1.6f, velocity.y * drag / 1.6f - 520f * elapsed * elapsed);
            float fade = 1 - Mathf.Clamp01((elapsed - CONFETTI_SECONDS * 0.6f) / (CONFETTI_SECONDS * 0.4f));
            Color color = SpellStyle.Alpha(_confettiColors[i % _confettiColors.Length], fade);
            float spin = elapsed * Mathf.Lerp(4f, 11f, SpellDraw.Hash(_confettiSeed, i + 200)) + i;
            // 뒤집히며 납작해 보이도록 한쪽 폭을 사인으로 줄인다.
            Vector2 size = new Vector2(Mathf.Lerp(10f, 18f, SpellDraw.Hash(_confettiSeed, i + 300)), 8f * Mathf.Abs(Mathf.Sin(spin * 1.7f)) + 2f);
            SpellDraw.Quad(vh, position, size, spin, color);
        }
    }

    /// <summary>가운데가 밝고 가장자리가 사라지는 작은 빛 점이다. 알갱이가 많아 정점 수가 적은 모양을 쓴다.</summary>
    private static void SoftDot(VertexHelper vh, Vector2 at, float radius, Color color)
    {
        if (color.a <= 0.003f) return;
        const int SIDES = 8;
        SpellDraw.Additive = 1;
        int start = vh.currentVertCount;
        SpellDraw.AddVert(vh, at, color);
        Color rim = SpellStyle.Alpha(color, 0);
        for (int i = 0; i < SIDES; i++) SpellDraw.AddVert(vh, at + SpellDraw.Direction(i * Mathf.PI * 2 / SIDES) * radius, rim);
        for (int i = 0; i < SIDES; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % SIDES);
        SpellDraw.Additive = 0;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
