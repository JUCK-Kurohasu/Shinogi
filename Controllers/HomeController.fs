namespace Shinogi.Controllers

open System
open System.Threading.Tasks
open System.Linq
open System.Collections.Generic
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Identity
open Microsoft.EntityFrameworkCore
open Shinogi.Domain
open Shinogi.Data
open Shinogi.ViewModels

[<AllowAnonymous>]
type HomeController(db: CtfdDbContext, userManager: UserManager<CtfdUser>) =
    inherit Controller()

    member this.Index() : IActionResult =
        this.View() :> IActionResult

    member this.About() : IActionResult =
        this.View() :> IActionResult

    member this.Endpoint() : IActionResult =
        this.View() :> IActionResult

    member this.Scoreboard() : Task<IActionResult> = task {
        let! submissions =
            db.Submissions
              .Where(fun s -> s.IsCorrect)
              .ToListAsync()
        let accountIds =
            submissions
            |> Seq.map (fun s -> s.AccountId)
            |> Seq.distinct
            |> Seq.toArray
        let! users =
            if accountIds.Length = 0 then
                Task.FromResult(List<CtfdUser>())
            else
                userManager.Users.Where(fun u -> accountIds.Contains u.Id).ToListAsync()
        let userById = users |> Seq.map (fun u -> u.Id, u) |> dict
        let! allMembers = db.TeamMembers.ToListAsync()
        let! allTeams = db.Teams.ToListAsync()
        let teamById = allTeams |> Seq.map (fun t -> t.Id, t) |> dict
        let memberByUserId = allMembers |> Seq.map (fun m -> m.UserId, m) |> dict

        let resolveTeamName (accountId: Guid) =
            match memberByUserId.TryGetValue(accountId) with
            | true, m ->
                match teamById.TryGetValue(m.TeamId) with
                | true, t -> t.Name
                | _ -> ""
            | _ -> ""

        let topScores =
            List<ScoreEntry>(
                submissions
                |> Seq.groupBy (fun s -> s.AccountId)
                |> Seq.map (fun (accountId, items) ->
                    let displayName =
                        match userById.TryGetValue(accountId) with
                        | true, u -> if String.IsNullOrWhiteSpace u.DisplayName then u.Email else u.DisplayName
                        | _ -> accountId.ToString()
                    { AccountId = accountId
                      DisplayName = displayName
                      TeamName = resolveTeamName accountId
                      Score = items |> Seq.sumBy (fun s -> s.ValueAwarded) })
                |> Seq.sortByDescending (fun e -> e.Score)
                |> Seq.truncate 20)

        // チームごとの累積スコア時系列を構築（上位5チーム）
        let teamTimelines = Dictionary<string, List<TimelinePoint>>()
        let teamSubmissions =
            submissions
            |> Seq.filter (fun s -> resolveTeamName s.AccountId <> "")
            |> Seq.groupBy (fun s -> resolveTeamName s.AccountId)
        // チームごとの合計スコアを算出し、上位5チームを選出
        let top5TeamNames =
            teamSubmissions
            |> Seq.map (fun (name, subs) -> name, subs |> Seq.sumBy (fun s -> s.ValueAwarded))
            |> Seq.sortByDescending snd
            |> Seq.truncate 5
            |> Seq.map fst
            |> Set.ofSeq
        for (teamName, subs) in teamSubmissions do
            if top5TeamNames.Contains teamName then
                let sorted = subs |> Seq.sortBy (fun s -> s.SubmittedAt) |> Seq.toList
                let mutable cumulative = 0
                let points = List<TimelinePoint>()
                for s in sorted do
                    cumulative <- cumulative + s.ValueAwarded
                    points.Add({ Time = s.SubmittedAt; Score = cumulative })
                teamTimelines.[teamName] <- points

        let vm =
            { Entries = topScores
              TeamTimelines = teamTimelines }
        return this.View(vm) :> IActionResult
    }
