namespace Shinogi.Controllers

open System
open System.Linq
open System.Threading.Tasks
open System.Collections.Generic
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Authorization
open Microsoft.EntityFrameworkCore
open Microsoft.AspNetCore.Identity
open Shinogi.Domain
open Shinogi.Data
open Shinogi.ViewModels

[<Authorize>]
type ProfileController(userManager: UserManager<CtfdUser>, db: CtfdDbContext) =
    inherit Controller()

    member this.Index() : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            let! memberRow = db.TeamMembers.FirstOrDefaultAsync(fun m -> m.UserId = user.Id)
            let! teamOpt =
                if isNull memberRow then
                    Task.FromResult<Option<Team>>(None)
                else
                    task {
                        let! t = db.Teams.FirstOrDefaultAsync(fun t -> t.Id = memberRow.TeamId)
                        return if isNull t then None else Some t
                    }
            let teamName =
                match teamOpt with
                | Some t -> t.Name
                | None -> ""
            let model =
                { Email = user.Email
                  DisplayName = user.DisplayName
                  TeamName = teamName
                  IsAdmin = this.User.IsInRole("Admins") }
            return this.View(model) :> IActionResult
    }

    member this.Settings() : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            let model =
                { DisplayName = user.DisplayName
                  Email = user.Email
                  CurrentPassword = ""
                  NewPassword = ""
                  ConfirmPassword = "" } : ProfileSettingsViewModel
            return this.View(model) :> IActionResult
    }

    [<HttpPost>]
    member this.UpdateSettings(dto: ProfileSettingsDto) : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            let mutable errorMsg = ""

            if not (String.IsNullOrWhiteSpace dto.DisplayName) then
                user.DisplayName <- dto.DisplayName.Trim()

            if errorMsg = "" && not (String.IsNullOrWhiteSpace dto.Email) && dto.Email.Trim() <> user.Email then
                let newEmail = dto.Email.Trim()
                let! existing = userManager.FindByEmailAsync(newEmail)
                if not (isNull existing) && existing.Id <> user.Id then
                    errorMsg <- "このメールアドレスは既に使用されています。"
                else
                    user.Email <- newEmail
                    user.UserName <- newEmail
                    user.NormalizedEmail <- newEmail.ToUpperInvariant()
                    user.NormalizedUserName <- newEmail.ToUpperInvariant()

            if errorMsg = "" && not (String.IsNullOrWhiteSpace dto.NewPassword) then
                if String.IsNullOrWhiteSpace dto.CurrentPassword then
                    errorMsg <- "現在のパスワードを入力してください。"
                elif dto.NewPassword <> dto.ConfirmPassword then
                    errorMsg <- "新しいパスワードが一致しません。"
                else
                    let! pwResult = userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword)
                    if not pwResult.Succeeded then
                        let errors = pwResult.Errors |> Seq.map (fun e -> e.Description) |> String.concat "; "
                        errorMsg <- $"パスワード変更失敗: {errors}"

            if errorMsg <> "" then
                this.TempData["Error"] <- errorMsg
                return this.RedirectToAction("Settings") :> IActionResult
            else
                let! _ = userManager.UpdateAsync(user)
                this.TempData["Success"] <- "設定を更新しました。"
                return this.RedirectToAction("Settings") :> IActionResult
    }

    [<HttpPost>]
    member this.UpdateProfile(dto: ProfileUpdateDto) : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            if not (String.IsNullOrWhiteSpace dto.DisplayName) then
                user.DisplayName <- dto.DisplayName.Trim()
                let! _ = userManager.UpdateAsync(user)
                ()
            return this.RedirectToAction("Index") :> IActionResult
    }

    member this.Team() : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            let! memberRow = db.TeamMembers.FirstOrDefaultAsync(fun m -> m.UserId = user.Id)
            if isNull memberRow then
                let model =
                    { TeamName = ""
                      TeamToken = ""
                      Members = List<TeamMemberViewModel>()
                      IsOwner = false
                      CurrentUserId = user.Id }
                return this.View(model) :> IActionResult
            else
                let! team = db.Teams.FirstOrDefaultAsync(fun t -> t.Id = memberRow.TeamId)
                let! members = db.TeamMembers.Where(fun m -> m.TeamId = memberRow.TeamId).ToListAsync()
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
                let isOwner = memberRow.Role = MemberRole.Owner
                let model =
                    { TeamName = if isNull team then "" else team.Name
                      TeamToken = if isNull team then "" else team.Token.ToString()
                      Members = viewMembers
                      IsOwner = isOwner
                      CurrentUserId = user.Id }
                return this.View(model) :> IActionResult
    }

    [<HttpPost>]
    member this.CreateTeam(dto: TeamCreateDto) : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            if String.IsNullOrWhiteSpace dto.Name then
                this.TempData["Error"] <- "チーム名を入力してください。"
                return this.RedirectToAction("Team") :> IActionResult
            else
                let! alreadyInTeam = db.TeamMembers.AnyAsync(fun m -> m.UserId = user.Id)
                if alreadyInTeam then
                    this.TempData["Error"] <- "既にチームに所属しています。"
                    return this.RedirectToAction("Team") :> IActionResult
                else
                    let teamName = dto.Name.Trim()
                    let! exists = db.Teams.AnyAsync(fun t -> t.Name = teamName)
                    if exists then
                        this.TempData["Error"] <- "そのチーム名は既に使われています。"
                        return this.RedirectToAction("Team") :> IActionResult
                    else
                        let joinPw = if isNull dto.JoinPassword then "" else dto.JoinPassword.Trim()
                        let team =
                            { Id = Guid.NewGuid()
                              Name = teamName
                              Token = Guid.NewGuid()
                              JoinPassword = joinPw
                              CreatedAt = DateTimeOffset.UtcNow }
                        let membership =
                            { Id = Guid.NewGuid()
                              TeamId = team.Id
                              UserId = user.Id
                              JoinedAt = DateTimeOffset.UtcNow
                              Role = MemberRole.Owner }
                        db.Teams.Add(team) |> ignore
                        db.TeamMembers.Add(membership) |> ignore
                        let! _ = db.SaveChangesAsync()
                        return this.RedirectToAction("Team") :> IActionResult
    }

    [<HttpPost>]
    member this.JoinTeam(dto: TeamJoinDto) : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            if String.IsNullOrWhiteSpace dto.Name then
                this.TempData["Error"] <- "チーム名を入力してください。"
                return this.RedirectToAction("Team") :> IActionResult
            else
                let! alreadyInTeam = db.TeamMembers.AnyAsync(fun m -> m.UserId = user.Id)
                if alreadyInTeam then
                    this.TempData["Error"] <- "既にチームに所属しています。"
                    return this.RedirectToAction("Team") :> IActionResult
                else
                    let teamName = dto.Name.Trim()
                    let! team = db.Teams.FirstOrDefaultAsync(fun t -> t.Name = teamName)
                    if isNull team then
                        this.TempData["Error"] <- "チームが見つかりません。"
                        return this.RedirectToAction("Team") :> IActionResult
                    else
                        let inputPw = if isNull dto.Password then "" else dto.Password.Trim()
                        let teamPw = if isNull team.JoinPassword then "" else team.JoinPassword
                        if teamPw <> "" && inputPw <> teamPw then
                            this.TempData["Error"] <- "パスワードが正しくありません。"
                            return this.RedirectToAction("Team") :> IActionResult
                        else
                            let membership =
                                { Id = Guid.NewGuid()
                                  TeamId = team.Id
                                  UserId = user.Id
                                  JoinedAt = DateTimeOffset.UtcNow
                                  Role = MemberRole.Player }
                            db.TeamMembers.Add(membership) |> ignore
                            let! _ = db.SaveChangesAsync()
                            return this.RedirectToAction("Team") :> IActionResult
    }

    [<HttpPost>]
    member this.LeaveTeam() : Task<IActionResult> = task {
        let! user = userManager.GetUserAsync(this.User)
        if isNull user then
            return this.RedirectToAction("Login", "Account") :> IActionResult
        else
            let! memberRow = db.TeamMembers.FirstOrDefaultAsync(fun m -> m.UserId = user.Id)
            if isNull memberRow then
                return this.RedirectToAction("Team") :> IActionResult
            else
                let! others =
                    db.TeamMembers
                      .Where(fun m -> m.TeamId = memberRow.TeamId && m.UserId <> user.Id)
                      .OrderBy(fun m -> m.JoinedAt)
                      .ToListAsync()
                if memberRow.Role = MemberRole.Owner && others.Count > 0 then
                    let newOwner = { others.[0] with Role = MemberRole.Owner }
                    db.Entry(others.[0]).State <- EntityState.Detached
                    db.TeamMembers.Update(newOwner) |> ignore
                db.TeamMembers.Remove(memberRow) |> ignore
                if others.Count = 0 then
                    let! team = db.Teams.FirstOrDefaultAsync(fun t -> t.Id = memberRow.TeamId)
                    if not (isNull team) then
                        db.Teams.Remove(team) |> ignore
                let! _ = db.SaveChangesAsync()
                return this.RedirectToAction("Team") :> IActionResult
    }
