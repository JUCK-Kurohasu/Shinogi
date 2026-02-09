namespace Shinogi.Dtos
open System

[<CLIMutable>]
type RegisterDto = { Email: string; Password: string; DisplayName: string }

[<CLIMutable>]
type LoginDto    = { Email: string; Password: string }

[<CLIMutable>]
type ChallengeCreateDto =
  { Name: string
    Category: string
    Description: string
    ValueInitial: int
    ValueMinimum: int
    Decay: int
    Function: string
    Logic: string
    MaxAttempts: int option }

[<CLIMutable>]
type FlagCreateDto = { Content: string; CaseSensitive: bool }

[<CLIMutable>]
type SubmitDto     = { Flag: string }
