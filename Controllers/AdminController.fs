namespace Shinogi.Controllers

open System
open System.IO
open System.Linq
open System.Threading.Tasks
open System.Collections.Generic
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Identity
open Microsoft.AspNetCore.Hosting
open Microsoft.EntityFrameworkCore
open Shinogi.Domain
open Shinogi.Data
open Shinogi.ViewModels
open Shinogi.Services

[<Authorize(Roles = "Admins")>]
type AdminController(db: CtfdDbContext, userManager: UserManager<CtfdUser>, env: IWebHostEnvironment) =
    inherit Controller()

    let parseFunction (value: string) =
        match value.Trim().ToLowerInvariant() with
        | "log" -> ScoreFunction.Log
        | "exp" -> ScoreFunction.Exp
        | _ -> ScoreFunction.Linear

    let parseLogic (value: string) =
        match value.Trim().ToLowerInvariant() with
        | "all" -> ChallengeLogic.All
        | "teamconsensus" -> ChallengeLogic.TeamConsensus
        | _ -> ChallengeLogic.Any

    let loadCategoryNames () = task {
        let! cats = db.ChallengeCategories.OrderBy(fun c -> c.SortOrder).ThenBy(fun c -> c.Name).ToListAsync()
        return List<string>(cats |> Seq.map (fun c -> c.Name))
    }
    let loadDifficultyNames () = task {
        let! diffs = db.ChallengeDifficulties.OrderBy(fun d -> d.SortOrder).ThenBy(fun d -> d.Name).ToListAsync()
        return List<string>(diffs |> Seq.map (fun d -> d.Name))
    }

    // ===== Challenges =====

    member this.Challenges() : Task<IActionResult> = task {
        let! items =
            db.Challenges
              .OrderBy(fun c -> c.Category)
              .ThenBy(fun c -> c.Name)
              .ToListAsync()
        let model =
            items
            |> Seq.map (fun c ->
                { Id = c.Id
                  Name = c.Name
                  Category = c.Category
                  Difficulty = c.Difficulty
                  Published = c.Published
                  CreatedAt = c.CreatedAt })
            |> List<ChallengeAdminListItem>
        return this.View(model) :> IActionResult
    }

    member this.NewChallenge() : Task<IActionResult> = task {
        let! cats = loadCategoryNames ()
        let! diffs = loadDifficultyNames ()
        let model =
            { Id = Guid.Empty
              Name = ""
              Category = ""
              Difficulty = ""
              Description = ""
              ValueInitial = 100
              ValueMinimum = 10
              Decay = 5
              Function = "linear"
              Logic = "any"
              MaxAttempts = Nullable()
              Published = false
              ReleaseAt = ""
              Categories = cats
              Difficulties = diffs
              RequiresInstance = false
              InstanceImage = ""
              InstancePort = Nullable()
              InstanceLifetimeMinutes = 30
              InstanceCpuLimit = "0.5"
              InstanceMemoryLimit = "256m" }
        return this.View(model) :> IActionResult
    }

    [<HttpPost>]
    member this.CreateChallenge(dto: ChallengeEditViewModel) : Task<IActionResult> = task {
        if String.IsNullOrWhiteSpace dto.Name || String.IsNullOrWhiteSpace dto.Category then
            this.TempData["Error"] <- "Name and Category are required."
            return this.RedirectToAction("NewChallenge") :> IActionResult
        else
            let releaseAt =
                match DateTimeOffset.TryParse(dto.ReleaseAt) with
                | true, d -> Some d
                | _ -> None
            let difficulty = if isNull dto.Difficulty then "" else dto.Difficulty.Trim()
            let challenge =
                { Id = Guid.NewGuid()
                  Name = dto.Name.Trim()
                  Category = dto.Category.Trim()
                  Difficulty = difficulty
                  Description = dto.Description
                  ValueInitial = dto.ValueInitial
                  ValueMinimum = dto.ValueMinimum
                  Decay = dto.Decay
                  Function = parseFunction dto.Function
                  Logic = parseLogic dto.Logic
                  MaxAttempts = if dto.MaxAttempts.HasValue then Some dto.MaxAttempts.Value else None
                  Published = dto.Published
                  ReleaseAt = releaseAt
                  CreatedAt = DateTimeOffset.UtcNow
                  RequiresInstance = dto.RequiresInstance
                  InstanceImage = if isNull dto.InstanceImage then "" else dto.InstanceImage.Trim()
                  InstancePort = if dto.InstancePort.HasValue then Some dto.InstancePort.Value else None
                  InstanceLifetimeMinutes = dto.InstanceLifetimeMinutes
                  InstanceCpuLimit = if isNull dto.InstanceCpuLimit then "0.5" else dto.InstanceCpuLimit.Trim()
                  InstanceMemoryLimit = if isNull dto.InstanceMemoryLimit then "256m" else dto.InstanceMemoryLimit.Trim() }
            db.Challenges.Add(challenge) |> ignore
            let! _ = db.SaveChangesAsync()
            return this.RedirectToAction("Challenges") :> IActionResult
    }

    member this.EditChallenge(id: Guid) : Task<IActionResult> = task {
        let! challenge = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id)
        if isNull challenge then
            return this.NotFound() :> IActionResult
        else
            let! cats = loadCategoryNames ()
            let! diffs = loadDifficultyNames ()
            let model =
                { Id = challenge.Id
                  Name = challenge.Name
                  Category = challenge.Category
                  Difficulty = challenge.Difficulty
                  Description = challenge.Description
                  ValueInitial = challenge.ValueInitial
                  ValueMinimum = challenge.ValueMinimum
                  Decay = challenge.Decay
                  Function = challenge.Function.ToString()
                  Logic = challenge.Logic.ToString()
                  MaxAttempts = if challenge.MaxAttempts.IsSome then Nullable challenge.MaxAttempts.Value else Nullable()
                  Published = challenge.Published
                  ReleaseAt = match challenge.ReleaseAt with | Some d -> d.ToLocalTime().ToString("yyyy-MM-ddTHH:mm") | None -> ""
                  Categories = cats
                  Difficulties = diffs
                  RequiresInstance = challenge.RequiresInstance
                  InstanceImage = challenge.InstanceImage
                  InstancePort = if challenge.InstancePort.IsSome then Nullable challenge.InstancePort.Value else Nullable()
                  InstanceLifetimeMinutes = challenge.InstanceLifetimeMinutes
                  InstanceCpuLimit = challenge.InstanceCpuLimit
                  InstanceMemoryLimit = challenge.InstanceMemoryLimit }
            return this.View(model) :> IActionResult
    }

    [<HttpPost>]
    member this.UpdateChallenge(id: Guid, dto: ChallengeEditViewModel) : Task<IActionResult> = task {
        let! existing = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id)
        if isNull existing then
            return this.NotFound() :> IActionResult
        else
            let releaseAt =
                match DateTimeOffset.TryParse(dto.ReleaseAt) with
                | true, d -> Some d
                | _ -> None
            let difficulty = if isNull dto.Difficulty then "" else dto.Difficulty.Trim()
            let updated =
                { existing with
                    Name = dto.Name.Trim()
                    Category = dto.Category.Trim()
                    Difficulty = difficulty
                    Description = dto.Description
                    ValueInitial = dto.ValueInitial
                    ValueMinimum = dto.ValueMinimum
                    Decay = dto.Decay
                    Function = parseFunction dto.Function
                    Logic = parseLogic dto.Logic
                    MaxAttempts = if dto.MaxAttempts.HasValue then Some dto.MaxAttempts.Value else None
                    Published = dto.Published
                    ReleaseAt = releaseAt
                    RequiresInstance = dto.RequiresInstance
                    InstanceImage = if isNull dto.InstanceImage then "" else dto.InstanceImage.Trim()
                    InstancePort = if dto.InstancePort.HasValue then Some dto.InstancePort.Value else None
                    InstanceLifetimeMinutes = dto.InstanceLifetimeMinutes
                    InstanceCpuLimit = if isNull dto.InstanceCpuLimit then "0.5" else dto.InstanceCpuLimit.Trim()
                    InstanceMemoryLimit = if isNull dto.InstanceMemoryLimit then "256m" else dto.InstanceMemoryLimit.Trim() }
            db.Entry(existing).State <- EntityState.Detached
            db.Challenges.Update(updated) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- "チャレンジを更新しました。"
            return this.RedirectToAction("Challenges") :> IActionResult
    }

    [<HttpPost>]
    member this.TogglePublish(id: Guid) : Task<IActionResult> = task {
        let! existing = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id)
        if isNull existing then
            return this.NotFound() :> IActionResult
        else
            let updated = { existing with Published = not existing.Published }
            db.Entry(existing).State <- EntityState.Detached
            db.Challenges.Update(updated) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- if updated.Published then "チャレンジを公開しました。" else "チャレンジを非公開にしました。"
            return this.RedirectToAction("Challenges") :> IActionResult
    }

    member this.Flags(id: Guid) : Task<IActionResult> = task {
        let! challenge = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id)
        if isNull challenge then
            return this.NotFound() :> IActionResult
        else
            let! flags = db.Flags.Where(fun f -> f.ChallengeId = id).ToListAsync()
            this.ViewData["ChallengeName"] <- challenge.Name
            this.ViewData["ChallengeId"] <- challenge.Id.ToString()
            return this.View(flags) :> IActionResult
    }

    [<HttpPost>]
    member this.AddFlag(id: Guid, dto: FlagCreateViewModel) : Task<IActionResult> = task {
        if String.IsNullOrWhiteSpace dto.Content then
            this.TempData["Error"] <- "Flag content is required."
            return this.RedirectToAction("Flags", {| id = id |}) :> IActionResult
        else
            let! challenge = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id)
            if isNull challenge then
                return this.NotFound() :> IActionResult
            else
                let content = dto.Content.Trim()
                let hash =
                    if dto.CaseSensitive then
                        Security.sha256 content
                    else
                        Security.sha256 (content.ToLowerInvariant())
                let! duplicate = db.Flags.AnyAsync(fun f -> f.ChallengeId = id && f.ContentHash = hash)
                if duplicate then
                    this.TempData["Error"] <- "同じフラグが既にこのチャレンジに登録されています。"
                    return this.RedirectToAction("Flags", {| id = id |}) :> IActionResult
                else
                    let flag =
                        { Id = Guid.NewGuid()
                          ChallengeId = id
                          Content = content
                          ContentHash = hash
                          CaseSensitive = dto.CaseSensitive }
                    db.Flags.Add(flag) |> ignore
                    let! _ = db.SaveChangesAsync()
                    this.TempData["Success"] <- "フラグを追加しました。"
                    return this.RedirectToAction("Flags", {| id = id |}) :> IActionResult
    }

    [<HttpPost>]
    member this.DeleteFlag(id: Guid, flagId: Guid) : Task<IActionResult> = task {
        let! flag = db.Flags.FirstOrDefaultAsync(fun f -> f.Id = flagId && f.ChallengeId = id)
        if not (isNull flag) then
            db.Flags.Remove(flag) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- "フラグを削除しました。"
        else
            this.TempData["Error"] <- "フラグが見つかりません。"
        return this.RedirectToAction("Flags", {| id = id |}) :> IActionResult
    }

    // ===== Challenge Files =====

    member this.Files(id: Guid) : Task<IActionResult> = task {
        let! challenge = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id)
        if isNull challenge then
            return this.NotFound() :> IActionResult
        else
            let! files = db.ChallengeFiles.Where(fun f -> f.ChallengeId = id).OrderBy(fun f -> f.UploadedAt).ToListAsync()
            this.ViewData["ChallengeName"] <- challenge.Name
            this.ViewData["ChallengeId"] <- challenge.Id.ToString()
            return this.View(files) :> IActionResult
    }

    [<HttpPost>]
    member this.UploadFile(id: Guid, file: IFormFile) : Task<IActionResult> = task {
        if isNull file || file.Length = 0L then
            return this.Json({| success = false; message = "ファイルを選択してください。" |}) :> IActionResult
        else
            let! challenge = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id)
            if isNull challenge then
                return this.Json({| success = false; message = "チャレンジが見つかりません。" |}) :> IActionResult
            else
                let storedName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}"
                let uploadsDir = Path.Combine(env.ContentRootPath, "uploads", id.ToString())
                Directory.CreateDirectory(uploadsDir) |> ignore
                let filePath = Path.Combine(uploadsDir, storedName)
                use stream = File.Create(filePath)
                do! file.CopyToAsync(stream)
                let challengeFile =
                    { Id = Guid.NewGuid()
                      ChallengeId = id
                      OriginalName = file.FileName
                      StoredName = storedName
                      UploadedAt = DateTimeOffset.UtcNow }
                db.ChallengeFiles.Add(challengeFile) |> ignore
                let! _ = db.SaveChangesAsync()
                return this.Json({| success = true; message = $"「{file.FileName}」をアップロードしました。" |}) :> IActionResult
    }

    [<HttpPost>]
    member this.DeleteFile(id: Guid, fileId: Guid) : Task<IActionResult> = task {
        let! file = db.ChallengeFiles.FirstOrDefaultAsync(fun f -> f.Id = fileId && f.ChallengeId = id)
        if isNull file then
            return this.Json({| success = false; message = "ファイルが見つかりません。" |}) :> IActionResult
        else
            let filePath = Path.Combine(env.ContentRootPath, "uploads", id.ToString(), file.StoredName)
            if File.Exists(filePath) then
                File.Delete(filePath)
            db.ChallengeFiles.Remove(file) |> ignore
            let! _ = db.SaveChangesAsync()
            return this.Json({| success = true; message = $"「{file.OriginalName}」を削除しました。" |}) :> IActionResult
    }

    // ===== Users Management =====

    member this.Users() : Task<IActionResult> = task {
        let! allUsers = userManager.Users.ToListAsync()
        let! allMembers = db.TeamMembers.ToListAsync()
        let! allTeams = db.Teams.ToListAsync()
        let teamById = allTeams |> Seq.map (fun t -> t.Id, t) |> dict
        let memberByUserId = allMembers |> Seq.map (fun m -> m.UserId, m) |> dict
        let mutable result = List<UserAdminViewModel>()
        for u in allUsers do
            let! roles = userManager.GetRolesAsync(u)
            let rolesStr = String.Join(", ", roles)
            let teamName =
                match memberByUserId.TryGetValue(u.Id) with
                | true, m ->
                    match teamById.TryGetValue(m.TeamId) with
                    | true, t -> t.Name
                    | _ -> ""
                | _ -> ""
            result.Add(
                { Id = u.Id
                  Email = u.Email
                  DisplayName = u.DisplayName
                  Roles = rolesStr
                  TeamName = teamName })
        return this.View(result) :> IActionResult
    }

    member this.EditUser(id: Guid) : Task<IActionResult> = task {
        let! user = userManager.FindByIdAsync(id.ToString())
        if isNull user then
            return this.NotFound() :> IActionResult
        else
            let! roles = userManager.GetRolesAsync(user)
            let model =
                { Id = user.Id
                  DisplayName = user.DisplayName
                  Email = user.Email
                  Role = if roles.Count > 0 then roles.[0] else "Player"
                  NewPassword = "" } : UserEditViewModel
            return this.View(model) :> IActionResult
    }

    [<HttpPost>]
    member this.UpdateUser(id: Guid, dto: UserEditViewModel) : Task<IActionResult> = task {
        let! user = userManager.FindByIdAsync(id.ToString())
        if isNull user then
            return this.NotFound() :> IActionResult
        else
            if not (String.IsNullOrWhiteSpace dto.DisplayName) then
                user.DisplayName <- dto.DisplayName.Trim()
            if not (String.IsNullOrWhiteSpace dto.Email) then
                user.Email <- dto.Email.Trim()
                user.UserName <- dto.Email.Trim()
            let! _ = userManager.UpdateAsync(user)

            let! currentRoles = userManager.GetRolesAsync(user)
            if currentRoles.Count > 0 then
                let! _ = userManager.RemoveFromRolesAsync(user, currentRoles)
                ()
            if not (String.IsNullOrWhiteSpace dto.Role) then
                let! _ = userManager.AddToRoleAsync(user, dto.Role.Trim())
                ()

            if not (String.IsNullOrWhiteSpace dto.NewPassword) then
                let! token = userManager.GeneratePasswordResetTokenAsync(user)
                let! _ = userManager.ResetPasswordAsync(user, token, dto.NewPassword)
                ()

            this.TempData["Success"] <- "ユーザー情報を更新しました。"
            return this.RedirectToAction("Users") :> IActionResult
    }

    [<HttpPost>]
    member this.DeleteUser(id: Guid) : Task<IActionResult> = task {
        let! user = userManager.FindByIdAsync(id.ToString())
        if not (isNull user) then
            let! memberRow = db.TeamMembers.FirstOrDefaultAsync(fun m -> m.UserId = user.Id)
            if not (isNull memberRow) then
                db.TeamMembers.Remove(memberRow) |> ignore
            let! _ = userManager.DeleteAsync(user)
            ()
        return this.RedirectToAction("Users") :> IActionResult
    }

    // ===== Teams Management =====

    member this.Teams() : Task<IActionResult> = task {
        let! allTeams = db.Teams.OrderBy(fun t -> t.Name).ToListAsync()
        let! allMembers = db.TeamMembers.ToListAsync()
        let memberCounts = allMembers |> Seq.groupBy (fun m -> m.TeamId) |> Seq.map (fun (tid, ms) -> tid, Seq.length ms) |> dict
        let model =
            allTeams
            |> Seq.map (fun t ->
                let count = match memberCounts.TryGetValue(t.Id) with | true, c -> c | _ -> 0
                { Id = t.Id
                  Name = t.Name
                  Token = t.Token
                  MemberCount = count
                  CreatedAt = t.CreatedAt } : TeamAdminViewModel)
            |> List<TeamAdminViewModel>
        return this.View(model) :> IActionResult
    }

    [<HttpPost>]
    member this.CreateTeamAdmin(name: string) : Task<IActionResult> = task {
        if String.IsNullOrWhiteSpace name then
            this.TempData["Error"] <- "チーム名を入力してください。"
            return this.RedirectToAction("Teams") :> IActionResult
        else
            let teamName = name.Trim()
            let! exists = db.Teams.AnyAsync(fun t -> t.Name = teamName)
            if exists then
                this.TempData["Error"] <- "そのチーム名は既に存在します。"
                return this.RedirectToAction("Teams") :> IActionResult
            else
                let team =
                    { Id = Guid.NewGuid()
                      Name = teamName
                      Token = Guid.NewGuid()
                      JoinPassword = ""
                      CreatedAt = DateTimeOffset.UtcNow }
                db.Teams.Add(team) |> ignore
                let! _ = db.SaveChangesAsync()
                this.TempData["Success"] <- $"チーム「{teamName}」を作成しました。"
                return this.RedirectToAction("Teams") :> IActionResult
    }

    [<HttpPost>]
    member this.DeleteTeam(id: Guid) : Task<IActionResult> = task {
        let! team = db.Teams.FirstOrDefaultAsync(fun t -> t.Id = id)
        if not (isNull team) then
            let! members = db.TeamMembers.Where(fun m -> m.TeamId = id).ToListAsync()
            db.TeamMembers.RemoveRange(members)
            db.Teams.Remove(team) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- $"チーム「{team.Name}」を削除しました。"
        return this.RedirectToAction("Teams") :> IActionResult
    }

    [<HttpPost>]
    member this.RegenerateTeamToken(id: Guid) : Task<IActionResult> = task {
        let! team = db.Teams.FirstOrDefaultAsync(fun t -> t.Id = id)
        if not (isNull team) then
            let updated = { team with Token = Guid.NewGuid() }
            db.Entry(team).State <- EntityState.Detached
            db.Teams.Update(updated) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- "チームトークンを再生成しました。"
        return this.RedirectToAction("Teams") :> IActionResult
    }

    member this.EditTeam(id: Guid) : Task<IActionResult> = task {
        let! team = db.Teams.FirstOrDefaultAsync(fun t -> t.Id = id)
        if isNull team then
            return this.NotFound() :> IActionResult
        else
            let! members = db.TeamMembers.Where(fun m -> m.TeamId = id).ToListAsync()
            let userIds = members |> Seq.map (fun m -> m.UserId) |> Seq.toArray
            let! users = userManager.Users.Where(fun u -> userIds.Contains u.Id).ToListAsync()
            let userById = users |> Seq.map (fun u -> u.Id, u) |> dict
            let viewMembers =
                members
                |> Seq.choose (fun m ->
                    match userById.TryGetValue(m.UserId) with
                    | true, u ->
                        Some { DisplayName = if String.IsNullOrWhiteSpace u.DisplayName then u.Email else u.DisplayName
                               Email = u.Email
                               Role = m.Role.ToString()
                               UserId = m.UserId }
                    | _ -> None)
                |> List<TeamMemberViewModel>
            this.ViewData["Team"] <- team
            this.ViewData["TeamToken"] <- team.Token.ToString()
            this.ViewData["JoinPassword"] <- (if isNull team.JoinPassword then "" else team.JoinPassword)
            return this.View(viewMembers) :> IActionResult
    }

    [<HttpPost>]
    member this.UpdateTeamJoinPassword(id: Guid, joinPassword: string) : Task<IActionResult> = task {
        let! team = db.Teams.FirstOrDefaultAsync(fun t -> t.Id = id)
        if not (isNull team) then
            let pw = if isNull joinPassword then "" else joinPassword.Trim()
            let updated = { team with JoinPassword = pw }
            db.Entry(team).State <- EntityState.Detached
            db.Teams.Update(updated) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- "参加パスワードを更新しました。"
        return this.RedirectToAction("EditTeam", {| id = id |}) :> IActionResult
    }

    [<HttpPost>]
    member this.AddTeamMember(dto: TeamMemberAddViewModel) : Task<IActionResult> = task {
        let! team = db.Teams.FirstOrDefaultAsync(fun t -> t.Id = dto.TeamId)
        if isNull team then
            return this.NotFound() :> IActionResult
        else
            if String.IsNullOrWhiteSpace dto.Email then
                this.TempData["Error"] <- "メールアドレスを入力してください。"
                return this.RedirectToAction("EditTeam", {| id = dto.TeamId |}) :> IActionResult
            else
                let! user = userManager.FindByEmailAsync(dto.Email.Trim())
                if isNull user then
                    this.TempData["Error"] <- "ユーザーが見つかりません。"
                    return this.RedirectToAction("EditTeam", {| id = dto.TeamId |}) :> IActionResult
                else
                    let! alreadyInTeam = db.TeamMembers.AnyAsync(fun m -> m.UserId = user.Id)
                    if alreadyInTeam then
                        this.TempData["Error"] <- "このユーザーは既にチームに所属しています。"
                        return this.RedirectToAction("EditTeam", {| id = dto.TeamId |}) :> IActionResult
                    else
                        let role =
                            match dto.Role.ToLowerInvariant() with
                            | "owner" -> MemberRole.Owner
                            | "ai" -> MemberRole.AI
                            | _ -> MemberRole.Player
                        let membership =
                            { Id = Guid.NewGuid()
                              TeamId = dto.TeamId
                              UserId = user.Id
                              JoinedAt = DateTimeOffset.UtcNow
                              Role = role }
                        db.TeamMembers.Add(membership) |> ignore
                        if role = MemberRole.AI then
                            let! hasAiRole = userManager.IsInRoleAsync(user, "AI")
                            if not hasAiRole then
                                let! _ = userManager.AddToRoleAsync(user, "AI")
                                ()
                        let! _ = db.SaveChangesAsync()
                        this.TempData["Success"] <- $"{user.Email} をチームに追加しました。"
                        return this.RedirectToAction("EditTeam", {| id = dto.TeamId |}) :> IActionResult
    }

    [<HttpPost>]
    member this.RemoveTeamMember(teamId: Guid, userId: Guid) : Task<IActionResult> = task {
        let! memberRow = db.TeamMembers.FirstOrDefaultAsync(fun m -> m.TeamId = teamId && m.UserId = userId)
        if not (isNull memberRow) then
            db.TeamMembers.Remove(memberRow) |> ignore
            let! _ = db.SaveChangesAsync()
            ()
        return this.RedirectToAction("EditTeam", {| id = teamId |}) :> IActionResult
    }

    [<HttpPost>]
    member this.ChangeTeamMemberRole(teamId: Guid, userId: Guid, role: string) : Task<IActionResult> = task {
        let! memberRow = db.TeamMembers.FirstOrDefaultAsync(fun m -> m.TeamId = teamId && m.UserId = userId)
        if not (isNull memberRow) then
            let newRole =
                match role.ToLowerInvariant() with
                | "owner" -> MemberRole.Owner
                | "ai" -> MemberRole.AI
                | _ -> MemberRole.Player
            let updated = { memberRow with Role = newRole }
            db.Entry(memberRow).State <- EntityState.Detached
            db.TeamMembers.Update(updated) |> ignore
            let! _ = db.SaveChangesAsync()
            ()
        return this.RedirectToAction("EditTeam", {| id = teamId |}) :> IActionResult
    }

    // ===== CTF Settings =====

    member this.Settings() : Task<IActionResult> = task {
        let! settings = db.CtfSettings.FirstOrDefaultAsync()
        let vm =
            if isNull settings then
                { EventStart = ""; EventEnd = ""; ThemePreset = "purple-network" } : CtfSettingsViewModel
            else
                { EventStart = match settings.EventStart with | Some d -> d.ToLocalTime().ToString("yyyy-MM-ddTHH:mm") | None -> ""
                  EventEnd = match settings.EventEnd with | Some d -> d.ToLocalTime().ToString("yyyy-MM-ddTHH:mm") | None -> ""
                  ThemePreset = if String.IsNullOrWhiteSpace settings.ThemePreset then "purple-network" else settings.ThemePreset }
        return this.View(vm) :> IActionResult
    }

    [<HttpPost>]
    member this.UpdateSettings(dto: CtfSettingsViewModel) : Task<IActionResult> = task {
        let! existing = db.CtfSettings.FirstOrDefaultAsync()
        let eventStart =
            match DateTimeOffset.TryParse(dto.EventStart) with
            | true, d -> Some d
            | _ -> None
        let eventEnd =
            match DateTimeOffset.TryParse(dto.EventEnd) with
            | true, d -> Some d
            | _ -> None
        let themePreset = if String.IsNullOrWhiteSpace dto.ThemePreset then "purple-network" else dto.ThemePreset.Trim()
        if isNull existing then
            let newSettings =
                { Id = Guid.NewGuid()
                  EventStart = eventStart
                  EventEnd = eventEnd
                  ThemePreset = themePreset }
            db.CtfSettings.Add(newSettings) |> ignore
        else
            let updated = { existing with EventStart = eventStart; EventEnd = eventEnd; ThemePreset = themePreset }
            db.Entry(existing).State <- EntityState.Detached
            db.CtfSettings.Update(updated) |> ignore
        let! _ = db.SaveChangesAsync()
        this.TempData["Success"] <- "CTF設定を更新しました。"
        return this.RedirectToAction("Settings") :> IActionResult
    }

    // ===== Categories & Difficulties =====

    member this.Categories() : Task<IActionResult> = task {
        let! items = db.ChallengeCategories.OrderBy(fun c -> c.SortOrder).ThenBy(fun c -> c.Name).ToListAsync()
        let model = items |> Seq.map (fun c -> { Id = c.Id; Name = c.Name; SortOrder = c.SortOrder } : MasterItemViewModel) |> List<MasterItemViewModel>
        this.ViewData["MasterType"] <- "カテゴリ"
        this.ViewData["MasterAction"] <- "Categories"
        return this.View("MasterList", model) :> IActionResult
    }

    [<HttpPost>]
    member this.AddCategory(name: string, sortOrder: int) : Task<IActionResult> = task {
        if not (String.IsNullOrWhiteSpace name) then
            let trimmed = name.Trim()
            let! exists = db.ChallengeCategories.AnyAsync(fun c -> c.Name = trimmed)
            if exists then
                this.TempData["Error"] <- "同名のカテゴリが既に存在します。"
            else
                let item : ChallengeCategory = { Id = Guid.NewGuid(); Name = trimmed; SortOrder = sortOrder }
                db.ChallengeCategories.Add(item) |> ignore
                let! _ = db.SaveChangesAsync()
                this.TempData["Success"] <- $"カテゴリ「{trimmed}」を追加しました。"
        return this.RedirectToAction("Categories") :> IActionResult
    }

    [<HttpPost>]
    member this.UpdateCategory(id: Guid, name: string, sortOrder: int) : Task<IActionResult> = task {
        let! item = db.ChallengeCategories.FirstOrDefaultAsync(fun c -> c.Id = id)
        if not (isNull item) && not (String.IsNullOrWhiteSpace name) then
            let updated = { item with Name = name.Trim(); SortOrder = sortOrder }
            db.Entry(item).State <- EntityState.Detached
            db.ChallengeCategories.Update(updated) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- "カテゴリを更新しました。"
        return this.RedirectToAction("Categories") :> IActionResult
    }

    [<HttpPost>]
    member this.DeleteCategory(id: Guid) : Task<IActionResult> = task {
        let! item = db.ChallengeCategories.FirstOrDefaultAsync(fun c -> c.Id = id)
        if not (isNull item) then
            db.ChallengeCategories.Remove(item) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- $"カテゴリ「{item.Name}」を削除しました。"
        return this.RedirectToAction("Categories") :> IActionResult
    }

    member this.Difficulties() : Task<IActionResult> = task {
        let! items = db.ChallengeDifficulties.OrderBy(fun d -> d.SortOrder).ThenBy(fun d -> d.Name).ToListAsync()
        let model = items |> Seq.map (fun d -> { Id = d.Id; Name = d.Name; SortOrder = d.SortOrder } : MasterItemViewModel) |> List<MasterItemViewModel>
        this.ViewData["MasterType"] <- "難易度"
        this.ViewData["MasterAction"] <- "Difficulties"
        return this.View("MasterList", model) :> IActionResult
    }

    [<HttpPost>]
    member this.AddDifficulty(name: string, sortOrder: int) : Task<IActionResult> = task {
        if not (String.IsNullOrWhiteSpace name) then
            let trimmed = name.Trim()
            let! exists = db.ChallengeDifficulties.AnyAsync(fun d -> d.Name = trimmed)
            if exists then
                this.TempData["Error"] <- "同名の難易度が既に存在します。"
            else
                let item : ChallengeDifficulty = { Id = Guid.NewGuid(); Name = trimmed; SortOrder = sortOrder }
                db.ChallengeDifficulties.Add(item) |> ignore
                let! _ = db.SaveChangesAsync()
                this.TempData["Success"] <- $"難易度「{trimmed}」を追加しました。"
        return this.RedirectToAction("Difficulties") :> IActionResult
    }

    [<HttpPost>]
    member this.UpdateDifficulty(id: Guid, name: string, sortOrder: int) : Task<IActionResult> = task {
        let! item = db.ChallengeDifficulties.FirstOrDefaultAsync(fun d -> d.Id = id)
        if not (isNull item) && not (String.IsNullOrWhiteSpace name) then
            let updated = { item with Name = name.Trim(); SortOrder = sortOrder }
            db.Entry(item).State <- EntityState.Detached
            db.ChallengeDifficulties.Update(updated) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- "難易度を更新しました。"
        return this.RedirectToAction("Difficulties") :> IActionResult
    }

    [<HttpPost>]
    member this.DeleteDifficulty(id: Guid) : Task<IActionResult> = task {
        let! item = db.ChallengeDifficulties.FirstOrDefaultAsync(fun d -> d.Id = id)
        if not (isNull item) then
            db.ChallengeDifficulties.Remove(item) |> ignore
            let! _ = db.SaveChangesAsync()
            this.TempData["Success"] <- $"難易度「{item.Name}」を削除しました。"
        return this.RedirectToAction("Difficulties") :> IActionResult
    }

    // ===== データリセット =====

    [<HttpPost>]
    member this.ResetData(resetChallenges: bool, resetUsers: bool, resetTeams: bool, reseed: bool) : Task<IActionResult> = task {
        let adminUser = this.User
        let! currentUser = userManager.GetUserAsync(adminUser)
        let currentUserId = if isNull currentUser then Guid.Empty else currentUser.Id

        // 提出データは常にリセット
        db.Submissions.RemoveRange(db.Submissions) |> ignore

        if resetChallenges then
            db.Flags.RemoveRange(db.Flags) |> ignore
            db.ChallengeFiles.RemoveRange(db.ChallengeFiles) |> ignore
            db.Challenges.RemoveRange(db.Challenges) |> ignore

        if resetTeams then
            db.TeamMembers.RemoveRange(db.TeamMembers) |> ignore
            db.Teams.RemoveRange(db.Teams) |> ignore

        if resetUsers then
            // Admin以外のユーザーを削除
            let! nonAdminUsers =
                db.Users.Where(fun u -> u.Id <> currentUserId).ToListAsync()
            for u in nonAdminUsers do
                let! _ = userManager.DeleteAsync(u :?> CtfdUser)
                ()

        let! _ = db.SaveChangesAsync()

        let mutable messages = System.Collections.Generic.List<string>()
        messages.Add("提出データをリセットしました")
        if resetChallenges then messages.Add("チャレンジ")
        if resetTeams then messages.Add("チーム")
        if resetUsers then messages.Add("ユーザー（Admin以外）")

        if reseed then
            // シーダーを再実行するためフラグをセット
            this.TempData["Success"] <- (String.Join("、", messages) + " をリセットしました。シーダーを再実行するにはアプリケーションを再起動してください。")
        else
            this.TempData["Success"] <- (String.Join("、", messages) + " をリセットしました。")

        return this.RedirectToAction("Settings") :> IActionResult
    }
