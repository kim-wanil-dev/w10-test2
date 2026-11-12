using System;

using UnityEngine;
using UnityEngine.UI;

/// <summary>손패 조각 카드의 모양·이름·특수 효과와 흐림·선택 상태를 표시하고 입력 컴포넌트를 연결한다.</summary>
public sealed class SpellHandCardView : MonoBehaviour
{
    private const float CELL = 56f;
    private const float RECT_CELL = 40f;

    [Header("구성")]
    [SerializeField] private Image _border;
    [SerializeField] private RectTransform _shapeArea;
    [SerializeField] private SpellShapeGraphic _shape;
    [SerializeField] private Image _cellGuide;
    [SerializeField] private Text _name;
    [SerializeField] private GameObject _effectChip;
    [SerializeField] private Image _effectChipFill;
    [SerializeField] private Text _effectLabel;
    [SerializeField] private Image _effectIcon;
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private Button _button;
    [SerializeField] private SpellCardDrag _drag;
    [SerializeField] private SpellPieceTooltip _tooltip;

    [Header("테두리")]
    [SerializeField] private Sprite _thinRing;
    [SerializeField] private Sprite _thickRing;
    [SerializeField] private Color _normalBorder = Color.white;
    [SerializeField] private Color _selectedBorder = Color.white;

    [Header("특수 효과 표식")]
    [SerializeField] private Sprite _flameIcon;
    [SerializeField] private Sprite _guardIcon;
    [SerializeField] private Sprite _resonanceIcon;
    [SerializeField] private Sprite _afterimageIcon;

    private SpellGame.Card _card;
    private Action<SpellGame.Card> _onClick;

    public SpellGame.Card Card { get { return _card; } }

    void Awake()
    {
        _button.onClick.AddListener(Click);
    }

    /// <summary>카드를 표시하고 드래그·설명 입력을 화면에 연결한다. effectColor는 코팅·씰링 등 성질이 있는 조각일 때만 쓴다.</summary>
    public void Bind(SpellGameUI ui, SpellGame.Card card, string displayName, string effectText, Color effectColor, bool dimmed, bool selected, Action<SpellGame.Card> onClick)
    {
        _card = card; _onClick = onClick;
        _drag.Setup(ui, card); _tooltip.Setup(ui, card);
        bool special = SpellGameUI.HasTraits(card);
        Vector2 bounds = SpellPiece3D.CellBounds(SpellGame.CardCells(card, 0));
        _shapeArea.sizeDelta = bounds * (Mathf.Max(bounds.x, bounds.y) > 1 ? RECT_CELL : CELL);
        _shape.SetCard(card, 0, false, ui.SealColor);
        _shape.color = ui.ShapeTint(card);
        _name.text = displayName;
        _effectChip.SetActive(special);
        _effectIcon.gameObject.SetActive(special);
        if (special)
        {
            _effectLabel.text = effectText; _effectLabel.color = effectColor;
            _effectChipFill.color = new Color(effectColor.r, effectColor.g, effectColor.b, 0.22f);
            _effectIcon.sprite = EffectIcon(card); _effectIcon.color = effectColor;
        }
        _border.sprite = special || selected ? _thickRing : _thinRing;
        _border.color = selected ? _selectedBorder : special ? effectColor : _normalBorder;
        _group.alpha = dimmed ? 0.42f : 1f;
    }

    /// <summary>카드의 코팅이나 이전 효과에 맞는 표식 스프라이트를 반환한다. 씰링만 있으면 씰링 표식을 쓴다.</summary>
    private Sprite EffectIcon(SpellGame.Card card)
    {
        if (card.Coating == SpellGame.Coating.Amplify) return _flameIcon;
        if (card.Coating == SpellGame.Coating.Guard) return _guardIcon;
        return SpellGame.EffectCount(card, SpellGame.Effect.Resonance) > 0 ? _resonanceIcon : _afterimageIcon;
    }

    /// <summary>카드 클릭을 화면에 전달한다.</summary>
    private void Click() { if (_card != null) _onClick?.Invoke(_card); }
}
