namespace Shinogi.Controllers

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Identity
open Microsoft.EntityFrameworkCore
open Shinogi.Domain
open Shinogi.Data
open Shinogi.ViewModels

[<Authorize>]
type CertificateController(db: CtfdDbContext, userManager: UserManager<CtfdUser>) =
    inherit Controller()

    // 全ユーザーのスコアを集計してランクを返すヘルパー
    let calcRank (allSubmissions: System.Collections.Generic.List<Submission>) (targetUserId: Guid) =
        let scores =
            allSubmissions
            |> Seq.groupBy (fun s -> s.AccountId)
            |> Seq.map (fun (uid, items) -> uid, items |> Seq.sumBy (fun s -> s.ValueAwarded))
            |> Seq.sortByDescending snd
            |> Seq.toArray
        let rank =
            scores
            |> Array.tryFindIndex (fun (uid, _) -> uid = targetUserId)
            |> Option.map (fun i -> i + 1)
            |> Option.defaultValue scores.Length
        rank, scores.Length

    /// 自分の修了証を表示
    member this.Index() : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            let! allCorrect = db.Submissions.Where(fun s -> s.IsCorrect).ToListAsync()
            let userScore =
                allCorrect
                |> Seq.filter (fun s -> s.AccountId = user.Id)
                |> Seq.sumBy (fun s -> s.ValueAwarded)
            let solvedCount =
                allCorrect
                |> Seq.filter (fun s -> s.AccountId = user.Id)
                |> Seq.map (fun s -> s.ChallengeId)
                |> Seq.distinct
                |> Seq.length
            let rank, total = calcRank allCorrect user.Id
            let ctfName = Environment.GetEnvironmentVariable("SHINOGI_CTF_NAME")
            let ctfName = if String.IsNullOrWhiteSpace ctfName then "CTF" else ctfName
            let displayName = if String.IsNullOrWhiteSpace user.DisplayName then user.Email else user.DisplayName
            let model =
                { DisplayName = displayName
                  CtfName = ctfName
                  Score = userScore
                  Rank = rank
                  TotalParticipants = total
                  SolvedCount = solvedCount
                  IssuedAt = DateTimeOffset.UtcNow }
            return this.View(model) :> IActionResult
    }

    /// 管理者が任意ユーザーの修了証を表示
    [<Authorize(Roles = "Admins")>]
    member this.ForUser(id: Guid) : Task<IActionResult> = task {
        let! user = userManager.FindByIdAsync(id.ToString())
        if isNull user then
            return this.NotFound() :> IActionResult
        else
            let! allCorrect = db.Submissions.Where(fun s -> s.IsCorrect).ToListAsync()
            let userScore =
                allCorrect
                |> Seq.filter (fun s -> s.AccountId = user.Id)
                |> Seq.sumBy (fun s -> s.ValueAwarded)
            let solvedCount =
                allCorrect
                |> Seq.filter (fun s -> s.AccountId = user.Id)
                |> Seq.map (fun s -> s.ChallengeId)
                |> Seq.distinct
                |> Seq.length
            let rank, total = calcRank allCorrect user.Id
            let ctfName = Environment.GetEnvironmentVariable("SHINOGI_CTF_NAME")
            let ctfName = if String.IsNullOrWhiteSpace ctfName then "CTF" else ctfName
            let displayName = if String.IsNullOrWhiteSpace user.DisplayName then user.Email else user.DisplayName
            let model =
                { DisplayName = displayName
                  CtfName = ctfName
                  Score = userScore
                  Rank = rank
                  TotalParticipants = total
                  SolvedCount = solvedCount
                  IssuedAt = DateTimeOffset.UtcNow }
            return this.View("Index", model) :> IActionResult
    }
