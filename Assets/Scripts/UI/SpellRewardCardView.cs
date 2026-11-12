using System;

using UnityEngine;
using UnityEngine.UI;

/// <summary>보상 후보 카드(특수 조각 또는 새 도안)의 종류, 모양, 이름을 표시하고 선택을 전달한다.</summary>
public sealed class SpellRewardCardView : MonoBehaviour
{
    private const float SHAPE_CELL = 44f;
    private const float PATTERN_CELL = 40f;

    [Header("구성")]
    [SerializeField] private Button _button;
    [SerializeField] private Image _stripe;
    [SerializeField] private Text _kind;
    [SerializeField] private Text _name;
    [SerializeField] private RectTransform _shapeArea;
    [SerializeField] private SpellShapeGraphic _shape;
    [SerializeField] private Image[] _patternCells;

    private int _reward;
    private Action<int> _onChoose;

    void Awake()
    {
        _button.onClick.AddListener(Choose);
    }

    /// <summary>조각 보상을 표시한다. 모양 영역은 도형 범위에 맞춰 칸 크기를 유지하고, 도형은 shapeTint로 칠하며 씰링은 sealColor 동그라미로 그린다.</summary>
    public void BindPiece(int reward, string displayName, SpellGame.Card card, Color accent, Color shapeTint, Func<SpellGame.Seal, Color> sealColor, Action<int> onChoose)
    {
        Bind(reward, "조각", displayName, accent, onChoose);
        _shapeArea.gameObject.SetActive(true);
        _shapeArea.sizeDelta = SpellPiece3D.CellBounds(SpellGame.CardCells(card, 0)) * SHAPE_CELL;
        _shape.SetCard(card, 0, false, sealColor); _shape.color = shapeTint;
        foreach (Image cell in _patternCells) cell.gameObject.SetActive(false);
    }

    /// <summary>도안을 1/4 구역 모양으로 표시한다. kind는 카드 상단 종류 문구이며 기본값은 보상용 "새 도안"이다.</summary>
    public void BindPattern(int reward, string displayName, int spell, Color accent, Action<int> onChoose, string kind = "새 도안")
    {
        Bind(reward, kind, displayName, accent, onChoose);
        _shapeArea.gameObject.SetActive(true);
        _shapeArea.sizeDelta = (Vector2)SpellPatternLayout.BaseSize(spell) * PATTERN_CELL;
        _shape.SetPattern(spell); _shape.color = accent;
        foreach (Image cell in _patternCells) cell.gameObject.SetActive(false);
    }

    /// <summary>공통 문구와 강조색, 선택 동작을 설정한다.</summary>
    private void Bind(int reward, string kind, string displayName, Color accent, Action<int> onChoose)
    {
        _reward = reward; _onChoose = onChoose;
        _kind.text = kind; _name.text = displayName; _stripe.color = accent;
    }

    /// <summary>선택한 보상 번호를 전달한다.</summary>
    private void Choose() { _onChoose?.Invoke(_reward); }
}
