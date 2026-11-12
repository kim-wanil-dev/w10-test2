using System.Collections.Generic;

using Newtonsoft.Json;

/// <summary>도안 하나의 효과, 칸 모양과 장착 화면 기본 배치·색이다. 목록 순서가 런타임 도안 번호다.</summary>
/// <remarks>JSON 데이터 클래스라 AGENTS 필드 규칙의 예외로 public 프로퍼티를 쓴다.</remarks>
public sealed class SpellPatternData
{
    [JsonProperty("id", Required = Required.Always)] public string Id { get; set; }
    [JsonProperty("name", Required = Required.Always)] public string Name { get; set; }
    [JsonProperty("kind", Required = Required.Always)] public SpellGame.SpellKind Kind { get; set; }
    [JsonProperty("baseValue", Required = Required.Always)] public int BaseValue { get; set; }
    // 산탄(Scatter) 도안의 타격 횟수다. 조각 수와 관계없이 이 횟수만큼 무작위 적을 친다. 이전 JSON에 없으면 1이다.
    [JsonProperty("hits")] public int Hits { get; set; } = 1;
    [JsonProperty("keyword", Required = Required.Always)] public SpellGame.Keyword Keyword { get; set; }
    // 아래 두 값은 한 방·순수 조건을 만족했을 때의 보상이다. 이전 JSON에 없으면 추가 피해 기본값을 쓴다.
    [JsonProperty("keywordBonus")] public SpellGame.KeywordBonus KeywordBonus { get; set; } = SpellGame.KeywordBonus.Damage;
    [JsonProperty("keywordBonusValue")] public int KeywordBonusValue { get; set; } = SpellGame.DEFAULT_KEYWORD_BONUS;
    [JsonProperty("cells", Required = Required.Always)] public List<SpellCellData> Cells { get; set; } = new List<SpellCellData>();
    [JsonProperty("layoutX", Required = Required.Always)] public int LayoutX { get; set; }
    [JsonProperty("layoutY", Required = Required.Always)] public int LayoutY { get; set; }
    [JsonProperty("layoutRotation", Required = Required.Always)] public int LayoutRotation { get; set; }
    [JsonProperty("color", Required = Required.Always)] public string Color { get; set; }
    // 시전 연출 프리셋이다. 이전 JSON에 없으면 효과 종류로 고르는 Auto다.
    [JsonProperty("effect")] public SpellGame.CastEffect CastEffect { get; set; } = SpellGame.CastEffect.Auto;
    [JsonProperty("isStarter", Required = Required.Always)] public bool IsStarter { get; set; }
    // 전투 보상의 도안 후보에 넣을지 여부다. 이전 JSON에 없으면 후보에 넣는다.
    [JsonProperty("isInRewardPool")] public bool IsInRewardPool { get; set; } = true;
}
