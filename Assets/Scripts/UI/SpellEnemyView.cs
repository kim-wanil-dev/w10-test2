using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>적 한 칸의 공격 의도, 실루엣, 이름·체력, 대상·조준·쓰러짐 상태와 예상 피해를 표시한다.
/// 가만히 있을 때는 천천히 숨 쉬듯 움직이고, 공격이 닿는 순간에 맞춰 흔들리며 번쩍이고 체력이 깎인다. 자기 차례에는 앞으로 덤벼든다.</summary>
public sealed class SpellEnemyView : MonoBehaviour
{
    private const float NAME_GAP = 30f;
    private const float INTENT_GAP = 16f;
    private const float BOSS_INTENT_GAP = 48f;
    private const float AIM_PADDING = 22f;
    // 화면 위쪽 끝과 의도 배지 사이에 남길 여백이다.
    private const float INTENT_TOP_MARGIN = 6f;

    // 숨쉬기: 위아래로 움직이는 폭과 한 번 오르내리는 시간이다.
    private const float BOB_PIXELS = 3.5f;
    private const float BOB_SECONDS = 2.4f;
    // 피격: 흔들리는 시간·폭과 흰색으로 번쩍이는 세기다.
    private const float HIT_SECONDS = 0.34f;
    private const float HIT_SHAKE_PIXELS = 16f;
    private const float HIT_FLASH = 0.85f;
    // 공격: 앞으로 덤벼드는 거리와 걸리는 시간이다. 닿는 순간은 LUNGE_IMPACT_SECONDS 뒤다.
    private const float LUNGE_PIXELS = 46f;
    private const float LUNGE_SECONDS = 0.42f;
    public const float LUNGE_IMPACT_SECONDS = 0.13f;
    // 쓰러짐: 가라앉으며 흐려지는 시간이다.
    private const float DEATH_SECONDS = 0.5f;
    private const float DEAD_ALPHA = 0.22f;

    /// <summary>예약된 타격 하나다. 시각이 되면 체력을 깎고 피격 반응을 낸다.</summary>
    private struct QueuedHit
    {
        public float Time;
        public int Damage;
        public Color Color;
    }

    [Header("구성")]
    [SerializeField] private RectTransform _rect;
    [SerializeField] private Button _hitArea;
    [SerializeField] private RectTransform _hitRect;
    [SerializeField] private SpellEnemyHover _hover;
    [SerializeField] private RectTransform _silhouette;
    [SerializeField] private Image[] _bodyParts;
    [SerializeField] private CanvasGroup _silhouetteGroup;
    [SerializeField] private GameObject _crown;
    [SerializeField] private RectTransform _intent;
    [SerializeField] private Text _intentValue;
    [SerializeField] private Text _pattern;
    [SerializeField] private RectTransform _nameRect;
    [SerializeField] private Text _name;
    [SerializeField] private RectTransform _healthRect;
    [SerializeField] private SpellHpBarView _health;
    [SerializeField] private GameObject _targetChip;
    [SerializeField] private RectTransform _aimFrame;
    [SerializeField] private GameObject _previewChip;
    [SerializeField] private Text _previewValue;
    [SerializeField] private Text _previewResult;
    [SerializeField] private Image _impact;

    [Header("표시")]
    [SerializeField] private Color _nameColor = Color.white;
    [SerializeField] private Color _deadNameColor = Color.gray;

    private int _index;
    private Action<int> _onClick;

    // 마지막으로 받은 표시 내용이다. 예약된 타격이 체력을 깎을 때 같은 내용으로 다시 그린다.
    private bool _hasBound;
    private string _displayName, _patternText, _ability;
    private int _boundHealth, _maxHealth, _intentAmount, _previewDamage, _block, _actionAmount;
    private Color _bodyColor;
    private Vector2 _size;
    private bool _isBoss, _isAiming;
    private SpellGame.EnemyAction _action;

    // 화면에 보여 주는 체력이다. 타격이 닿기 전까지는 모델보다 높게 남아 있다.
    private int _shownHealth;
    private readonly List<QueuedHit> _hits = new List<QueuedHit>();
    private Vector2 _silhouetteBase;
    private float _phase;
    private float _hitStart = -1f, _hitStrength = 1f;
    private float _attackStart = -1f;
    private float _deathStart = -1f;
    private bool _isShownDead;

    public RectTransform HitRect { get { return _hitRect; } }
    public RectTransform EffectAnchor { get { return _silhouette; } }

    void Awake()
    {
        _hitArea.onClick.AddListener(Click);
        // 적마다 숨쉬기가 어긋나도록 시작 위상을 다르게 둔다.
        _phase = UnityEngine.Random.Range(0f, BOB_SECONDS);
    }

    void OnDisable() { ResetPresentation(); }

    /// <summary>적 번호, 클릭 동작, 조준 미리보기 화면을 설정한다.</summary>
    public void Setup(SpellGameUI ui, int index, Action<int> onClick)
    {
        _index = index; _onClick = onClick; _hover.Setup(ui);
    }

    /// <summary>예약된 타격과 진행 중인 움직임을 지운다. 전투가 바뀌거나 화면이 꺼질 때 부른다.</summary>
    public void ResetPresentation()
    {
        _hits.Clear(); _hasBound = false;
        _hitStart = _attackStart = _deathStart = -1f; _isShownDead = false;
        if (_silhouette != null) _silhouette.localScale = Vector3.one;
    }

    /// <summary>atTime(실제 시간)에 damage만큼 맞은 것으로 보여 주도록 예약한다. 그때까지 체력 막대는 깎이기 전 값을 유지한다. 시전 직후, 화면을 갱신하기 전에 부른다.</summary>
    public void QueueHit(float atTime, int damage, Color color)
    {
        _hits.Add(new QueuedHit { Time = atTime, Damage = damage, Color = color });
    }

    /// <summary>delay초 뒤에 플레이어 쪽으로 덤벼들었다가 돌아간다.</summary>
    public void PlayAttack(float delay) { _attackStart = Time.unscaledTime + Mathf.Max(0, delay); }

    /// <summary>적 상태와 다음 행동·방어를 표시한다. 조준 피해는 적 방어를 먼저 차감하며 행동 설명을 의도 배지 위에 배치한다.</summary>
    public void Bind(string displayName, int health, int maxHealth, int intent, string patternText, Color color, Vector2 size, bool boss, bool target, bool aiming, int previewDamage, string ability = null, int block = 0, SpellGame.EnemyAction action = SpellGame.EnemyAction.Attack, int actionAmount = 0)
    {
        bool isSameEnemy = _hasBound && displayName == _displayName && maxHealth == _maxHealth;
        if (!isSameEnemy)
        {
            // 다른 적이 이 칸을 쓰게 됐으면 이전 연출을 버리고 지금 상태로 바로 시작한다.
            ResetPresentation();
            _shownHealth = health; _isShownDead = health <= 0;
        }
        else if (_hits.Count == 0 || health > _shownHealth)
        {
            // 예약 없이 체력이 깎였으면(시전 연출을 거치지 않은 피해) 그 자리에서 바로 반응한다.
            if (health < _shownHealth && isActiveAndEnabled) ReactToHit(_shownHealth - health, SpellStyle.Parchment, health <= 0);
            _shownHealth = health;
        }
        _hasBound = true;
        _displayName = displayName; _boundHealth = health; _maxHealth = maxHealth; _intentAmount = intent; _patternText = patternText; _bodyColor = color; _size = size;
        _isBoss = boss; _isAiming = aiming; _previewDamage = previewDamage; _ability = ability; _block = block; _action = action; _actionAmount = actionAmount;
        Apply();
    }

    /// <summary>기억해 둔 표시 내용과 지금 보여 주는 체력으로 배치·문구·막대를 갱신한다.</summary>
    private void Apply()
    {
        int health = _shownHealth;
        bool dead = health <= 0;
        float healthTop = _healthRect.anchoredPosition.y + _healthRect.sizeDelta.y;
        float nameBottom = healthTop + 8f;
        _nameRect.anchoredPosition = new Vector2(0, nameBottom);
        float silhouetteBottom = nameBottom + _nameRect.sizeDelta.y + NAME_GAP;
        _silhouette.sizeDelta = _size;
        _silhouetteBase = new Vector2(0, silhouetteBottom);
        if (dead && !_isShownDead) _deathStart = isActiveAndEnabled ? Time.unscaledTime : -1f;
        if (!dead) _deathStart = -1f;
        _isShownDead = dead;
        _crown.SetActive(_isBoss);
        _intent.gameObject.SetActive(!dead);
        // 큰 적은 의도 배지가 화면 위로 넘어가지 않게 위쪽 끝 안으로 당긴다.
        float intentY = silhouetteBottom + _size.y + (_isBoss ? BOSS_INTENT_GAP : INTENT_GAP);
        float top = -_rect.anchoredPosition.y;
        if (top > 0) intentY = Mathf.Min(intentY, top - _intent.sizeDelta.y - INTENT_TOP_MARGIN);
        _intent.anchoredPosition = new Vector2(0, intentY);
        _intentValue.text = _action == SpellGame.EnemyAction.Defend ? $"+{_actionAmount}" : _action == SpellGame.EnemyAction.Charge ? "충전" : _action == SpellGame.EnemyAction.Curse ? "저주" : _intentAmount.ToString();
        _pattern.gameObject.SetActive(!dead && _ability != null);
        _pattern.text = _ability ?? _patternText;
        if (_ability != null)
        {
            RectTransform description = _pattern.rectTransform;
            description.anchorMin = description.anchorMax = new Vector2(0.5f, 1f);
            description.pivot = new Vector2(0.5f, 0f);
            description.anchoredPosition = new Vector2(0, 6);
            description.sizeDelta = new Vector2(360, 80);
            _pattern.fontSize = 16; _pattern.alignment = TextAnchor.LowerCenter;
            _pattern.color = _nameColor;
        }
        _name.text = dead ? $"{_displayName} · 쓰러짐" : _displayName;
        _name.color = dead ? _deadNameColor : _nameColor;
        _name.fontSize = _isBoss ? 22 : 18;
        _healthRect.gameObject.SetActive(!dead);
        int shownDamage = _isAiming ? Mathf.Max(0, _previewDamage - _block) : 0;
        int after = Mathf.Max(0, health - shownDamage);
        _health.Bind(health, _maxHealth, shownDamage, shownDamage > 0 ? $"{health} → {after} / {_maxHealth}" : $"{health} / {_maxHealth}");
        _targetChip.SetActive(false);
        _aimFrame.gameObject.SetActive(_isAiming && !dead);
        _aimFrame.sizeDelta = _size + Vector2.one * AIM_PADDING * 2;
        _aimFrame.anchoredPosition = new Vector2(0, silhouetteBottom - AIM_PADDING);
        _previewChip.SetActive(_isAiming && !dead && _previewDamage > 0);
        _previewValue.text = $"-{shownDamage}";
        _previewResult.text = $"체력 {health} → {after}";
        _hitRect.sizeDelta = new Vector2(Mathf.Max(_size.x + AIM_PADDING * 2, 240f), silhouetteBottom + _size.y + AIM_PADDING);
        _hitArea.interactable = !dead;
        Animate();
    }

    void Update()
    {
        if (!_hasBound) return;
        float now = Time.unscaledTime;
        bool changed = false;
        for (int i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Time > now) continue;
            QueuedHit hit = _hits[i];
            _hits.RemoveAt(i--);
            int before = _shownHealth;
            _shownHealth = Mathf.Max(_boundHealth, _shownHealth - hit.Damage);
            // 마지막 예약이 끝나면 모델 값과 어긋나지 않게 맞춘다.
            if (_hits.Count == 0) _shownHealth = _boundHealth;
            ReactToHit(hit.Damage, hit.Color, before > 0 && _shownHealth <= 0);
            changed = true;
        }
        if (changed) Apply(); else Animate();
    }

    /// <summary>맞은 순간의 반응을 낸다. 흔들리며 번쩍이고 피해 숫자를 띄우며, 쓰러뜨린 타격은 화면을 더 크게 흔든다.</summary>
    private void ReactToHit(int damage, Color color, bool lethal)
    {
        _hitStart = Time.unscaledTime;
        _hitStrength = Mathf.Lerp(0.6f, 1.3f, Mathf.Clamp01(damage / 12f));
        SpellJuice juice = SpellJuice.Instance;
        if (juice == null) return;
        Vector2 above = new Vector2(0, _size.y * 0.5f + 6f);
        if (damage > 0) juice.Float($"-{damage}", _silhouette, above, Color.Lerp(color, Color.white, 0.55f), Mathf.Min(64, 40 + damage));
        else juice.Float("막힘", _silhouette, above, SpellStyle.Guard, 32);
        if (lethal) { juice.Shake(10f, 0.3f); juice.Flash(Color.white, 0.1f, 0.18f); }
        else if (damage > 0) juice.Shake(Mathf.Lerp(3f, 7f, Mathf.Clamp01(damage / 12f)), 0.18f);
    }

    /// <summary>숨쉬기·피격·덤벼들기·쓰러짐을 합쳐 실루엣의 위치, 크기, 색과 투명도를 정한다.</summary>
    private void Animate()
    {
        float now = Time.unscaledTime;
        Vector2 offset = Vector2.zero;
        Vector2 scale = Vector2.one;
        float flash = 0f, alpha = 1f, gray = 0f;
        if (!_isShownDead)
        {
            float breath = Mathf.Sin((now + _phase) * Mathf.PI * 2 / BOB_SECONDS);
            offset.y += breath * BOB_PIXELS;
            scale.y += breath * 0.012f;
        }
        if (_hitStart >= 0)
        {
            float t = (now - _hitStart) / HIT_SECONDS;
            if (t >= 1) _hitStart = -1f;
            else
            {
                float decay = (1 - t) * (1 - t);
                offset.x += Mathf.Sin(t * Mathf.PI * 7f) * HIT_SHAKE_PIXELS * decay * _hitStrength;
                scale += new Vector2(0.09f, -0.07f) * decay * _hitStrength;
                flash = HIT_FLASH * decay;
            }
        }
        if (_attackStart >= 0 && now >= _attackStart)
        {
            float t = (now - _attackStart) / LUNGE_SECONDS;
            if (t >= 1) _attackStart = -1f;
            else
            {
                // 빠르게 내리꽂고 천천히 제자리로 돌아간다.
                float reach = LUNGE_IMPACT_SECONDS / LUNGE_SECONDS;
                float lunge = t < reach ? SpellEase.InCubic(t / reach) : 1 - SpellEase.OutCubic((t - reach) / (1 - reach));
                offset.y -= LUNGE_PIXELS * lunge;
                scale += new Vector2(0.06f, 0.06f) * lunge;
            }
        }
        if (_isShownDead)
        {
            float t = _deathStart >= 0 ? Mathf.Clamp01((now - _deathStart) / DEATH_SECONDS) : 1f;
            float eased = SpellEase.OutCubic(t);
            alpha = Mathf.Lerp(1f, DEAD_ALPHA, eased);
            offset.y -= 18f * eased;
            gray = eased;
        }
        _silhouette.anchoredPosition = _silhouetteBase + offset;
        _silhouette.localScale = new Vector3(scale.x, scale.y, 1f);
        _silhouetteGroup.alpha = alpha;
        Color body = _bodyColor;
        if (gray > 0) { float luminance = body.grayscale; body = Color.Lerp(body, new Color(luminance, luminance, luminance, body.a), gray * 0.7f); }
        if (flash > 0) body = Color.Lerp(body, Color.white, flash);
        foreach (Image part in _bodyParts) part.color = body;
    }

    /// <summary>이름과 체력 바의 너비를 슬롯 간격에 맞춘다.</summary>
    public void SetWidth(float width)
    {
        _healthRect.sizeDelta = new Vector2(width, _healthRect.sizeDelta.y);
        _nameRect.sizeDelta = new Vector2(width, _nameRect.sizeDelta.y);
    }

    /// <summary>시전 직후 적 위에 짧게 깜빡이는 색을 설정한다. alpha가 0이면 숨긴다.</summary>
    public void SetImpact(Color color, float alpha)
    {
        _impact.gameObject.SetActive(alpha > 0);
        _impact.color = new Color(color.r, color.g, color.b, alpha);
    }

    /// <summary>화면 좌표가 이 적의 클릭 영역 안인지 반환한다.</summary>
    public bool Contains(Vector2 screenPosition, Camera eventCamera)
    {
        return gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(_hitRect, screenPosition, eventCamera);
    }

    /// <summary>적 클릭을 화면에 전달한다.</summary>
    private void Click() { _onClick?.Invoke(_index); }
}
