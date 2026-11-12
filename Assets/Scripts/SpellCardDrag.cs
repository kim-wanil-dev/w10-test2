using UnityEngine.EventSystems;

/// <summary>손패 카드의 드래그 제스처를 게임 화면에 전달한다.</summary>
public sealed class SpellCardDrag : UnityEngine.MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private SpellGameUI _ui;
    private SpellGame.Card _card;

    /// <summary>드래그할 카드와 화면을 설정한다.</summary>
    public void Setup(SpellGameUI ui, SpellGame.Card card) { _ui = ui; _card = card; }

    public void OnBeginDrag(PointerEventData eventData) { if (isActiveAndEnabled && (_ui.CanInteract || _ui.CanDragHammerCard || _ui.CanDragGlueCard) && eventData.button == PointerEventData.InputButton.Left) _ui.BeginCardDrag(_card, eventData.position); }
    public void OnDrag(PointerEventData eventData) { if (isActiveAndEnabled && (_ui.CanInteract || _ui.CanDragHammerCard || _ui.CanDragGlueCard) && eventData.button == PointerEventData.InputButton.Left) { _ui.MoveCardDrag(eventData.position); _ui.PreviewCardDrag(eventData); } }
    public void OnEndDrag(PointerEventData eventData) { if (isActiveAndEnabled && eventData.button == PointerEventData.InputButton.Left) _ui.EndCardDrag(eventData); }
}

