namespace Shinogi.ViewModels

open System
open System.Collections.Generic

[<CLIMutable>]
type ChallengeAdminListItem =
  { Id: Guid
    Name: string
    Category: string
    Difficulty: string
    Published: bool
    CreatedAt: DateTimeOffset }

[<CLIMutable>]
type ChallengeEditViewModel =
  { Id: Guid
    Name: string
    Category: string
    Difficulty: string
    Description: string
    ValueInitial: int
    ValueMinimum: int
    Decay: int
    Function: string
    Logic: string
    MaxAttempts: Nullable<int>
    Published: bool
    ReleaseAt: string
    Categories: List<string>
    Difficulties: List<string> }

[<CLIMutable>]
type FlagCreateViewModel =
  { Content: string
    CaseSensitive: bool }

[<CLIMutable>]
type UserAdminViewModel =
  { Id: Guid
    Email: string
    DisplayName: string
    Roles: string
    TeamName: string }

[<CLIMutable>]
type UserEditViewModel =
  { Id: Guid
    DisplayName: string
    Email: string
    Role: string
    NewPassword: string }

[<CLIMutable>]
type TeamAdminViewModel =
  { Id: Guid
    Name: string
    Token: Guid
    MemberCount: int
    CreatedAt: DateTimeOffset }

[<CLIMutable>]
type TeamEditViewModel =
  { Id: Guid
    Name: string
    JoinPassword: string }

[<CLIMutable>]
type TeamMemberAddViewModel =
  { TeamId: Guid
    Email: string
    Role: string }

[<CLIMutable>]
type CtfSettingsViewModel =
  { EventStart: string
    EventEnd: string
    ThemePreset: string }

[<CLIMutable>]
type MasterItemViewModel =
  { Id: Guid
    Name: string
    SortOrder: int }
