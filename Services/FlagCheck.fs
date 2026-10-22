namespace Shinogi.Services

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Npgsql
open Shinogi.Data

/// フラグ判定。MVC / JWT API / チームAPI の3経路で共有する。
module FlagCheck =

    /// 正解提出の一意制約名（同一アカウント×同一チャレンジの IsCorrect=true は1件のみ）。
    let correctSubmissionIndexName = "IX_Submissions_AccountId_ChallengeId_Correct"

    /// 静的フラグ（Flags テーブル）またはインスタンス固有フラグに一致するか。
    /// instanceOwnerIds: インスタンス固有フラグを受け付けるインスタンス起動者（本人、チームAPIならチームメンバー全員）。
    let isCorrect (db: CtfdDbContext) (challengeId: Guid) (instanceOwnerIds: Guid[]) (submitted: string) : Task<bool> =
        task {
            let normalized = if isNull submitted then "" else submitted.Trim()
            let hashExact = Security.sha256 normalized
            let hashLower = Security.sha256 (normalized.ToLowerInvariant())
            let! staticMatch =
                db.Flags.AnyAsync(fun f ->
                    f.ChallengeId = challengeId
                    && ((f.CaseSensitive && f.ContentHash = hashExact) || ((not f.CaseSensitive) && f.ContentHash = hashLower)))
            if staticMatch then
                return true
            elif normalized = "" || instanceOwnerIds.Length = 0 then
                return false
            else
                // 停止・期限切れ後でも、起動中に得たフラグは有効とする。他人のインスタンスのフラグは不正解。
                return!
                    db.ChallengeInstances.AnyAsync(fun i ->
                        i.ChallengeId = challengeId && instanceOwnerIds.Contains(i.UserId) && i.Flag = normalized)
        }

    /// SaveChanges の例外が、同時提出による正解の重複（一意制約違反）か。
    let private isDuplicateCorrect (ex: exn) =
        match ex with
        | :? DbUpdateException as dbEx ->
            match dbEx.InnerException with
            | :? PostgresException as pg ->
                pg.SqlState = PostgresErrorCodes.UniqueViolation && pg.ConstraintName = correctSubmissionIndexName
            | _ -> false
        | _ -> false

    /// 追加済みの Submission を保存する。既解チェック後に同時提出が先に正解を記録していた場合は false。
    let trySaveSubmission (db: CtfdDbContext) : Task<bool> =
        task {
            try
                let! _ = db.SaveChangesAsync()
                return true
            with ex when isDuplicateCorrect ex ->
                return false
        }
