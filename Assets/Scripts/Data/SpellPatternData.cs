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
    [JsonProperty("keyword", Required = Required.Always)] public SpellGame.Keyword Keyword { get; set; }
    [JsonProperty("cells", Required = Required.Always)] public List<SpellCellData> Cells { get; set; } = new List<SpellCellData>();
    [JsonProperty("layoutX", Required = Required.Always)] public int LayoutX { get; set; }
    [JsonProperty("layoutY", Required = Required.Always)] public int LayoutY { get; set; }
    [JsonProperty("layoutRotation", Required = Required.Always)] public int LayoutRotation { get; set; }
    [JsonProperty("color", Required = Required.Always)] public string Color { get; set; }
    [JsonProperty("isStarter", Required = Required.Always)] public bool IsStarter { get; set; }
}
