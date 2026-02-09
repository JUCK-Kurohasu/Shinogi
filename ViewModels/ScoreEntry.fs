namespace Shinogi.ViewModels

open System
open System.Collections.Generic

[<CLIMutable>]
type ScoreEntry =
  { AccountId: Guid
    DisplayName: string
    TeamName: string
    Score: int }

[<CLIMutable>]
type TimelinePoint =
  { Time: DateTimeOffset
    Score: int }

[<CLIMutable>]
type ScoreboardViewModel =
  { Entries: List<ScoreEntry>
    TeamTimelines: Dictionary<string, List<TimelinePoint>> }
