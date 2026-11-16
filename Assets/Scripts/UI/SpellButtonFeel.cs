using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>버튼에 손맛을 준다. 올리면 살짝 커지며 밝아지고, 누르면 작아진다. 켜질 때 튀어나오거나, 눌러 달라는 뜻으로 천천히 숨 쉬게 할 수 있다.
/// 색은 건드리지 않고 밝은 덧판을 따로 올리므로 버튼 색을 코드로 바꾸는 화면과 부딪히지 않는다.</summary>
public sealed class SpellButtonFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private const float HOVER_SCALE = 1.035f;
    private const float PRESS_SCALE = 0.965f;
    private const float FOLLOW_SPEED = 18f;
    private const float HOVER_SHEEN = 0.09f;
    private const float PRESS_SHEEN = 0.16f;
    private const float POP_SECONDS = 0.22f;
    private const float POP_FROM = 0.55f;
    private const float PULSE_AMOUNT = 0.022f;
    private const float PULSE_SECONDS = 1.6f;

    private Selectable _selectable;
    private Image _sheen;
    // 마지막으로 곱해 둔 배율이다. 다른 코드가 크기를 바꿔도 그 크기를 기준으로 삼도록 매번 나눠서 원래 크기를 구한다.
    private float _applied = 1f;
    private float _scale = 1f;
    private float _sheenAlpha;
    private float _popStart = -1f;
    private bool _isHovered;
    private bool _isPressed;

    /// <summary>true면 가만히 있을 때 천천히 커졌다 작아지며 눌러 달라고 알린다.</summary>
    public bool Pulse { get; set; }

    /// <summary>true면 켜질 때마다 작게 시작해 튀어나온다.</summary>
    public bool PopOnEnable { get; set; }

    /// <summary>대상에 버튼 반응을 붙인다. 이미 있으면 설정만 바꾼다.</summary>
    public static SpellButtonFeel Attach(GameObject target, bool pulse = false, bool popOnEnable = false)
    {
        SpellButtonFeel feel = target.GetComponent<SpellButtonFeel>();
        if (feel == null) feel = target.AddComponent<SpellButtonFeel>();
        feel.Pulse = pulse; feel.PopOnEnable = popOnEnable;
        return feel;
    }

    void Awake()
    {
        _selectable = GetComponent<Selectable>();
        EnsureCenterPivot();
        BuildSheen();
    }

    void OnEnable()
    {
        EnsureCenterPivot();
        _isHovered = _isPressed = false; _scale = 1f; _sheenAlpha = 0f;
        _popStart = PopOnEnable ? Time.unscaledTime : -1f;
        Apply();
    }

    /// <summary>버튼이 항상 중앙 기준으로 커지고 작아지도록 피벗을 중앙(0.5, 0.5)으로 맞추고 위치를 보정한다.</summary>
    private void EnsureCenterPivot()
    {
        RectTransform rect = transform as RectTransform;
        if (rect == null) return;
        Vector2 delta = new Vector2(0.5f, 0.5f) - rect.pivot;
        if (delta.sqrMagnitude < 0.0001f) return;
        Vector2 size = rect.rect.size;
        if (size.x <= 0f && size.y <= 0f) size = rect.sizeDelta;
        Vector3 scale = rect.localScale;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition += new Vector2(delta.x * size.x * scale.x, delta.y * size.y * scale.y);
    }

    void OnDisable()
    {
        RestoreScale();
        if (_sheen != null) _sheen.color = new Color(1, 1, 1, 0);
    }

    void Update()
    {
        bool usable = _selectable == null || _selectable.IsInteractable();
        float target = !usable ? 1f : _isPressed ? PRESS_SCALE : _isHovered ? HOVER_SCALE : 1f;
        float sheen = !usable ? 0f : _isPressed ? PRESS_SHEEN : _isHovered ? HOVER_SHEEN : 0f;
        float step = 1 - Mathf.Exp(-FOLLOW_SPEED * Time.unscaledDeltaTime);
        _scale = Mathf.Lerp(_scale, target, step);
        _sheenAlpha = Mathf.Lerp(_sheenAlpha, sheen, step);
        Apply();
    }

    /// <summary>눌림·올림·튀어나옴·숨쉬기를 합친 크기와 덧판 밝기를 적용한다.</summary>
    private void Apply()
    {
        float scale = _scale;
        if (_popStart >= 0)
        {
            float t = (Time.unscaledTime - _popStart) / POP_SECONDS;
            if (t >= 1) _popStart = -1f;
            else scale *= Mathf.LerpUnclamped(POP_FROM, 1f, SpellEase.OutBack(t));
        }
        if (Pulse && !_isHovered && !_isPressed && (_selectable == null || _selectable.IsInteractable()))
            scale *= 1f + PULSE_AMOUNT * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2 / PULSE_SECONDS));
        Vector3 baseScale = transform.localScale / Mathf.Max(0.0001f, _applied);
        transform.localScale = baseScale * scale;
        _applied = scale;
        if (_sheen != null) _sheen.color = new Color(1, 1, 1, _sheenAlpha);
    }

    /// <summary>곱해 둔 배율을 걷어 원래 크기로 되돌린다.</summary>
    private void RestoreScale()
    {
        transform.localScale /= Mathf.Max(0.0001f, _applied);
        _applied = 1f;
    }

    /// <summary>버튼 바탕과 같은 모양의 흰 덧판을 바탕 바로 위에 만든다. 평소에는 투명하다.</summary>
    private void BuildSheen()
    {
        Image background = GetComponent<Image>();
        if (background == null || background.color.a < 0.05f) return;
        RectTransform rect = new GameObject("Sheen", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.SetAsFirstSibling();
        _sheen = rect.GetComponent<Image>();
        _sheen.sprite = background.sprite; _sheen.type = background.type; _sheen.raycastTarget = false;
        _sheen.color = new Color(1, 1, 1, 0);
    }

    public void OnPointerEnter(PointerEventData eventData) { _isHovered = true; }

    public void OnPointerExit(PointerEventData eventData) { _isHovered = false; _isPressed = false; }

    public void OnPointerDown(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) _isPressed = true; }

    public void OnPointerUp(PointerEventData eventData) { _isPressed = false; }
}
