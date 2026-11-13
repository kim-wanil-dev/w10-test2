using UnityEngine;
using UnityEngine.UI;

/// <summary>플레이어 초상화 아래의 체력·방어·받을 피해를 표시한다.</summary>
public sealed class SpellPlayerStatusView : MonoBehaviour
{
    [Header("구성")]
    [SerializeField] private SpellHpBarView _health;
    [SerializeField] private Text _defense;
    [SerializeField] private Text _incoming;

    [Header("받을 피해 색")]
    [SerializeField] private Color _dangerColor = new Color(0.937f, 0.42f, 0.396f);
    [SerializeField] private Color _safeColor = new Color(0.522f, 0.553f, 0.651f);

    /// <summary>체력과 방어 수치를 갱신하고 이번 턴 받을 피해만큼 체력 막대 끝을 깜빡이게 한다.</summary>
    public void Bind(int health, int maxHealth, int defense, int incomingDamage)
    {
        _health.Bind(health, maxHealth, incomingDamage, $"체력 {health} / {maxHealth}");
        _health.BindDefense(defense, maxHealth);
        _defense.text = $"방어도 {defense}";
        _incoming.text = incomingDamage > 0 ? $"받을 피해 {incomingDamage}" : "받을 피해 없음";
        _incoming.color = incomingDamage > 0 ? _dangerColor : _safeColor;
    }
}
