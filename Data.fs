namespace Shinogi.Data

open System
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Storage.ValueConversion
open Microsoft.AspNetCore.Identity
open Microsoft.AspNetCore.Identity.EntityFrameworkCore
open Shinogi.Domain

type CtfdDbContext(options: DbContextOptions<CtfdDbContext>) =
    inherit IdentityDbContext<CtfdUser, IdentityRole<Guid>, Guid>(options)

    [<DefaultValue>] val mutable challenges: DbSet<Challenge>
    [<DefaultValue>] val mutable flags: DbSet<Flag>
    [<DefaultValue>] val mutable submissions: DbSet<Submission>
    [<DefaultValue>] val mutable teams: DbSet<Team>
    [<DefaultValue>] val mutable teamMembers: DbSet<TeamMember>
    [<DefaultValue>] val mutable ctfSettings: DbSet<CtfSettings>
    [<DefaultValue>] val mutable challengeCategories: DbSet<ChallengeCategory>
    [<DefaultValue>] val mutable challengeDifficulties: DbSet<ChallengeDifficulty>
    [<DefaultValue>] val mutable challengeFiles: DbSet<ChallengeFile>
    [<DefaultValue>] val mutable challengeInstances: DbSet<ChallengeInstance>

    member this.Challenges with get() = this.challenges and set v = this.challenges <- v
    member this.Flags with get() = this.flags and set v = this.flags <- v
    member this.Submissions with get() = this.submissions and set v = this.submissions <- v
    member this.Teams with get() = this.teams and set v = this.teams <- v
    member this.TeamMembers with get() = this.teamMembers and set v = this.teamMembers <- v
    member this.CtfSettings with get() = this.ctfSettings and set v = this.ctfSettings <- v
    member this.ChallengeCategories with get() = this.challengeCategories and set v = this.challengeCategories <- v
    member this.ChallengeDifficulties with get() = this.challengeDifficulties and set v = this.challengeDifficulties <- v
    member this.ChallengeFiles with get() = this.challengeFiles and set v = this.challengeFiles <- v
    member this.ChallengeInstances with get() = this.challengeInstances and set v = this.challengeInstances <- v

    override _.OnModelCreating(builder: ModelBuilder) =
        base.OnModelCreating(builder)
        let scoreConverter =
            ValueConverter<ScoreFunction, string>(
                (fun v -> v.ToString()),
                (fun s ->
                    match s.ToLowerInvariant() with
                    | "log" -> ScoreFunction.Log
                    | "exp" -> ScoreFunction.Exp
                    | _ -> ScoreFunction.Linear))
        let logicConverter =
            ValueConverter<ChallengeLogic, string>(
                (fun v -> v.ToString()),
                (fun s ->
                    match s.ToLowerInvariant() with
                    | "all" -> ChallengeLogic.All
                    | "teamconsensus" -> ChallengeLogic.TeamConsensus
                    | _ -> ChallengeLogic.Any))
        let roleConverter =
            ValueConverter<MemberRole, string>(
                (fun v -> v.ToString()),
                (fun s ->
                    match s.ToLowerInvariant() with
                    | "owner" -> MemberRole.Owner
                    | "ai" -> MemberRole.AI
                    | _ -> MemberRole.Player))
        let instanceStatusConverter =
            ValueConverter<InstanceStatus, string>(
                (fun v -> v.ToString()),
                (fun s ->
                    match s.ToLowerInvariant() with
                    | "stopped" -> InstanceStatus.Stopped
                    | "expired" -> InstanceStatus.Expired
                    | _ -> InstanceStatus.Running))
        let maxAttemptsConverter =
            ValueConverter<int option, Nullable<int>>(
                (fun v -> match v with | Some i -> Nullable i | None -> Nullable()),
                (fun n -> if n.HasValue then Some n.Value else None))
        let dtoConverter =
            ValueConverter<DateTimeOffset option, Nullable<DateTimeOffset>>(
                (fun v -> match v with | Some d -> Nullable d | None -> Nullable()),
                (fun n -> if n.HasValue then Some n.Value else None))
        builder.Entity<Challenge>().Property(fun c -> c.Function).HasConversion(scoreConverter) |> ignore
        builder.Entity<Challenge>().Property(fun c -> c.Logic).HasConversion(logicConverter) |> ignore
        builder.Entity<Challenge>().Property(fun c -> c.MaxAttempts).HasConversion(maxAttemptsConverter).IsRequired(false) |> ignore
        builder.Entity<Challenge>().Property(fun c -> c.ReleaseAt).HasConversion(dtoConverter).IsRequired(false) |> ignore
        builder.Entity<Challenge>().Property(fun c -> c.InstancePort).HasConversion(maxAttemptsConverter).IsRequired(false) |> ignore
        builder.Entity<CtfSettings>().Property(fun s -> s.EventStart).HasConversion(dtoConverter).IsRequired(false) |> ignore
        builder.Entity<CtfSettings>().Property(fun s -> s.EventEnd).HasConversion(dtoConverter).IsRequired(false) |> ignore
        builder.Entity<Flag>().HasIndex("ChallengeId", "ContentHash").IsUnique() |> ignore
        builder.Entity<Flag>().HasOne<Challenge>().WithMany().HasForeignKey("ChallengeId") |> ignore
        builder.Entity<Submission>().HasIndex("AccountId") |> ignore
        builder.Entity<Team>().HasIndex("Name").IsUnique() |> ignore
        builder.Entity<Team>().HasIndex("Token").IsUnique() |> ignore
        builder.Entity<TeamMember>().Property(fun m -> m.Role).HasConversion(roleConverter) |> ignore
        builder.Entity<TeamMember>().HasIndex("UserId") |> ignore
        builder.Entity<TeamMember>().HasIndex("TeamId", "UserId").IsUnique() |> ignore
        builder.Entity<ChallengeCategory>().HasIndex("Name").IsUnique() |> ignore
        builder.Entity<ChallengeDifficulty>().HasIndex("Name").IsUnique() |> ignore
        builder.Entity<ChallengeFile>().HasOne<Challenge>().WithMany().HasForeignKey("ChallengeId") |> ignore
        builder.Entity<ChallengeInstance>().Property(fun i -> i.Status).HasConversion(instanceStatusConverter) |> ignore
        builder.Entity<ChallengeInstance>().HasIndex("UserId") |> ignore
        builder.Entity<ChallengeInstance>().HasIndex("ChallengeId") |> ignore
        builder.Entity<ChallengeInstance>().HasIndex("Status") |> ignore
        builder.Entity<ChallengeInstance>().HasOne<Challenge>().WithMany().HasForeignKey("ChallengeId") |> ignore
