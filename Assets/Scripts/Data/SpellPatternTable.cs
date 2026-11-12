using System.Collections.Generic;

using Newtonsoft.Json;

/// <summary>patterns.json 최상위 객체다. 형식 버전과 도안 전체 목록을 담는다.</summary>
/// <remarks>JSON 데이터 클래스라 AGENTS 필드 규칙의 예외로 public 프로퍼티를 쓴다.</remarks>
public sealed class SpellPatternTable
{
    [JsonProperty("version", Required = Required.Always)] public int Version { get; set; }
    [JsonProperty("patterns", Required = Required.Always)] public List<SpellPatternData> Patterns { get; set; } = new List<SpellPatternData>();
}
