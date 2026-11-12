using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>도안 이동 핸들에서 공유 격자로 도안을 드래그한다.</summary>
public sealed class SpellPatternDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private SpellGameUI _ui;
    private int _pattern;

    /// <summary>이 항목이 이동할 도안을 설정한다.</summary>
    public void Setup(SpellGameUI ui, int pattern) { _ui = ui; _pattern = pattern; }
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (isActiveAndEnabled && eventData.button == PointerEventData.InputButton.Left) _ui.BeginPatternDrag(eventData, _pattern);
    }
    public void OnDrag(PointerEventData eventData)
    {
        if (isActiveAndEnabled && eventData.button == PointerEventData.InputButton.Left) _ui.PreviewPatternDrag(eventData);
    }
    public void OnEndDrag(PointerEventData eventData)
    {
        if (isActiveAndEnabled && eventData.button == PointerEventData.InputButton.Left) _ui.EndPatternDrag(eventData);
    }
}

