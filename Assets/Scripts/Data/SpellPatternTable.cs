using System.Collections.Generic;

using Newtonsoft.Json;

/// <summary>도안 프리셋 하나의 이름과 도안 목록을 담는다.</summary>
/// <remarks>JSON 데이터 클래스라 AGENTS 필드 규칙의 예외로 public 프로퍼티를 쓴다.</remarks>
public sealed class SpellPatternPreset
{
    [JsonProperty("name", Required = Required.Always)] public string Name { get; set; } = "";
    [JsonProperty("patterns", Required = Required.Always)] public List<SpellPatternData> Patterns { get; set; } = new List<SpellPatternData>();
}

/// <summary>patterns.json 최상위 객체다. 형식 버전, 게임에 적용 중인 도안 목록과 프리셋 목록을 담는다.</summary>
/// <remarks>JSON 데이터 클래스라 AGENTS 필드 규칙의 예외로 public 프로퍼티를 쓴다.</remarks>
public sealed class SpellPatternTable
{
    [JsonProperty("version", Required = Required.Always)] public int Version { get; set; }
    [JsonProperty("activePreset")] public int ActivePreset { get; set; }
    [JsonProperty("patterns", Required = Required.Always)] public List<SpellPatternData> Patterns { get; set; } = new List<SpellPatternData>();
    [JsonProperty("presets")] public List<SpellPatternPreset> Presets { get; set; } = new List<SpellPatternPreset>();
}
