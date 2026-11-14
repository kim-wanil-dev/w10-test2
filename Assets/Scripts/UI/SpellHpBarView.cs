using UnityEngine;
using UnityEngine.UI;

/// <summary>체력 막대의 현재 비율, 예상 피해 구간, 수치 문구를 표시한다. 값이 바뀌면 툭 끊기지 않고 부드럽게 따라가며,
/// 깎인 만큼은 밝은 잔상이 잠깐 남았다가 줄어들어 얼마나 깎였는지 보여 준다.</summary>
public sealed class SpellHpBarView : MonoBehaviour
{
    private const float PULSE_SPEED = 5f;
    private const float PULSE_MIN_ALPHA = 0.25f;
    // 채움이 새 값까지 가는 시간, 깎인 자리의 잔상이 버티는 시간과 줄어드는 시간이다.
    private const float FILL_SECONDS = 0.3f;
    private const float CHIP_DELAY = 0.3f;
    private const float CHIP_SECONDS = 0.45f;
    private static readonly Color CHIP_COLOR = new Color(1f, 0.95f, 0.82f, 0.92f);

    [Header("구성")]
    [SerializeField] private RectTransform _fill;
    [SerializeField] private RectTransform _preview;
    [SerializeField] private Text _value;

    [Header("예상 피해 깜빡임")]
    [SerializeField] private bool _isPreviewPulsing;
    [SerializeField] private Color _pulseColor = new Color(0.937f, 0.32f, 0.3f);

    private RectTransform _defenseFill;
    private RectTransform _chip;
    private Image _previewImage;

    // 지금 그려진 비율, 트윈의 시작·목표와 시작 시각이다. 시작 시각이 음수면 멈춰 있다.
    private bool _hasValue;
    private float _shown, _from, _target, _tweenStart = -1f;
    private float _chipRatio, _chipFrom, _chipStart = -1f;
    // 예상 피해를 뺀 뒤의 비율이다. 음수면 예상 피해가 없다.
    private float _previewRatio = -1f;

    // 잠시 미뤄 둔 갱신이다. 공격이 닿는 순간에 맞춰 깎이도록 할 때 쓴다.
    private float _holdUntil = -1f;
    private bool _hasPending;
    private int _pendingCurrent, _pendingMax, _pendingPreview;
    private string _pendingText;

    void Awake()
    {
        _previewImage = _preview.GetComponent<Image>();
    }

    void OnDisable()
    {
        // 다시 켜질 때는 새 값으로 바로 시작한다.
        _hasValue = false; _tweenStart = _chipStart = _holdUntil = -1f; _hasPending = false;
        if (_chip != null) _chip.gameObject.SetActive(false);
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (_hasPending && now >= _holdUntil)
        {
            _hasPending = false; _holdUntil = -1f;
            Bind(_pendingCurrent, _pendingMax, _pendingPreview, _pendingText);
        }
        bool moved = false;
        if (_tweenStart >= 0)
        {
            float t = (now - _tweenStart) / FILL_SECONDS;
            _shown = Mathf.Lerp(_from, _target, SpellEase.OutCubic(t));
            if (t >= 1) { _shown = _target; _tweenStart = -1f; }
            moved = true;
        }
        if (_chipStart >= 0 && now >= _chipStart)
        {
            float t = (now - _chipStart) / CHIP_SECONDS;
            _chipRatio = Mathf.Lerp(_chipFrom, _target, SpellEase.OutCubic(t));
            if (t >= 1) { _chipRatio = _target; _chipStart = -1f; }
            moved = true;
        }
        if (moved) Layout();
        if (!_isPreviewPulsing || !_preview.gameObject.activeSelf) return;
        Color color = _pulseColor;
        color.a = Mathf.Lerp(PULSE_MIN_ALPHA, 1f, (Mathf.Sin(now * PULSE_SPEED) + 1f) * 0.5f);
        _previewImage.color = color;
    }

    /// <summary>체력 막대 위에 방어량을 최대 체력 대비 회색 띠로 표시한다.</summary>
    public void BindDefense(int defense, int maxHealth)
    {
        if (_defenseFill == null)
        {
            GameObject bar = new GameObject("DefenseFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _defenseFill = bar.GetComponent<RectTransform>();
            _defenseFill.SetParent(_fill.parent, false);
            _defenseFill.SetAsLastSibling();
            _value.transform.SetAsLastSibling();
            Image image = bar.GetComponent<Image>();
            image.color = new Color(0.65f, 0.67f, 0.7f, 1f);
            image.raycastTarget = false;
        }
        _defenseFill.gameObject.SetActive(defense > 0);
        _defenseFill.anchorMin = new Vector2(0, 1);
        _defenseFill.anchorMax = new Vector2(maxHealth > 0 ? Mathf.Clamp01((float)defense / maxHealth) : 0, 1);
        _defenseFill.pivot = new Vector2(0, 1);
        _defenseFill.offsetMin = new Vector2(0, -6);
        _defenseFill.offsetMax = Vector2.zero;
    }

    /// <summary>지금부터 seconds초 동안 들어오는 갱신을 미뤘다가 한꺼번에 반영한다. 공격 연출이 닿는 순간에 막대가 깎이게 할 때 먼저 부른다.</summary>
    public void Hold(float seconds)
    {
        if (seconds > 0) _holdUntil = Time.unscaledTime + seconds;
    }

    /// <summary>현재 체력과 최대 체력, 예상 피해로 막대와 수치 문구를 갱신한다. 체력이 바뀌었으면 부드럽게 따라간다.</summary>
    public void Bind(int current, int max, int previewDamage, string text)
    {
        if (_holdUntil >= 0 && Time.unscaledTime < _holdUntil && _hasValue)
        {
            _hasPending = true; _pendingCurrent = current; _pendingMax = max; _pendingPreview = previewDamage; _pendingText = text;
            return;
        }
        float now = max > 0 ? Mathf.Clamp01((float)current / max) : 0;
        float after = max > 0 ? Mathf.Clamp01((float)Mathf.Max(0, current - previewDamage) / max) : 0;
        _previewRatio = previewDamage > 0 ? after : -1f;
        _value.text = text;
        if (!_hasValue || !isActiveAndEnabled)
        {
            _hasValue = true; _shown = _from = _target = _chipRatio = now; _tweenStart = _chipStart = -1f;
        }
        else if (!Mathf.Approximately(now, _target))
        {
            if (now < _shown)
            {
                // 깎이기 전 자리에서 잔상이 잠깐 버틴 뒤 따라 줄어든다. 연달아 맞으면 가장 높던 자리에서 이어 간다.
                _chipRatio = Mathf.Max(_chipRatio, _shown);
                _chipFrom = _chipRatio; _chipStart = Time.unscaledTime + CHIP_DELAY;
            }
            else { _chipRatio = now; _chipStart = -1f; }
            _from = _shown; _target = now; _tweenStart = Time.unscaledTime;
        }
        Layout();
    }

    /// <summary>지금 비율로 채움·예상 피해 구간·잔상의 범위를 맞춘다.</summary>
    private void Layout()
    {
        float fill = _previewRatio >= 0 ? Mathf.Min(_previewRatio, _shown) : _shown;
        _fill.anchorMax = new Vector2(fill, 1);
        bool hasPreview = _previewRatio >= 0 && _shown > _previewRatio;
        _preview.gameObject.SetActive(hasPreview);
        if (hasPreview) { _preview.anchorMin = new Vector2(_previewRatio, 0); _preview.anchorMax = new Vector2(_shown, 1); }
        bool hasChip = _chipRatio > _shown + 0.002f;
        if (hasChip && _chip == null) BuildChip();
        if (_chip == null) return;
        _chip.gameObject.SetActive(hasChip);
        if (hasChip) { _chip.anchorMin = new Vector2(_shown, 0); _chip.anchorMax = new Vector2(_chipRatio, 1); }
    }

    /// <summary>깎인 자리를 보여 주는 밝은 잔상 띠를 채움 바로 아래 층에 만든다.</summary>
    private void BuildChip()
    {
        GameObject bar = new GameObject("Chip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _chip = bar.GetComponent<RectTransform>();
        _chip.SetParent(_fill.parent, false);
        _chip.SetSiblingIndex(_fill.GetSiblingIndex());
        _chip.offsetMin = _chip.offsetMax = Vector2.zero;
        Image image = bar.GetComponent<Image>(), source = _fill.GetComponent<Image>();
        if (source != null) { image.sprite = source.sprite; image.type = source.type; }
        image.color = CHIP_COLOR; image.raycastTarget = false;
    }
}
