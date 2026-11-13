using System.Collections.Generic;

using UnityEngine;

/// <summary>화면이 켜질 때 바로 아래 요소들을 위에서부터 차례로 떠오르게 한다. 타이틀·결과·이벤트 화면처럼 요소 위치가 고정된 화면에 쓴다.
/// 크기는 건드리지 않으므로 버튼 반응과 함께 쓸 수 있고, 다른 코드가 투명도를 쓰는 요소(이미 CanvasGroup이 있는 요소)는 위치만 움직인다.</summary>
public sealed class SpellScreenIntro : MonoBehaviour
{
    private const float STEP_SECONDS = 0.07f;
    private const float ITEM_SECONDS = 0.4f;
    private const float RISE = 26f;

    private struct Item
    {
        public RectTransform Rect;
        public CanvasGroup Group;
        public Vector2 Base;
    }

    private readonly List<Item> _items = new List<Item>();
    private float _start = -1f;

    /// <summary>화면에 차례로 떠오르는 연출을 붙인다. 이미 있으면 그대로 돌려준다.</summary>
    public static SpellScreenIntro Attach(GameObject screen)
    {
        SpellScreenIntro intro = screen.GetComponent<SpellScreenIntro>();
        return intro != null ? intro : screen.AddComponent<SpellScreenIntro>();
    }

    void OnEnable() { Play(); }

    void OnDisable() { Finish(); }

    /// <summary>지금 켜져 있는 바로 아래 요소들로 연출을 처음부터 다시 재생한다.</summary>
    public void Play()
    {
        Finish();
        _items.Clear();
        foreach (Transform child in transform)
        {
            RectTransform rect = child as RectTransform;
            if (rect == null || !child.gameObject.activeSelf) continue;
            CanvasGroup group = null;
            SpellIntroGroup marker = child.GetComponent<SpellIntroGroup>();
            if (marker != null) group = marker.Group;
            else if (child.GetComponent<CanvasGroup>() == null)
            {
                // 이 연출이 만든 투명도 묶음만 건드리도록 표시를 남긴다.
                marker = child.gameObject.AddComponent<SpellIntroGroup>();
                group = marker.Group;
            }
            _items.Add(new Item { Rect = rect, Group = group, Base = rect.anchoredPosition });
        }
        _start = Time.unscaledTime;
        Apply();
    }

    void Update()
    {
        if (_start < 0) return;
        if (Time.unscaledTime - _start >= (_items.Count - 1) * STEP_SECONDS + ITEM_SECONDS) { Finish(); return; }
        Apply();
    }

    private void Apply()
    {
        float elapsed = Time.unscaledTime - _start;
        for (int i = 0; i < _items.Count; i++)
        {
            Item item = _items[i];
            if (item.Rect == null) continue;
            float t = Mathf.Clamp01((elapsed - i * STEP_SECONDS) / ITEM_SECONDS);
            float eased = SpellEase.OutCubic(t);
            item.Rect.anchoredPosition = item.Base + Vector2.down * RISE * (1 - eased);
            if (item.Group != null) item.Group.alpha = Mathf.Clamp01(t * 1.5f);
        }
    }

    private void Finish()
    {
        if (_start < 0) return;
        _start = -1f;
        foreach (Item item in _items)
        {
            if (item.Rect == null) continue;
            item.Rect.anchoredPosition = item.Base;
            if (item.Group != null) item.Group.alpha = 1f;
        }
    }
}
