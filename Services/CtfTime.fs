namespace Shinogi.Services

open System
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Shinogi.Domain
open Shinogi.Data

/// CTF イベントの開催時間ウィンドウ（CtfSettings.EventStart/EventEnd）の判定。
module CtfTime =

    /// イベントの現在状態。設定が無い場合は常に Running。
    type EventState =
        | NotStarted of DateTimeOffset
        | Running
        | Ended of DateTimeOffset

    let getEventState (db: CtfdDbContext) : Task<EventState> =
        task {
            let! settings = db.CtfSettings.AsNoTracking().FirstOrDefaultAsync()
            if isNull (box settings) then
                return Running
            else
                let now = DateTimeOffset.UtcNow
                match settings.EventStart with
                | Some start when now < start -> return NotStarted start
                | _ ->
                    match settings.EventEnd with
                    | Some endAt when now > endAt -> return Ended endAt
                    | _ -> return Running
        }

    /// 提出を受け付けられる状態か。エラー時は理由の日本語メッセージを返す。
    let checkSubmittable (db: CtfdDbContext) : Task<Result<unit, string>> =
        task {
            let! state = getEventState db
            match state with
            | NotStarted start ->
                let startLocal = start.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                return Error $"CTF はまだ開始されていません（開始: {startLocal}）。"
            | Ended _ ->
                return Error "CTF は終了しました。フラグの提出はできません。"
            | Running ->
                return Ok ()
        }

    /// チャレンジが公開時刻（ReleaseAt）を迎えているか。
    let isReleased (now: DateTimeOffset) (c: Challenge) =
        match c.ReleaseAt with
        | Some releaseAt -> releaseAt <= now
        | None -> true

    /// スコアボード凍結時刻。未設定なら None。
    let getFreezeAt (db: CtfdDbContext) : Task<DateTimeOffset option> =
        task {
            let! settings = db.CtfSettings.AsNoTracking().FirstOrDefaultAsync()
            if isNull (box settings) then
                return None
            else
                return settings.FreezeAt
        }
