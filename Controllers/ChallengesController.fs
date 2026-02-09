namespace Shinogi.Controllers

open System
open System.IO
open System.Threading.Tasks
open System.Collections.Generic
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Identity
open Microsoft.AspNetCore.Hosting
open Microsoft.EntityFrameworkCore
open System.Linq
open Shinogi.Data
open Shinogi.Domain
open Shinogi.Services
open Shinogi.ViewModels

[<AllowAnonymous>]
type ChallengesController(db: CtfdDbContext, userManager: UserManager<CtfdUser>, env: IWebHostEnvironment) =
    inherit Controller()

    member this.Index() : Task<IActionResult> = task {
        let now = DateTimeOffset.UtcNow
        let! allPublished =
            db.Challenges
              .Where(fun c -> c.Published)
              .OrderBy(fun c -> c.Category)
              .ThenBy(fun c -> c.Name)
              .ToListAsync()
        let challenges =
            allPublished
            |> Seq.filter (fun c ->
                match c.ReleaseAt with
                | Some releaseAt -> releaseAt <= now
                | None -> true)
            |> System.Collections.Generic.List
        let challengeIds = challenges |> Seq.map (fun c -> c.Id) |> Seq.toArray
        let filesByChallenge = Dictionary<Guid, List<ChallengeFile>>()
        if challengeIds.Length > 0 then
            let! allFiles =
                db.ChallengeFiles
                  .Where(fun f -> challengeIds.Contains(f.ChallengeId))
                  .OrderBy(fun f -> f.UploadedAt)
                  .ToListAsync()
            for (cid, fs) in allFiles |> Seq.groupBy (fun f -> f.ChallengeId) do
                filesByChallenge.[cid] <- List<ChallengeFile>(fs)
        this.ViewData["FilesByChallenge"] <- filesByChallenge
        return this.View(challenges) :> IActionResult
    }

    [<HttpPost>]
    [<Authorize>]
    member this.Submit(id: Guid, flag: string) : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            let! challenge = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id && c.Published)
            if isNull challenge then
                this.TempData["Error"] <- "チャレンジが見つかりません。"
                return this.RedirectToAction("Index") :> IActionResult
            else
                let! alreadySolved = db.Submissions.AnyAsync(fun s -> s.AccountId = user.Id && s.ChallengeId = id && s.IsCorrect)
                if alreadySolved then
                    this.TempData["Info"] <- "既に正解済みです。"
                    return this.RedirectToAction("Index") :> IActionResult
                else
                    let! attempts = db.Submissions.CountAsync(fun s -> s.AccountId = user.Id && s.ChallengeId = id)
                    match challenge.MaxAttempts with
                    | Some max when attempts >= max ->
                        this.TempData["Error"] <- "最大試行回数に達しました。"
                        return this.RedirectToAction("Index") :> IActionResult
                    | _ ->
                        let normalized = if isNull flag then "" else flag.Trim()
                        let hashExact = Security.sha256 normalized
                        let hashLower = Security.sha256 (normalized.ToLowerInvariant())
                        let! correct = db.Flags.AnyAsync(fun f -> f.ChallengeId = id && ((f.CaseSensitive && f.ContentHash = hashExact) || ((not f.CaseSensitive) && f.ContentHash = hashLower)))
                        let! solveCount = db.Submissions.CountAsync(fun s -> s.ChallengeId = id && s.IsCorrect)
                        let awarded = if correct then Scoring.dynamicValue challenge solveCount else 0
                        let ip =
                            match this.HttpContext.Connection.RemoteIpAddress with
                            | null -> "unknown"
                            | addr -> addr.ToString()
                        let submission =
                            { Id = Guid.NewGuid()
                              AccountId = user.Id
                              ChallengeId = id
                              SubmittedAt = DateTimeOffset.UtcNow
                              IsCorrect = correct
                              ValueAwarded = awarded
                              Ip = ip }
                        db.Submissions.Add(submission) |> ignore
                        let! _ = db.SaveChangesAsync()
                        if correct then
                            this.TempData["Success"] <- $"正解！ {awarded} ポイント獲得！"
                        else
                            this.TempData["Error"] <- "不正解です。"
                        return this.RedirectToAction("Index") :> IActionResult
    }

    member this.DownloadFile(id: Guid, fileId: Guid) : Task<IActionResult> = task {
        let! file = db.ChallengeFiles.FirstOrDefaultAsync(fun f -> f.Id = fileId && f.ChallengeId = id)
        if isNull file then
            return this.NotFound() :> IActionResult
        else
            let filePath = Path.Combine(env.ContentRootPath, "uploads", id.ToString(), file.StoredName)
            if not (File.Exists filePath) then
                return this.NotFound() :> IActionResult
            else
                let bytes = File.ReadAllBytes(filePath)
                return this.File(bytes, "application/octet-stream", file.OriginalName) :> IActionResult
    }
