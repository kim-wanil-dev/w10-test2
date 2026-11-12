using System.Collections.Generic;

using Newtonsoft.Json;

/// <summary>pieces.json 최상위 객체다. 형식 버전과 조각 도형 전체 목록을 담는다.</summary>
/// <remarks>JSON 데이터 클래스라 AGENTS 필드 규칙의 예외로 public 프로퍼티를 쓴다.</remarks>
public sealed class SpellPieceTable
{
    [JsonProperty("version", Required = Required.Always)] public int Version { get; set; }
    [JsonProperty("pieces", Required = Required.Always)] public List<SpellPieceData> Pieces { get; set; } = new List<SpellPieceData>();
}
