using UnityEngine;
using UnityEngine.UI;

/// <summary>더미 열람 그리드의 한 칸. 조각 모양과 성질 테두리를 표시한다.</summary>
public sealed class SpellPileTileView : MonoBehaviour
{
    private const float SHAPE_MARGIN = 14f;
    private const float MAX_SHAPE_CELL = 40f;

    [Header("구성")]
    [SerializeField] private Image _border;
    [SerializeField] private RectTransform _shapeArea;
    [SerializeField] private SpellShapeGraphic _shape;

    [Header("테두리")]
    [SerializeField] private Color _normalBorder = Color.white;

    /// <summary>칸 하나에 조각 하나를 그린다. 모양은 여백을 두고 cellSize 안에 들어가도록 손패와 같은 칸 크기를 상한으로 비율을 유지해 맞추며, 성질이 있으면 테두리를 강조색으로 칠한다.</summary>
    public void Bind(SpellGameUI ui, SpellGame.Card card, float cellSize)
    {
        Vector2 bounds = SpellPiece3D.CellBounds(SpellGame.CardCells(card, 0));
        float inner = cellSize - SHAPE_MARGIN * 2;
        float shapeCell = Mathf.Min(MAX_SHAPE_CELL, inner / Mathf.Max(bounds.x, bounds.y));
        _shapeArea.sizeDelta = bounds * shapeCell;
        _shape.SetCard(card, 0, false, ui.SealColor);
        _shape.color = ui.ShapeTint(card);
        _border.color = SpellGameUI.HasTraits(card) ? ui.AccentColor(card) : _normalBorder;
    }
}
