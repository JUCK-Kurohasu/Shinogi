module Shinogi.Services.EmailService

open System
open System.Net
open System.Net.Mail
open System.Threading.Tasks

let sendConfirmationEmail (toEmail: string) (callbackUrl: string) : Task =
    task {
        let smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST")
        let smtpPortStr = Environment.GetEnvironmentVariable("SMTP_PORT")
        let smtpUser = Environment.GetEnvironmentVariable("SMTP_USER")
        let smtpPass = Environment.GetEnvironmentVariable("SMTP_PASS")
        let smtpFrom = Environment.GetEnvironmentVariable("SMTP_FROM")

        if String.IsNullOrWhiteSpace smtpHost then
            // SMTP未設定の場合はコンソールに出力（開発用フォールバック）
            Console.WriteLine("========================================")
            Console.WriteLine("[Email Confirmation - SMTP未設定のためコンソール出力]")
            Console.WriteLine($"  宛先: {toEmail}")
            Console.WriteLine($"  確認URL: {callbackUrl}")
            Console.WriteLine("========================================")
        else
            let port = match Int32.TryParse(smtpPortStr) with | true, p -> p | _ -> 587
            let fromAddr = if String.IsNullOrWhiteSpace smtpFrom then smtpUser else smtpFrom

            use client = new SmtpClient(smtpHost, port)
            client.EnableSsl <- port <> 1025  // MailHog(ポート1025)はSSL不要
            if not (String.IsNullOrWhiteSpace smtpUser) then
                client.Credentials <- NetworkCredential(smtpUser, smtpPass)

            use msg = new MailMessage()
            msg.From <- MailAddress(fromAddr, "Shinogi")
            msg.To.Add(toEmail)
            msg.Subject <- "[Shinogi] メールアドレスの確認"
            msg.IsBodyHtml <- true
            msg.Body <-
                $"""<html>
<body style="font-family: sans-serif; background: #000; color: #e4e7ef; padding: 24px;">
  <h2 style="color: #855CF9;">Shinogi アカウント確認</h2>
  <p>Shinogi にアカウントが作成されました。以下のリンクをクリックしてメールアドレスを確認してください。</p>
  <p><a href="{callbackUrl}" style="display: inline-block; background: #855CF9; color: #fff; padding: 12px 24px; border-radius: 6px; text-decoration: none; font-weight: bold;">メールアドレスを確認</a></p>
  <p style="font-size: 12px; color: #6D28D9; margin-top: 24px;">このメールに心当たりがない場合は無視してください。</p>
</body>
</html>"""

            do! client.SendMailAsync(msg)
    } :> Task
