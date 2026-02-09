namespace Shinogi.Controllers

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Identity
open System.Linq
open Shinogi.Domain
open Shinogi.Dtos

[<AllowAnonymous>]
type AccountController(userManager: UserManager<CtfdUser>, signInManager: SignInManager<CtfdUser>) =
    inherit Controller()

    member this.Login() = this.View() :> IActionResult

    [<HttpPost>]
    member this.Login(dto: LoginDto) : Task<IActionResult> = task {
        if String.IsNullOrWhiteSpace dto.Email || String.IsNullOrWhiteSpace dto.Password then
            this.TempData["Error"] <- "メールアドレスとパスワードを入力してください。"
            return this.View() :> IActionResult
        else
            let! user = userManager.FindByEmailAsync(dto.Email)
            match user with
            | null ->
                this.TempData["Error"] <- "メールアドレスまたはパスワードが正しくありません。"
                return this.View() :> IActionResult
            | u ->
                let! emailConfirmed = userManager.IsEmailConfirmedAsync(u)
                if not emailConfirmed then
                    this.TempData["Error"] <- "メールアドレスが未確認です。受信トレイの確認メールをご確認ください。"
                    return this.View() :> IActionResult
                else
                    let! res = signInManager.PasswordSignInAsync(u, dto.Password, true, false)
                    if res.Succeeded then
                        this.TempData["Success"] <- "ログインしました。"
                        return this.RedirectToAction("Index", "Home") :> IActionResult
                    else
                        this.TempData["Error"] <- "メールアドレスまたはパスワードが正しくありません。"
                        return this.View() :> IActionResult
    }

    member this.Register() = this.View() :> IActionResult

    [<HttpPost>]
    member this.Register(dto: RegisterDto) : Task<IActionResult> = task {
        if String.IsNullOrWhiteSpace dto.Email || String.IsNullOrWhiteSpace dto.Password then
            this.TempData["Error"] <- "必須項目を入力してください。"
            return this.View() :> IActionResult
        else
            let user = CtfdUser(Email = dto.Email, UserName = dto.Email, DisplayName = dto.DisplayName)
            let! res = userManager.CreateAsync(user, dto.Password)
            if res.Succeeded then
                let! token = userManager.GenerateEmailConfirmationTokenAsync(user)
                let callbackUrl =
                    this.Url.Action("ConfirmEmail", "Account",
                        {| userId = user.Id.ToString(); token = token |},
                        this.Request.Scheme)
                do! Shinogi.Services.EmailService.sendConfirmationEmail user.Email callbackUrl
                this.TempData["Success"] <- "アカウントを作成しました。確認メールを送信しましたので、メール内のリンクをクリックしてアカウントを有効化してください。"
                return this.RedirectToAction("Login") :> IActionResult
            else
                let errors = res.Errors |> Seq.map (fun e -> e.Description) |> String.concat " "
                this.TempData["Error"] <- $"アカウント作成に失敗しました: {errors}"
                return this.View() :> IActionResult
    }

    member this.ConfirmEmail(userId: string, token: string) : Task<IActionResult> = task {
        if String.IsNullOrWhiteSpace userId || String.IsNullOrWhiteSpace token then
            this.TempData["Error"] <- "無効な確認リンクです。"
            return this.RedirectToAction("Login") :> IActionResult
        else
            let! user = userManager.FindByIdAsync(userId)
            if isNull user then
                this.TempData["Error"] <- "ユーザーが見つかりません。"
                return this.RedirectToAction("Login") :> IActionResult
            else
                let! result = userManager.ConfirmEmailAsync(user, token)
                if result.Succeeded then
                    this.TempData["Success"] <- "メールアドレスの確認が完了しました。ログインしてください。"
                else
                    this.TempData["Error"] <- "メール確認に失敗しました。リンクが期限切れの可能性があります。"
                return this.RedirectToAction("Login") :> IActionResult
    }

    [<Authorize>]
    member this.Logout() : Task<IActionResult> = task {
        do! signInManager.SignOutAsync()
        this.TempData["Success"] <- "ログアウトしました。"
        return this.RedirectToAction("Index", "Home") :> IActionResult
    }
