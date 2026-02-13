namespace Shinogi.ViewModels

open System
open System.Collections.Generic

[<CLIMutable>]
type ProfileViewModel =
  { Email: string
    DisplayName: string
    TeamName: string
    IsAdmin: bool
    TotalScore: int
    SolvedCount: int }

[<CLIMutable>]
type ProfileSettingsViewModel =
  { DisplayName: string
    Email: string
    CurrentPassword: string
    NewPassword: string
    ConfirmPassword: string }

[<CLIMutable>]
type TeamMemberViewModel =
  { DisplayName: string
    Email: string
    Role: string
    UserId: Guid }

[<CLIMutable>]
type TeamViewModel =
  { TeamName: string
    TeamToken: string
    Members: List<TeamMemberViewModel>
    IsOwner: bool
    CurrentUserId: Guid
    TeamScore: int
    TeamSolvedCount: int }

[<CLIMutable>]
type ProfileUpdateDto =
  { DisplayName: string }

[<CLIMutable>]
type ProfileSettingsDto =
  { DisplayName: string
    Email: string
    CurrentPassword: string
    NewPassword: string
    ConfirmPassword: string }

[<CLIMutable>]
type TeamCreateDto =
  { Name: string
    JoinPassword: string }

[<CLIMutable>]
type TeamJoinDto =
  { Name: string
    Password: string }
