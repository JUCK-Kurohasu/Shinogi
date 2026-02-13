namespace Shinogi.Services

open System
open System.Diagnostics
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open System.Linq
open Microsoft.EntityFrameworkCore
open Shinogi.Domain
open Shinogi.Data

module InstanceManager =

    // 利用可能なポート範囲
    let minPort = 30000
    let maxPort = 32000

    // インスタンス専用フラグ生成
    let generateInstanceFlag (challengeId: Guid) (userId: Guid) : string =
        let data = $"{challengeId}_{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
        use sha256 = SHA256.Create()
        let hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data))
        let hashStr = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
        $"flag{{{hashStr.Substring(0, 16)}}}"

    // 利用可能なポート番号を取得
    let findAvailablePort (db: CtfdDbContext) : Task<int option> =
        task {
            let! usedPorts =
                db.ChallengeInstances
                  .Where(fun i -> i.Status = InstanceStatus.Running)
                  .Select(fun i -> i.HostPort)
                  .ToListAsync()
            let usedSet = Set.ofSeq usedPorts
            let availablePort =
                seq { minPort .. maxPort }
                |> Seq.tryFind (fun p -> not (usedSet.Contains p))
            return availablePort
        }

    // Dockerコマンド実行ヘルパー
    let runDockerCommand (args: string) : Task<string * int> =
        task {
            let psi = ProcessStartInfo()
            psi.FileName <- "docker"
            psi.Arguments <- args
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            psi.CreateNoWindow <- true

            use proc = Process.Start(psi)
            let! output = proc.StandardOutput.ReadToEndAsync()
            let! error = proc.StandardError.ReadToEndAsync()
            do! proc.WaitForExitAsync()

            let result = if String.IsNullOrWhiteSpace(output) then error else output
            return (result.Trim(), proc.ExitCode)
        }

    // インスタンス起動
    let createInstance (db: CtfdDbContext) (challenge: Challenge) (userId: Guid) (publicUrl: string) : Task<Result<ChallengeInstance, string>> =
        task {
            try
                // ユーザーの既存インスタンスをチェック
                let! existing =
                    db.ChallengeInstances
                      .Where(fun i -> i.UserId = userId && i.ChallengeId = challenge.Id && i.Status = InstanceStatus.Running)
                      .FirstOrDefaultAsync()
                if not (isNull existing) then
                    return Error "既にこのチャレンジのインスタンスが起動しています"
                else
                    // ポート取得
                    let! portOpt = findAvailablePort db
                    match portOpt with
                    | None -> return Error "利用可能なポートがありません"
                    | Some hostPort ->
                        // フラグ生成
                        let flag = generateInstanceFlag challenge.Id userId

                        // コンテナ名
                        let containerName = $"ctf-{userId.ToString().Substring(0, 8)}-{challenge.Id.ToString().Substring(0, 8)}"

                        // Dockerコマンド構築
                        let containerPortStr =
                            match challenge.InstancePort with
                            | Some p -> p.ToString()
                            | None -> "80"

                        let dockerArgs =
                            $"run -d --name {containerName} " +
                            $"--network shinogi-instances " +
                            $"-p {hostPort}:{containerPortStr} " +
                            $"--cpus=\"{challenge.InstanceCpuLimit}\" " +
                            $"--memory=\"{challenge.InstanceMemoryLimit}\" " +
                            $"-e FLAG=\"{flag}\" " +
                            $"{challenge.InstanceImage}"

                        // Docker実行
                        let! (output, exitCode) = runDockerCommand dockerArgs

                        if exitCode <> 0 then
                            return Error $"Dockerコンテナ起動失敗: {output}"
                        else
                            let containerId = output.Trim()
                            let url = $"{publicUrl}:{hostPort}"
                            let now = DateTimeOffset.UtcNow
                            let expiresAt = now.AddMinutes(float challenge.InstanceLifetimeMinutes)

                            let instance =
                                { Id = Guid.NewGuid()
                                  ChallengeId = challenge.Id
                                  UserId = userId
                                  ContainerId = containerId
                                  HostPort = hostPort
                                  Url = url
                                  Flag = flag
                                  CreatedAt = now
                                  ExpiresAt = expiresAt
                                  Status = InstanceStatus.Running }

                            db.ChallengeInstances.Add(instance) |> ignore
                            let! _ = db.SaveChangesAsync()

                            return Ok instance
            with ex ->
                return Error $"インスタンス作成中にエラーが発生しました: {ex.Message}"
        }

    // インスタンス停止
    let stopInstance (db: CtfdDbContext) (instanceId: Guid) : Task<Result<unit, string>> =
        task {
            try
                let! instance = db.ChallengeInstances.FirstOrDefaultAsync(fun i -> i.Id = instanceId)
                if isNull instance then
                    return Error "インスタンスが見つかりません"
                else
                    // Docker停止・削除
                    let! (_, stopCode) = runDockerCommand $"stop {instance.ContainerId}"
                    let! (_, rmCode) = runDockerCommand $"rm {instance.ContainerId}"

                    // ステータス更新
                    let updated = { instance with Status = InstanceStatus.Stopped }
                    db.ChallengeInstances.Update(updated) |> ignore
                    let! _ = db.SaveChangesAsync()

                    return Ok ()
            with ex ->
                return Error $"インスタンス停止中にエラーが発生しました: {ex.Message}"
        }

    // 期限切れインスタンスのクリーンアップ
    let cleanupExpiredInstances (db: CtfdDbContext) : Task<int> =
        task {
            try
                let now = DateTimeOffset.UtcNow
                let! expired =
                    db.ChallengeInstances
                      .Where(fun i -> i.Status = InstanceStatus.Running && i.ExpiresAt <= now)
                      .ToListAsync()

                let mutable cleanedCount = 0
                for instance in expired do
                    let! (_, stopCode) = runDockerCommand $"stop {instance.ContainerId}"
                    let! (_, rmCode) = runDockerCommand $"rm {instance.ContainerId}"
                    let updated = { instance with Status = InstanceStatus.Expired }
                    db.ChallengeInstances.Update(updated) |> ignore
                    cleanedCount <- cleanedCount + 1

                if cleanedCount > 0 then
                    let! _ = db.SaveChangesAsync()
                    Console.WriteLine($"期限切れインスタンスを {cleanedCount} 個削除しました")

                return cleanedCount
            with ex ->
                Console.WriteLine($"クリーンアップ中にエラー: {ex.Message}")
                return 0
        }

    // ユーザーの同時起動数チェック
    let canCreateInstance (db: CtfdDbContext) (userId: Guid) : Task<bool> =
        task {
            let maxConcurrent = 3
            let! count =
                db.ChallengeInstances
                  .CountAsync(fun i -> i.UserId = userId && i.Status = InstanceStatus.Running)
            return count < maxConcurrent
        }
