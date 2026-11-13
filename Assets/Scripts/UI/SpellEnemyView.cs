using System;

using UnityEngine;
using UnityEngine.UI;

/// <summary>적 한 칸의 공격 의도, 실루엣, 이름·체력, 대상·조준·쓰러짐 상태와 예상 피해를 표시한다.</summary>
public sealed class SpellEnemyView : MonoBehaviour
{
    private const float NAME_GAP = 14f;
    private const float AIM_NAME_GAP = 30f;
    private const float INTENT_GAP = 16f;
    private const float BOSS_INTENT_GAP = 48f;
    private const float AIM_PADDING = 22f;

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

    public RectTransform HitRect { get { return _hitRect; } }
    public RectTransform EffectAnchor { get { return _silhouette; } }

    void Awake()
    {
        _hitArea.onClick.AddListener(Click);
    }

    /// <summary>적 번호, 클릭 동작, 조준 미리보기 화면을 설정한다.</summary>
    public void Setup(SpellGameUI ui, int index, Action<int> onClick)
    {
        _index = index; _onClick = onClick; _hover.Setup(ui);
    }

    /// <summary>적 상태와 다음 행동·방어를 표시한다. 조준 피해는 적 방어를 먼저 차감하며 행동 설명을 의도 배지 위에 배치한다.</summary>
    public void Bind(string displayName, int health, int maxHealth, int intent, string patternText, Color color, Vector2 size, bool boss, bool target, bool aiming, int previewDamage, string ability = null, int block = 0, SpellGame.EnemyAction action = SpellGame.EnemyAction.Attack, int actionAmount = 0)
    {
        bool dead = health <= 0;
        float healthTop = _healthRect.anchoredPosition.y + _healthRect.sizeDelta.y;
        float nameBottom = healthTop + 8f;
        _nameRect.anchoredPosition = new Vector2(0, nameBottom);
        float silhouetteBottom = nameBottom + _nameRect.sizeDelta.y + (aiming ? AIM_NAME_GAP : NAME_GAP);
        _silhouette.sizeDelta = size;
        _silhouette.anchoredPosition = new Vector2(0, silhouetteBottom);
        foreach (Image part in _bodyParts) part.color = color;
        _silhouetteGroup.alpha = dead ? 0.22f : 1f;
        _crown.SetActive(boss);
        _intent.gameObject.SetActive(!dead);
        _intent.anchoredPosition = new Vector2(0, silhouetteBottom + size.y + (boss ? BOSS_INTENT_GAP : INTENT_GAP));
        _intentValue.text = action == SpellGame.EnemyAction.Defend ? $"+{actionAmount}" : action == SpellGame.EnemyAction.Charge ? "충전" : action == SpellGame.EnemyAction.Curse ? "저주" : intent.ToString();
        _pattern.gameObject.SetActive(!dead && ability != null);
        _pattern.text = ability ?? patternText;
        if (ability != null)
        {
            RectTransform description = _pattern.rectTransform;
            description.anchorMin = description.anchorMax = new Vector2(0.5f, 1f);
            description.pivot = new Vector2(0.5f, 0f);
            description.anchoredPosition = new Vector2(0, 6);
            description.sizeDelta = new Vector2(360, 80);
            _pattern.fontSize = 16; _pattern.alignment = TextAnchor.LowerCenter;
            _pattern.color = _nameColor;
        }
        _name.text = dead ? $"{displayName} · 쓰러짐" : displayName;
        _name.color = dead ? _deadNameColor : _nameColor;
        _name.fontSize = boss ? 22 : 18;
        _healthRect.gameObject.SetActive(!dead);
        int shownDamage = aiming ? Mathf.Max(0, previewDamage - block) : 0;
        int after = Mathf.Max(0, health - shownDamage);
        _health.Bind(health, maxHealth, shownDamage, shownDamage > 0 ? $"{health} → {after} / {maxHealth}" : $"{health} / {maxHealth}");
        _targetChip.SetActive(false);
        _aimFrame.gameObject.SetActive(aiming && !dead);
        _aimFrame.sizeDelta = size + Vector2.one * AIM_PADDING * 2;
        _aimFrame.anchoredPosition = new Vector2(0, silhouetteBottom - AIM_PADDING);
        _previewChip.SetActive(aiming && !dead && previewDamage > 0);
        _previewValue.text = $"-{shownDamage}";
        _previewResult.text = $"체력 {health} → {after}";
        _hitRect.sizeDelta = new Vector2(Mathf.Max(size.x + AIM_PADDING * 2, 240f), silhouetteBottom + size.y + AIM_PADDING);
        _hitArea.interactable = !dead;
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
