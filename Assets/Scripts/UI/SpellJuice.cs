using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

/// <summary>화면 연출을 한곳에서 맡는다. 화면 흔들림, 번쩍임, 가장자리 붉은 번쩍임, 화면 전환 페이드, 떠오르는 숫자, 가운데 띠 알림, 지연 실행을 제공한다.
/// 연출끼리 부딪히지 않도록 모든 화면이 이 관리자를 통해서만 요청한다. 실제 시간으로 움직이며 입력은 막지 않는다.</summary>
public sealed class SpellJuice : MonoBehaviour
{
    private const float FLOAT_SECONDS = 0.95f;
    private const float FLOAT_RISE = 86f;
    private const float FLOAT_POP_SECONDS = 0.16f;
    private const float BANNER_IN = 0.18f;
    private const float BANNER_OUT = 0.28f;
    private const float SHAKE_FREQUENCY = 38f;

    /// <summary>떠오르는 글자 하나다.</summary>
    private sealed class FloatText
    {
        public RectTransform Rect;
        public Text Label;
        public Outline Outline;
        public float Start;
        public float Duration;
        public Vector2 From;
        public Vector2 Drift;
        public Color Color;
    }

    /// <summary>나중에 실행할 일이다.</summary>
    private struct Scheduled
    {
        public float Time;
        public Action Action;
    }

    public static SpellJuice Instance { get; private set; }

    private RectTransform _content;
    private RectTransform _layer;
    private Action<Vector2> _onShake;
    private Image _flash;
    private Image _fade;
    private SpellVignetteGraphic _edge;
    private RectTransform _banner;
    private CanvasGroup _bannerGroup;
    private Text _bannerText;
    private Image _bannerLineTop, _bannerLineBottom;
    private ObjectPool<FloatText> _pool;
    private readonly List<FloatText> _floats = new List<FloatText>();
    private readonly List<Scheduled> _schedule = new List<Scheduled>();
    private readonly List<Action> _due = new List<Action>();

    private Vector2 _contentBase;
    private float _shakeAmplitude, _shakeStart, _shakeDuration;
    private Color _flashColor; private float _flashAlpha, _flashStart, _flashDuration;
    private Color _edgeColor; private float _edgeAlpha, _edgeStart, _edgeDuration;
    private float _fadeStart, _fadeDuration;
    private float _bannerStart = -1f, _bannerHold;

    /// <summary>canvasRoot 맨 위에 연출 층을 만든다. content는 흔들 화면 묶음이고, onShake는 같은 흔들림(캔버스 단위)을 3D 카메라에 전하는 데 쓴다.</summary>
    public static SpellJuice Create(RectTransform canvasRoot, RectTransform content, Action<Vector2> onShake)
    {
        RectTransform layer = new GameObject("Juice Layer", typeof(RectTransform)).GetComponent<RectTransform>();
        layer.SetParent(canvasRoot, false);
        Stretch(layer);
        layer.SetAsLastSibling();
        SpellJuice juice = layer.gameObject.AddComponent<SpellJuice>();
        juice._content = content; juice._layer = layer; juice._onShake = onShake; juice._contentBase = content.anchoredPosition;
        juice.Build();
        Instance = juice;
        return juice;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ───────────── 요청 ─────────────

    /// <summary>화면을 pixels(캔버스 단위)만큼 seconds초 동안 흔든다. 진행 중인 흔들림보다 약하면 무시한다.</summary>
    public void Shake(float pixels, float seconds)
    {
        float remaining = CurrentShake();
        if (pixels < remaining) return;
        _shakeAmplitude = pixels; _shakeStart = Time.unscaledTime; _shakeDuration = Mathf.Max(0.05f, seconds);
    }

    /// <summary>화면 전체를 color로 alpha만큼 번쩍였다가 seconds초에 걸쳐 걷는다.</summary>
    public void Flash(Color color, float alpha, float seconds)
    {
        _flashColor = color; _flashAlpha = alpha; _flashStart = Time.unscaledTime; _flashDuration = Mathf.Max(0.05f, seconds);
    }

    /// <summary>화면 가장자리만 color로 번쩍였다가 걷는다. 맞았을 때 쓴다.</summary>
    public void EdgeFlash(Color color, float alpha, float seconds)
    {
        _edgeColor = color; _edgeAlpha = alpha; _edgeStart = Time.unscaledTime; _edgeDuration = Mathf.Max(0.05f, seconds);
    }

    /// <summary>화면을 바탕색으로 덮었다가 seconds초에 걸쳐 걷어 새 화면이 부드럽게 나타나게 한다.</summary>
    public void FadeIn(float seconds)
    {
        _fadeStart = Time.unscaledTime; _fadeDuration = Mathf.Max(0.05f, seconds);
        _fade.gameObject.SetActive(true);
        _fade.color = SpellStyle.Ink;
    }

    /// <summary>anchor 중심에서 offset(캔버스 단위)만큼 떨어진 곳에 글자를 띄워 위로 올리며 지운다.</summary>
    public void Float(string text, RectTransform anchor, Vector2 offset, Color color, int size = 40)
    {
        if (anchor == null) return;
        Vector2 local = _layer.InverseTransformPoint(anchor.TransformPoint(anchor.rect.center));
        Spawn(text, local + offset, color, size);
    }

    /// <summary>화면 좌표에 글자를 띄워 위로 올리며 지운다.</summary>
    public void FloatAtScreen(string text, Vector2 screenPoint, Color color, int size = 40)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screenPoint, null, out Vector2 local);
        Spawn(text, local, color, size);
    }

    /// <summary>화면 가운데에 가로 띠와 글자를 잠깐 보여 준다. 턴 시작처럼 흐름이 바뀌는 순간에 쓴다.</summary>
    public void Banner(string text, Color color, float holdSeconds = 0.55f)
    {
        _bannerText.text = text; _bannerText.color = color;
        _bannerLineTop.color = _bannerLineBottom.color = SpellStyle.Alpha(color, 0.75f);
        _bannerStart = Time.unscaledTime; _bannerHold = holdSeconds;
        _banner.gameObject.SetActive(true);
    }

    /// <summary>delay초 뒤에 action을 실행한다. Clear가 불리면 취소된다.</summary>
    public void After(float delay, Action action)
    {
        if (action == null) return;
        if (delay <= 0) { action(); return; }
        _schedule.Add(new Scheduled { Time = Time.unscaledTime + delay, Action = action });
    }

    /// <summary>예약한 일과 떠 있는 글자, 띠 알림, 흔들림을 모두 지운다. 화면이 바뀔 때 부른다.</summary>
    public void Clear()
    {
        _schedule.Clear();
        for (int i = _floats.Count - 1; i >= 0; i--) _pool.Release(_floats[i]);
        _floats.Clear();
        _bannerStart = -1f; _banner.gameObject.SetActive(false);
        _shakeAmplitude = 0; ApplyShake(Vector2.zero);
        _edgeAlpha = 0; _edge.color = new Color(0, 0, 0, 0);
    }

    // ───────────── 갱신 ─────────────

    void Update()
    {
        float now = Time.unscaledTime;
        RunSchedule(now);
        UpdateShake(now);
        UpdateOverlays(now);
        UpdateFloats(now);
        UpdateBanner(now);
    }

    private void RunSchedule(float now)
    {
        if (_schedule.Count == 0) return;
        _due.Clear();
        for (int i = _schedule.Count - 1; i >= 0; i--)
        {
            if (_schedule[i].Time > now) continue;
            _due.Add(_schedule[i].Action); _schedule.RemoveAt(i);
        }
        // 예약한 순서대로 실행한다(뒤에서부터 모았으므로 거꾸로 돈다).
        for (int i = _due.Count - 1; i >= 0; i--) _due[i]();
        _due.Clear();
    }

    /// <summary>지금 남아 있는 흔들림 세기를 반환한다.</summary>
    private float CurrentShake()
    {
        if (_shakeAmplitude <= 0) return 0;
        float t = (Time.unscaledTime - _shakeStart) / _shakeDuration;
        if (t >= 1) return 0;
        return _shakeAmplitude * (1 - t) * (1 - t);
    }

    private void UpdateShake(float now)
    {
        float strength = CurrentShake();
        if (strength <= 0)
        {
            if (_shakeAmplitude > 0) { _shakeAmplitude = 0; ApplyShake(Vector2.zero); }
            return;
        }
        float phase = now * SHAKE_FREQUENCY;
        // 서로 다른 주기의 사인을 섞어 규칙적으로 보이지 않게 한다.
        Vector2 offset = new Vector2(Mathf.Sin(phase * 1.31f) + 0.5f * Mathf.Sin(phase * 2.73f + 1.7f), Mathf.Sin(phase * 1.87f + 0.9f) + 0.5f * Mathf.Sin(phase * 3.11f)) * (strength / 1.5f);
        ApplyShake(offset);
    }

    private void ApplyShake(Vector2 offset)
    {
        if (_content != null) _content.anchoredPosition = _contentBase + offset;
        _onShake?.Invoke(offset);
    }

    private void UpdateOverlays(float now)
    {
        float flash = _flashAlpha > 0 ? _flashAlpha * (1 - SpellEase.OutCubic((now - _flashStart) / _flashDuration)) : 0;
        if (flash <= 0.003f) { _flashAlpha = 0; if (_flash.gameObject.activeSelf) _flash.gameObject.SetActive(false); }
        else { if (!_flash.gameObject.activeSelf) _flash.gameObject.SetActive(true); _flash.color = SpellStyle.Alpha(_flashColor, flash); }

        float edge = _edgeAlpha > 0 ? _edgeAlpha * (1 - SpellEase.OutCubic((now - _edgeStart) / _edgeDuration)) : 0;
        if (edge <= 0.003f) { if (_edgeAlpha > 0) { _edgeAlpha = 0; _edge.color = new Color(0, 0, 0, 0); } }
        else _edge.color = SpellStyle.Alpha(_edgeColor, edge);

        if (_fade.gameObject.activeSelf)
        {
            float t = (now - _fadeStart) / _fadeDuration;
            if (t >= 1) _fade.gameObject.SetActive(false);
            else _fade.color = SpellStyle.Alpha(SpellStyle.Ink, 1 - SpellEase.InOutCubic(t));
        }
    }

    private void UpdateFloats(float now)
    {
        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            FloatText f = _floats[i];
            float t = (now - f.Start) / f.Duration;
            if (t >= 1) { _floats.RemoveAt(i); _pool.Release(f); continue; }
            float rise = SpellEase.OutCubic(t);
            f.Rect.anchoredPosition = f.From + f.Drift * rise;
            // 처음에 크게 튀어나왔다가 제 크기로 돌아온다.
            float pop = now - f.Start < FLOAT_POP_SECONDS ? Mathf.LerpUnclamped(0.5f, 1f, SpellEase.OutBack((now - f.Start) / FLOAT_POP_SECONDS)) : 1f;
            f.Rect.localScale = Vector3.one * pop;
            float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            f.Label.color = SpellStyle.Alpha(f.Color, alpha);
            f.Outline.effectColor = new Color(0.02f, 0.03f, 0.06f, 0.85f * alpha);
        }
    }

    private void UpdateBanner(float now)
    {
        if (_bannerStart < 0) return;
        float elapsed = now - _bannerStart, total = BANNER_IN + _bannerHold + BANNER_OUT;
        if (elapsed >= total) { _bannerStart = -1f; _banner.gameObject.SetActive(false); return; }
        float appear = SpellEase.OutCubic(elapsed / BANNER_IN);
        float leave = elapsed > BANNER_IN + _bannerHold ? (elapsed - BANNER_IN - _bannerHold) / BANNER_OUT : 0;
        _bannerGroup.alpha = appear * (1 - leave);
        _banner.localScale = new Vector3(1, Mathf.Lerp(0.6f, 1f, appear), 1);
        // 글자는 오른쪽에서 밀려 들어왔다가 왼쪽으로 조금 흘러 나간다.
        _bannerText.rectTransform.anchoredPosition = new Vector2(60f * (1 - appear) - 40f * SpellEase.InCubic(leave), 0);
    }

    private void Spawn(string text, Vector2 local, Color color, int size)
    {
        FloatText f = _pool.Get();
        f.Label.text = text; f.Label.fontSize = size; f.Label.color = color;
        f.Rect.anchoredPosition = local; f.Rect.localScale = Vector3.one * 0.5f;
        f.Rect.SetAsLastSibling();
        f.Start = Time.unscaledTime; f.Duration = FLOAT_SECONDS; f.From = local; f.Color = color;
        // 여러 숫자가 겹치지 않도록 옆으로 조금씩 흩어 올린다.
        f.Drift = new Vector2(UnityEngine.Random.Range(-26f, 26f), FLOAT_RISE);
        _floats.Add(f);
    }

    // ───────────── 만들기 ─────────────

    private void Build()
    {
        _edge = NewChild<SpellVignetteGraphic>("Edge Flash");
        _edge.SetShape(0.5f, 1.5f); _edge.color = new Color(0, 0, 0, 0);

        _banner = new GameObject("Banner", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        _banner.SetParent(_layer, false);
        _banner.anchorMin = new Vector2(0, 0.5f); _banner.anchorMax = new Vector2(1, 0.5f); _banner.pivot = new Vector2(0.5f, 0.5f);
        _banner.anchoredPosition = new Vector2(0, 40); _banner.sizeDelta = new Vector2(0, 128);
        _bannerGroup = _banner.GetComponent<CanvasGroup>(); _bannerGroup.blocksRaycasts = false; _bannerGroup.interactable = false;
        Image band = NewImage(_banner, "Band", SpellStyle.FadeH, SpellStyle.Alpha(SpellStyle.InkDeep, 0.86f));
        Stretch(band.rectTransform);
        _bannerLineTop = NewImage(_banner, "Line Top", SpellStyle.FadeH, SpellStyle.Gold);
        _bannerLineTop.rectTransform.anchorMin = new Vector2(0.15f, 1); _bannerLineTop.rectTransform.anchorMax = new Vector2(0.85f, 1);
        _bannerLineTop.rectTransform.sizeDelta = new Vector2(0, 2); _bannerLineTop.rectTransform.anchoredPosition = Vector2.zero;
        _bannerLineBottom = NewImage(_banner, "Line Bottom", SpellStyle.FadeH, SpellStyle.Gold);
        _bannerLineBottom.rectTransform.anchorMin = new Vector2(0.15f, 0); _bannerLineBottom.rectTransform.anchorMax = new Vector2(0.85f, 0);
        _bannerLineBottom.rectTransform.sizeDelta = new Vector2(0, 2); _bannerLineBottom.rectTransform.anchoredPosition = Vector2.zero;
        _bannerText = NewText(_banner, "Text", SpellStyle.Serif, 58, SpellStyle.Cream);
        Stretch(_bannerText.rectTransform);
        _banner.gameObject.SetActive(false);

        _flash = NewImage(_layer, "Flash", null, new Color(1, 1, 1, 0));
        Stretch(_flash.rectTransform); _flash.gameObject.SetActive(false);
        _fade = NewImage(_layer, "Fade", null, SpellStyle.Ink);
        Stretch(_fade.rectTransform); _fade.gameObject.SetActive(false);

        _pool = new ObjectPool<FloatText>(CreateFloat, f => f.Rect.gameObject.SetActive(true), f => f.Rect.gameObject.SetActive(false), f => Destroy(f.Rect.gameObject), false, 12, 48);
    }

    private FloatText CreateFloat()
    {
        Text label = NewText(_layer, "Float Text", SpellStyle.SansBold, 40, Color.white);
        label.rectTransform.sizeDelta = new Vector2(420, 70);
        label.horizontalOverflow = HorizontalWrapMode.Overflow; label.verticalOverflow = VerticalWrapMode.Overflow;
        Outline outline = label.gameObject.AddComponent<Outline>();
        outline.effectDistance = new Vector2(2.5f, -2.5f);
        return new FloatText { Rect = label.rectTransform, Label = label, Outline = outline };
    }

    private T NewChild<T>(string name) where T : Graphic
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        rect.SetParent(_layer, false);
        Stretch(rect);
        T graphic = rect.gameObject.AddComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static Image NewImage(RectTransform parent, string name, Sprite sprite, Color color)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = rect.GetComponent<Image>();
        image.sprite = sprite; image.color = color; image.raycastTarget = false;
        return image;
    }

    private static Text NewText(RectTransform parent, string name, Font font, int size, Color color)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Text text = rect.GetComponent<Text>();
        text.font = font; text.fontSize = size; text.color = color; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
