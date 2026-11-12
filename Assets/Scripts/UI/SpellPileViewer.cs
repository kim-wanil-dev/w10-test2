using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>드로우 덱·묘지 열람 창. 조각 한 장씩 타일 그리드로 보여 주고 바깥·닫기로 닫는다.</summary>
public sealed class SpellPileViewer : MonoBehaviour
{
    private const string EMPTY_TEXT = "비어 있습니다";

    [Header("구성")]
    [SerializeField] private Button _blocker;
    [SerializeField] private Text _title;
    [SerializeField] private Text _body;
    [SerializeField] private SpellButtonView _close;
    [SerializeField] private ScrollRect _scroll;
    [SerializeField] private GridLayoutGroup _grid;
    [SerializeField] private SpellPileTileView _tilePrefab;

    private readonly List<SpellPileTileView> _tiles = new List<SpellPileTileView>();
    private Action _onClose;

    void Awake()
    {
        _blocker.onClick.AddListener(Close);
        _close.SetAction(Close);
    }

    /// <summary>제목과 조각 목록, 닫힘 동작을 설정한다. 목록이 비면 본문 자리에 비어 있음 문구를 보인다.</summary>
    public void Bind(SpellGameUI ui, string title, List<SpellGame.Card> cards, Action onClose)
    {
        _title.text = title; _onClose = onClose;
        bool empty = cards.Count == 0;
        _body.gameObject.SetActive(empty);
        _scroll.gameObject.SetActive(!empty);
        if (empty) { _body.text = EMPTY_TEXT; return; }
        while (_tiles.Count < cards.Count) _tiles.Add(Instantiate(_tilePrefab, _grid.transform));
        float cellSize = Mathf.Min(_grid.cellSize.x, _grid.cellSize.y);
        for (int i = 0; i < _tiles.Count; i++)
        {
            bool used = i < cards.Count;
            _tiles[i].gameObject.SetActive(used);
            if (used) _tiles[i].Bind(ui, cards[i], cellSize);
        }
        _scroll.verticalNormalizedPosition = 1f;
    }

    /// <summary>닫힘 동작을 전달한다.</summary>
    private void Close() { _onClose?.Invoke(); }
}
