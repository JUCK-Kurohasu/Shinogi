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
open Shinogi.Services
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

    member this.Notifications() : Task<IActionResult> = task {
        let! items = db.Notifications.OrderByDescending(fun n -> n.CreatedAt).ToListAsync()
        return this.View(items) :> IActionResult
    }

    member this.Scoreboard() : Task<IActionResult> = task {
        // スコアボード凍結: 凍結時刻以降の提出・ヒント開放は公開スコアボードに反映しない
        let! freezeAt = CtfTime.getFreezeAt db
        let! allSubmissions =
            db.Submissions
              .Where(fun s -> s.IsCorrect)
              .ToListAsync()
        let! allUnlocks = db.HintUnlocks.ToListAsync()
        let submissions =
            match freezeAt with
            | Some f -> allSubmissions |> Seq.filter (fun s -> s.SubmittedAt < f) |> Seq.toList
            | None -> allSubmissions |> Seq.toList
        let unlocks =
            match freezeAt with
            | Some f -> allUnlocks |> Seq.filter (fun u -> u.UnlockedAt < f) |> Seq.toList
            | None -> allUnlocks |> Seq.toList

        // ヒントコストを減算した純スコア
        let netScores = Scoring.netScoresByAccount submissions unlocks
        let accountIds = netScores |> Map.toSeq |> Seq.map fst |> Seq.toArray
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
                netScores
                |> Map.toSeq
                |> Seq.choose (fun (accountId, score) ->
                    match userById.TryGetValue(accountId) with
                    | false, _ -> None  // 削除済みユーザーは除外
                    | true, u ->
                        let displayName = if String.IsNullOrWhiteSpace u.DisplayName then u.Email else u.DisplayName
                        Some { AccountId = accountId
                               DisplayName = displayName
                               TeamName = resolveTeamName accountId
                               Score = score })
                |> Seq.sortByDescending (fun e -> e.Score)
                |> Seq.truncate 20)

        // チームごとの累積スコア時系列を構築（上位5チーム）。提出は加算、ヒント開放は減算イベントとして扱う。
        let teamTimelines = Dictionary<string, List<TimelinePoint>>()
        let scoreEvents =
            Seq.append
                (submissions |> Seq.map (fun s -> s.AccountId, s.SubmittedAt, s.ValueAwarded))
                (unlocks |> Seq.filter (fun u -> u.Cost > 0) |> Seq.map (fun u -> u.AccountId, u.UnlockedAt, -u.Cost))
        let teamEvents =
            scoreEvents
            |> Seq.filter (fun (accountId, _, _) -> resolveTeamName accountId <> "")
            |> Seq.groupBy (fun (accountId, _, _) -> resolveTeamName accountId)
            |> Seq.toList
        // チームごとの合計スコアを算出し、上位5チームを選出
        let top5TeamNames =
            teamEvents
            |> Seq.map (fun (name, evts) -> name, evts |> Seq.sumBy (fun (_, _, delta) -> delta))
            |> Seq.sortByDescending snd
            |> Seq.truncate 5
            |> Seq.map fst
            |> Set.ofSeq
        for (teamName, evts) in teamEvents do
            if top5TeamNames.Contains teamName then
                let sorted = evts |> Seq.sortBy (fun (_, time, _) -> time) |> Seq.toList
                let mutable cumulative = 0
                let points = List<TimelinePoint>()
                for (_, time, delta) in sorted do
                    cumulative <- cumulative + delta
                    points.Add({ Time = time; Score = cumulative })
                teamTimelines.[teamName] <- points

        if freezeAt.IsSome then
            this.ViewData["FrozenAt"] <- freezeAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")

        let vm =
            { Entries = topScores
              TeamTimelines = teamTimelines }
        return this.View(vm) :> IActionResult
    }
