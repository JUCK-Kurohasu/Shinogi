namespace Shinogi.ViewModels

open System

[<CLIMutable>]
type CertificateViewModel =
  { DisplayName: string
    CtfName: string
    Score: int
    Rank: int
    TotalParticipants: int
    SolvedCount: int
    IssuedAt: DateTimeOffset }
