namespace Shinogi.Domain

open System
open Microsoft.AspNetCore.Identity

type ScoreFunction =
    | Linear
    | Log
    | Exp

type ChallengeLogic =
    | Any
    | All
    | TeamConsensus

type MemberRole =
    | Owner
    | Player
    | AI

type InstanceStatus =
    | Running
    | Stopped
    | Expired

[<AllowNullLiteral>]
type CtfdUser() =
    inherit IdentityUser<Guid>()
    member val DisplayName = "" with get, set

[<CLIMutable>]
type ChallengeCategory =
  { Id: Guid
    Name: string
    SortOrder: int }

[<CLIMutable>]
type ChallengeDifficulty =
  { Id: Guid
    Name: string
    SortOrder: int }

[<CLIMutable>]
type Challenge =
  { Id: Guid
    Name: string
    Category: string
    Difficulty: string
    Description: string
    ValueInitial: int
    ValueMinimum: int
    Decay: int
    Function: ScoreFunction
    Logic: ChallengeLogic
    MaxAttempts: int option
    Published: bool
    ReleaseAt: DateTimeOffset option
    CreatedAt: DateTimeOffset
    RequiresInstance: bool
    InstanceImage: string
    InstancePort: int option
    InstanceLifetimeMinutes: int
    InstanceCpuLimit: string
    InstanceMemoryLimit: string }

[<CLIMutable>]
type Flag =
  { Id: Guid
    ChallengeId: Guid
    Content: string
    ContentHash: string
    CaseSensitive: bool }

[<CLIMutable>]
type Submission =
  { Id: Guid
    AccountId: Guid
    ChallengeId: Guid
    SubmittedAt: DateTimeOffset
    IsCorrect: bool
    ValueAwarded: int
    Ip: string }

[<CLIMutable>]
type Team =
  { Id: Guid
    Name: string
    Token: Guid
    JoinPassword: string
    CreatedAt: DateTimeOffset }

[<CLIMutable>]
type TeamMember =
  { Id: Guid
    TeamId: Guid
    UserId: Guid
    JoinedAt: DateTimeOffset
    Role: MemberRole }

[<CLIMutable>]
type ChallengeFile =
  { Id: Guid
    ChallengeId: Guid
    OriginalName: string
    StoredName: string
    UploadedAt: DateTimeOffset }

[<CLIMutable>]
type ChallengeInstance =
  { Id: Guid
    ChallengeId: Guid
    UserId: Guid
    ContainerId: string
    HostPort: int
    Url: string
    Flag: string
    CreatedAt: DateTimeOffset
    ExpiresAt: DateTimeOffset
    Status: InstanceStatus }

[<CLIMutable>]
type CtfSettings =
  { Id: Guid
    EventStart: DateTimeOffset option
    EventEnd: DateTimeOffset option
    ThemePreset: string }
