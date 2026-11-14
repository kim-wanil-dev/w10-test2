using UnityEngine;

/// <summary>SpellScreenIntro가 만든 투명도 묶음이라는 표시다. 다른 코드가 쓰는 CanvasGroup과 구분하는 데만 쓴다.</summary>
public sealed class SpellIntroGroup : MonoBehaviour
{
    private CanvasGroup _group;

    public CanvasGroup Group
    {
        get
        {
            if (_group == null)
            {
                _group = GetComponent<CanvasGroup>();
                if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            }
            return _group;
        }
    }
}
