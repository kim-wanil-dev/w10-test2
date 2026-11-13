using UnityEngine;
using UnityEngine.UI;

/// <summary>플레이어 초상화 아래의 체력·방어·받을 피해를 표시한다. 맞으면 초상화가 흔들리며 붉게 번쩍인다.</summary>
public sealed class SpellPlayerStatusView : MonoBehaviour
{
    private const float HIT_SECONDS = 0.38f;
    private const float HIT_SHAKE_PIXELS = 13f;
    private const float GUARD_SECONDS = 0.45f;

    [Header("구성")]
    [SerializeField] private SpellHpBarView _health;
    [SerializeField] private Text _defense;
    [SerializeField] private Text _incoming;

    [Header("받을 피해 색")]
    [SerializeField] private Color _dangerColor = new Color(0.937f, 0.42f, 0.396f);
    [SerializeField] private Color _safeColor = new Color(0.522f, 0.553f, 0.651f);

    private RectTransform _portrait;
    private Image _portraitImage;
    private Vector2 _portraitBase;
    private Color _portraitColor;
    private float _hitStart = -1f;
    private float _guardStart = -1f;
    private Vector3 _defenseScale = Vector3.one;

    /// <summary>떠오르는 숫자와 연출이 기준으로 삼을 초상화 영역이다. 초상화를 찾지 못하면 이 뷰 전체를 쓴다.</summary>
    public RectTransform Portrait { get { return _portrait != null ? _portrait : (RectTransform)transform; } }

    void Awake()
    {
        _portrait = transform.Find("Portrait") as RectTransform;
        if (_portrait != null)
        {
            _portraitImage = _portrait.GetComponent<Image>();
            _portraitBase = _portrait.anchoredPosition;
            if (_portraitImage != null) _portraitColor = _portraitImage.color;
        }
        _defenseScale = _defense.transform.localScale;
    }

    void OnDisable()
    {
        _hitStart = _guardStart = -1f;
        if (_portrait != null) _portrait.anchoredPosition = _portraitBase;
        if (_portraitImage != null) _portraitImage.color = _portraitColor;
        _defense.transform.localScale = _defenseScale;
    }

    /// <summary>체력과 방어 수치를 갱신하고 이번 턴 받을 피해만큼 체력 막대 끝을 깜빡이게 한다.</summary>
    public void Bind(int health, int maxHealth, int defense, int incomingDamage)
    {
        _health.Bind(health, maxHealth, incomingDamage, $"체력 {health} / {maxHealth}");
        _health.BindDefense(defense, maxHealth);
        _defense.text = $"방어도 {defense}";
        _incoming.text = incomingDamage > 0 ? $"받을 피해 {incomingDamage}" : "받을 피해 없음";
        _incoming.color = incomingDamage > 0 ? _dangerColor : _safeColor;
    }

    /// <summary>지금부터 seconds초 동안 체력 막대 갱신을 미룬다. 적의 공격이 닿는 순간에 깎이게 할 때 화면 갱신 전에 부른다.</summary>
    public void HoldHealth(float seconds) { _health.Hold(seconds); }

    /// <summary>맞은 반응을 낸다. 초상화가 좌우로 흔들리며 붉게 번쩍인다.</summary>
    public void PlayHit() { _hitStart = Time.unscaledTime; }

    /// <summary>방어도를 얻은 반응을 낸다. 방어도 글자가 한 번 커졌다 돌아온다.</summary>
    public void PlayGuard() { _guardStart = Time.unscaledTime; }

    void Update()
    {
        float now = Time.unscaledTime;
        if (_hitStart >= 0 && _portrait != null)
        {
            float t = (now - _hitStart) / HIT_SECONDS;
            if (t >= 1)
            {
                _hitStart = -1f; _portrait.anchoredPosition = _portraitBase;
                if (_portraitImage != null) _portraitImage.color = _portraitColor;
            }
            else
            {
                float decay = (1 - t) * (1 - t);
                _portrait.anchoredPosition = _portraitBase + new Vector2(Mathf.Sin(t * Mathf.PI * 7f) * HIT_SHAKE_PIXELS * decay, 0);
                if (_portraitImage != null) _portraitImage.color = Color.Lerp(_portraitColor, SpellStyle.Danger, 0.75f * decay);
            }
        }
        if (_guardStart >= 0)
        {
            float t = (now - _guardStart) / GUARD_SECONDS;
            if (t >= 1) { _guardStart = -1f; _defense.transform.localScale = _defenseScale; }
            else _defense.transform.localScale = _defenseScale * (1f + 0.28f * SpellEase.Punch(t) * 2f);
        }
    }
}
