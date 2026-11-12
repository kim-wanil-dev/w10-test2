using UnityEngine;

using UnityEngine.EventSystems;

/// <summary>접근하는 도둑손을 외부 이미지 없이 그리고 실제 손 영역의 클릭만 저지 입력으로 받는다.</summary>
public sealed class SpellThiefHandGraphic : UnityEngine.UI.MaskableGraphic, IPointerClickHandler
{
    private static readonly Rect[] HAND_PARTS =
    {
        new Rect(28, 26, 42, 38), new Rect(32, 10, 34, 20), new Rect(14, 39, 18, 12),
        new Rect(28, 60, 8, 25), new Rect(39, 60, 8, 31), new Rect(50, 60, 8, 33), new Rect(61, 60, 8, 27)
    };
    private SpellGameUI _ui;
    private SpellGame.Card _target;

    /// <summary>손의 소유 UI와 현재 목표 인스턴스를 연결한다.</summary>
    public void Setup(SpellGameUI ui, SpellGame.Card target) { _ui = ui; _target = target; SetVerticesDirty(); }

    /// <summary>손바닥·손가락·손목을 테두리가 있는 단순 사각형들로 그린다.</summary>
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        foreach (Rect part in HAND_PARTS)
        {
            Quad(vh, new Rect(part.x - 2, part.y - 2, part.width + 4, part.height + 4), new Color(0.16f, 0.08f, 0.14f));
            Quad(vh, part, new Color(0.94f, 0.66f, 0.43f));
        }
        Quad(vh, new Rect(32, 13, 34, 8), new Color(0.44f, 0.27f, 0.54f));
    }

    /// <summary>회전·이동한 실제 손 영역에 해당하는 클릭만 받으며 투명한 사각형 여백은 통과시킨다.</summary>
    public override bool Raycast(Vector2 screenPoint, Camera eventCamera)
    {
        if (!base.Raycast(screenPoint, eventCamera)) return false;
        Vector2 point;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out point)) return false;
        point -= rectTransform.rect.min;
        foreach (Rect part in HAND_PARTS)
            if (new Rect(part.x - 2, part.y - 2, part.width + 4, part.height + 4).Contains(point)) return true;
        return false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (isActiveAndEnabled && eventData.button == PointerEventData.InputButton.Left) _ui.StopTheft(_target);
    }

    /// <summary>그래픽 로컬 좌표의 손 구성 사각형을 메시 끝에 추가한다.</summary>
    private void Quad(UnityEngine.UI.VertexHelper vh, Rect rect, Color color)
    {
        int start = vh.currentVertCount;
        Vector2 origin = rectTransform.rect.min;
        UIVertex vertex = UIVertex.simpleVert; vertex.color = color;
        vertex.position = origin + new Vector2(rect.xMin, rect.yMin); vh.AddVert(vertex);
        vertex.position = origin + new Vector2(rect.xMin, rect.yMax); vh.AddVert(vertex);
        vertex.position = origin + new Vector2(rect.xMax, rect.yMax); vh.AddVert(vertex);
        vertex.position = origin + new Vector2(rect.xMax, rect.yMin); vh.AddVert(vertex);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }
}

