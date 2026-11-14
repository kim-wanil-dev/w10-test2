using UnityEngine;

/// <summary>켜질 때 작고 투명하게 시작해 제자리로 나타난다. 창·말풍선·제목처럼 버튼이 아닌 요소에 쓴다(버튼은 SpellButtonFeel의 튀어나옴을 쓴다).
/// 밀려 들어오기(Slide)는 위치를 코드로 계속 옮기지 않는 요소에만 쓴다.</summary>
public sealed class SpellPopIn : MonoBehaviour
{
    private CanvasGroup _group;
    private RectTransform _rect;
    private Vector3 _baseScale = Vector3.one;
    private Vector2 _basePosition;
    private bool _hasBase;
    private float _start = -1f;
    private float _delay;

    /// <summary>나타나는 데 걸리는 시간(초)이다.</summary>
    public float Duration { get; set; } = 0.2f;

    /// <summary>시작 크기 배율이다. 1이면 크기는 바꾸지 않는다.</summary>
    public float FromScale { get; set; } = 0.92f;

    /// <summary>true면 투명에서 시작한다.</summary>
    public bool Fade { get; set; } = true;

    /// <summary>이만큼 떨어진 곳에서 제자리로 밀려 들어온다.</summary>
    public Vector2 Slide { get; set; }

    /// <summary>켜질 때 이만큼 기다렸다가 나타난다.</summary>
    public float Delay { get; set; }

    /// <summary>true면 크기를 살짝 지나쳤다 돌아오는 곡선을 쓴다.</summary>
    public bool Overshoot { get; set; }

    /// <summary>대상에 나타남 연출을 붙인다. 이미 있으면 설정만 바꾼다.</summary>
    public static SpellPopIn Attach(GameObject target, float fromScale = 0.92f, float duration = 0.2f, bool fade = true, float delay = 0f)
    {
        SpellPopIn pop = target.GetComponent<SpellPopIn>();
        if (pop == null) pop = target.AddComponent<SpellPopIn>();
        pop.FromScale = fromScale; pop.Duration = duration; pop.Fade = fade; pop.Delay = delay;
        // 켜진 대상에 붙였으면 기본값으로 이미 재생을 시작했으므로 새 설정으로 다시 재생한다.
        if (pop.isActiveAndEnabled) pop.Play(delay);
        return pop;
    }

    void Awake()
    {
        _rect = transform as RectTransform;
    }

    void OnEnable() { Play(Delay); }

    void OnDisable() { Finish(); }

    /// <summary>나타남 연출을 delay초 뒤에 처음부터 다시 재생한다.</summary>
    public void Play(float delay = 0f)
    {
        if (!_hasBase)
        {
            _baseScale = transform.localScale;
            if (_rect != null) _basePosition = _rect.anchoredPosition;
            _hasBase = true;
        }
        else if (_start < 0 && _rect != null) _basePosition = _rect.anchoredPosition;
        _start = Time.unscaledTime; _delay = Mathf.Max(0, delay);
        Apply(0f);
    }

    void Update()
    {
        if (_start < 0) return;
        float t = (Time.unscaledTime - _start - _delay) / Mathf.Max(0.01f, Duration);
        if (t >= 1) { Finish(); return; }
        Apply(Mathf.Max(0, t));
    }

    /// <summary>진행도 t(0~1)의 크기·투명도·위치를 적용한다.</summary>
    private void Apply(float t)
    {
        float eased = SpellEase.OutCubic(t);
        float scale = Mathf.LerpUnclamped(FromScale, 1f, Overshoot ? SpellEase.OutBack(t) : eased);
        transform.localScale = _baseScale * scale;
        if (Fade)
        {
            // 투명도 묶음은 실제로 투명하게 시작할 때만 만든다. 다른 연출이 투명도를 맡은 요소에는 붙이지 않기 위해서다.
            if (_group == null) { _group = GetComponent<CanvasGroup>(); if (_group == null) _group = gameObject.AddComponent<CanvasGroup>(); }
            _group.alpha = Mathf.Clamp01(t * 1.6f);
        }
        else if (_group != null) _group.alpha = 1f;
        if (_rect != null && Slide != Vector2.zero) _rect.anchoredPosition = _basePosition + Slide * (1 - eased);
    }

    /// <summary>연출을 끝내고 원래 상태로 맞춘다.</summary>
    private void Finish()
    {
        if (_start < 0) return;
        _start = -1f;
        transform.localScale = _baseScale;
        // 붙이는 순간 기본값(투명 시작)으로 한 번 재생됐을 수 있으므로 Fade 설정과 관계없이 투명도를 되돌린다.
        if (_group != null) _group.alpha = 1f;
        if (_rect != null && Slide != Vector2.zero) _rect.anchoredPosition = _basePosition;
    }
}
