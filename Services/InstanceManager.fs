namespace Shinogi.Services

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open System.Linq
open Microsoft.EntityFrameworkCore
open Shinogi.Domain
open Shinogi.Data

module InstanceManager =

    let minPort = 30000
    let maxPort = 32000
    let private instancesNetworkName = "shinogi-instances"

    let generateInstanceFlag (challengeId: Guid) (userId: Guid) : string =
        let data = $"{challengeId}_{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
        use sha256 = SHA256.Create()
        let hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data))
        let hashStr = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
        $"flag{{{hashStr.Substring(0, 16)}}}"

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

    let runDockerCommand (cwd: string option) (args: string) : Task<string * int> =
        task {
            let psi = ProcessStartInfo()
            psi.FileName <- "docker"
            psi.Arguments <- args
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            psi.CreateNoWindow <- true
            match cwd with
            | Some d when Directory.Exists d -> psi.WorkingDirectory <- d
            | _ -> ()

            use proc = Process.Start(psi)
            let! output = proc.StandardOutput.ReadToEndAsync()
            let! error = proc.StandardError.ReadToEndAsync()
            do! proc.WaitForExitAsync()

            let combined =
                if String.IsNullOrWhiteSpace output then error
                elif String.IsNullOrWhiteSpace error then output
                else output + Environment.NewLine + error
            return (combined.Trim(), proc.ExitCode)
        }

    let private yamlDoubleQuoted (s: string) =
        let esc = s.Replace("\\", "\\\\").Replace("\"", "\\\"")
        $"\"{esc}\""

    let private pickComposeFile (dir: string) =
        let yml = Path.Combine(dir, "docker-compose.yml")
        let yaml = Path.Combine(dir, "docker-compose.yaml")
        if File.Exists yml then Some "docker-compose.yml"
        elif File.Exists yaml then Some "docker-compose.yaml"
        else None

    let private hasDockerfile (dir: string) = File.Exists(Path.Combine(dir, "Dockerfile"))

    let private newImageTag () =
        let hex = Guid.NewGuid().ToString("N")
        $"shinogi/ctf-{hex.Substring(0, 16)}".ToLowerInvariant()

    let private newComposeProject () =
        let hex = Guid.NewGuid().ToString("N")
        $"sg{hex.Substring(0, 24)}".ToLowerInvariant()

    let private tryParseCpu (cpuLimit: string) =
        match Double.TryParse(cpuLimit.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, v -> v
        | _ -> 0.5

    let private writeComposeOverride
        (path: string)
        (serviceName: string)
        (hostPort: int)
        (containerPort: int)
        (flag: string)
        (memLimit: string)
        (cpuLimit: string)
        =
        let cpus = tryParseCpu cpuLimit
        let yaml =
            $"""services:
  {serviceName}:
    ports:
      - "{hostPort}:{containerPort}"
    environment:
      FLAG: {yamlDoubleQuoted flag}
    mem_limit: {yamlDoubleQuoted memLimit}
    cpus: {cpus.ToString(CultureInfo.InvariantCulture)}
    networks:
      - {instancesNetworkName}

networks:
  {instancesNetworkName}:
    external: true
"""
        File.WriteAllText(path, yaml, Encoding.UTF8)

    let private getComposePrimaryService (challengeDir: string) (composeRel: string) : Task<Result<string, string>> =
        task {
            let args = $"compose -f \"{composeRel}\" config --services"
            let! (out, code) = runDockerCommand (Some challengeDir) args
            if code <> 0 then
                return Error $"compose config 失敗: {out}"
            else
                let lines =
                    out.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun s -> s.Trim())
                    |> Array.filter (fun s -> s.Length > 0)
                match lines |> Array.tryHead with
                | None -> return Error "compose にサービス定義がありません。"
                | Some svc -> return Ok svc
        }

    let private getComposeContainerId (project: string) (serviceName: string) : Task<Result<string, string>> =
        task {
            let args = $"compose -p \"{project}\" ps -q \"{serviceName}\""
            let! (out, code) = runDockerCommand None args
            if code <> 0 then
                return Error $"compose ps 失敗: {out}"
            else
                let id =
                    out.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun s -> s.Trim())
                    |> Array.tryHead
                    |> Option.defaultValue ""
                if String.IsNullOrWhiteSpace id then
                    return Error "compose コンテナ ID を取得できませんでした。"
                else
                    return Ok id
        }

    let private stopDockerResources (containerId: string) (composeProject: string) : Task<unit> =
        task {
            if not (String.IsNullOrWhiteSpace composeProject) then
                let! (_, _) = runDockerCommand None $"compose -p \"{composeProject}\" down --remove-orphans"
                ()
            else
                let! (_, _) = runDockerCommand None $"stop {containerId}"
                let! (_, _) = runDockerCommand None $"rm {containerId}"
                ()
        }

    /// compose の external ネットワークおよび docker run --network 用。無ければ作成する。
    let private ensureInstancesNetwork () : Task<Result<unit, string>> =
        task {
            let! (_, inspectCode) = runDockerCommand None $"network inspect {instancesNetworkName}"
            if inspectCode = 0 then
                return Ok ()
            else
                let! (createOut, createCode) = runDockerCommand None $"network create {instancesNetworkName}"
                if createCode = 0 then
                    return Ok ()
                else
                    return Error $"Docker ネットワーク {instancesNetworkName} を作成できません: {createOut}"
        }

    let private randomWordSlug () =
        let words =
            [| "amber"; "breeze"; "coral"; "delta"; "ember"; "frost"; "glade"; "harbor"; "ivory"; "jade"
               "kelp"; "lotus"; "mist"; "nebula"; "ocean"; "pearl"; "quartz"; "river"; "sage"; "tide"
               "unity"; "vapor"; "willow"; "xenon"; "yarn"; "zenith"; "aurora"; "brook"; "cedar"; "dawn"
               "eagle"; "falcon" |]
        let a = words.[Random.Shared.Next(words.Length)]
        let b = words.[Random.Shared.Next(words.Length)]
        let h = Guid.NewGuid().ToString("N").Substring(0, 4)
        $"{a}-{b}-{h}"

    /// 公開 URL（サイトのベース）とホスト側ポートから、コンテナへの直接リンクを組み立てる。
    let private buildPortBasedUrl (publicUrlBase: string) (hostPort: int) =
        let t = publicUrlBase.Trim().TrimEnd('/')
        let uri = Uri(if t.Contains("://") then t else $"http://{t}")
        $"{uri.Scheme}://{uri.Host}:{hostPort}"

    let private buildSubdomainUrl (publicUrlBase: string) (slug: string) (suffix: string) =
        let t = publicUrlBase.Trim().TrimEnd('/')
        let uri = Uri(if t.Contains("://") then t else $"http://{t}")
        let portPart =
            if uri.IsDefaultPort then
                ""
            else
                $":{uri.Port}"
        let sfx = suffix.Trim().ToLowerInvariant()
        $"{uri.Scheme}://{slug}.{sfx}{portPart}/"

    let private reserveUniqueAccessSlug (db: CtfdDbContext) =
        let rec pick attempt =
            task {
                if attempt > 12 then
                    return Error "インスタンス用アクセス名の生成に失敗しました。"
                else
                    let s = randomWordSlug ()
                    let! taken =
                        db.ChallengeInstances.AnyAsync(fun i ->
                            i.Status = InstanceStatus.Running && i.AccessSlug = s)
                    if taken then
                        return! pick (attempt + 1)
                    else
                        return Ok s
            }
        pick 0

    let private resolveInstanceAccessUrl (db: CtfdDbContext) (publicUrl: string) (hostPort: int) =
        task {
            let suffix = Environment.GetEnvironmentVariable("SHINOGI_INSTANCE_HOST_SUFFIX")
            if String.IsNullOrWhiteSpace suffix then
                return Ok("", buildPortBasedUrl publicUrl hostPort)
            else
                match! reserveUniqueAccessSlug db with
                | Error e -> return Error e
                | Ok slug -> return Ok(slug, buildSubdomainUrl publicUrl slug suffix)
        }

    /// インスタンス起動（フォルダ指定時は起動時ビルド。compose 優先。レガシー InstanceImage のみの場合は build なし）。
    let createInstance
        (db: CtfdDbContext)
        (challenge: Challenge)
        (userId: Guid)
        (publicUrl: string)
        (contentRoot: string)
        : Task<Result<ChallengeInstance, string>> =
        task {
            try
                let! existing =
                    db.ChallengeInstances
                      .Where(fun i -> i.UserId = userId && i.ChallengeId = challenge.Id && i.Status = InstanceStatus.Running)
                      .FirstOrDefaultAsync()
                if not (isNull existing) then
                    return Error "既にこのチャレンジのインスタンスが起動しています"
                else
                    let! portOpt = findAvailablePort db
                    match portOpt with
                    | None -> return Error "利用可能なポートがありません"
                    | Some hostPort ->
                        let flag = generateInstanceFlag challenge.Id userId
                        let containerName = $"ctf-{userId.ToString().Substring(0, 8)}-{challenge.Id.ToString().Substring(0, 8)}"
                        let containerPortStr =
                            match challenge.InstancePort with
                            | Some p -> p.ToString()
                            | None -> "80"

                        let containerPort =
                            match Int32.TryParse(containerPortStr) with
                            | true, p -> p
                            | _ -> 80

                        let mem = if String.IsNullOrWhiteSpace challenge.InstanceMemoryLimit then "256m" else challenge.InstanceMemoryLimit.Trim()
                        let cpu = if String.IsNullOrWhiteSpace challenge.InstanceCpuLimit then "0.5" else challenge.InstanceCpuLimit.Trim()

                        let folder = if isNull challenge.InstanceDockerFolder then "" else challenge.InstanceDockerFolder.Trim()
                        let legacyImage = if isNull challenge.InstanceImage then "" else challenge.InstanceImage.Trim()

                        let! buildResult =
                            task {
                                let! netRes = ensureInstancesNetwork ()
                                match netRes with
                                | Error e -> return Error e
                                | Ok () ->
                                    if not (String.IsNullOrWhiteSpace folder) then
                                        match DockerChallengeFolders.tryResolveDirectory contentRoot folder with
                                        | Error e -> return Error e
                                        | Ok challengeDir ->
                                            match pickComposeFile challengeDir with
                                            | Some composeRel ->
                                                let! svcRes = getComposePrimaryService challengeDir composeRel
                                                match svcRes with
                                                | Error e -> return Error e
                                                | Ok serviceName ->
                                                    let project = newComposeProject ()
                                                    let overridePath = Path.Combine(Path.GetTempPath(), $"shinogi-{Guid.NewGuid():N}.override.yml")
                                                    // 相対 -f はプロセスの cwd に依存するため、チャレンジ直下の compose を絶対パスで渡す。
                                                    let composeAbs = Path.GetFullPath(Path.Combine(challengeDir, composeRel))
                                                    try
                                                        writeComposeOverride overridePath serviceName hostPort containerPort flag mem cpu
                                                        let baseArgs (cmd: string) =
                                                            $"compose -f \"{composeAbs}\" -f \"{overridePath}\" -p \"{project}\" --project-directory \"{challengeDir}\" {cmd}"
                                                        let! (buildOut, bCode) = runDockerCommand (Some challengeDir) (baseArgs "build")
                                                        if bCode <> 0 then
                                                            return Error $"Docker compose build 失敗: {buildOut}"
                                                        else
                                                            let! (upOut, uCode) = runDockerCommand (Some challengeDir) (baseArgs "up -d")
                                                            if uCode <> 0 then
                                                                return Error $"Docker compose up 失敗: {upOut}"
                                                            else
                                                                let! cidRes = getComposeContainerId project serviceName
                                                                match cidRes with
                                                                | Error e -> return Error e
                                                                | Ok cid -> return Ok(cid, project)
                                                    finally
                                                        try
                                                            File.Delete overridePath
                                                        with _ ->
                                                            ()
                                            | None ->
                                                if not (hasDockerfile challengeDir) then
                                                    return Error "Dockerfile も docker-compose も見つかりません。"
                                                else
                                                    let tag = newImageTag ()
                                                    let! (bOut, bCode) = runDockerCommand (Some challengeDir) $"build -t \"{tag}\" ."
                                                    if bCode <> 0 then
                                                        return Error $"Docker build 失敗: {bOut}"
                                                    else
                                                        let dockerArgs =
                                                            $"run -d --name {containerName} " +
                                                            $"--network {instancesNetworkName} " +
                                                            $"-p {hostPort}:{containerPortStr} " +
                                                            $"--cpus=\"{cpu}\" " +
                                                            $"--memory=\"{mem}\" " +
                                                            $"-e FLAG=\"{flag}\" " +
                                                            $"\"{tag}\""
                                                        let! (runOut, rCode) = runDockerCommand None dockerArgs
                                                        if rCode <> 0 then
                                                            return Error $"Docker run 失敗: {runOut}"
                                                        else
                                                            return Ok(runOut.Trim(), "")
                                    elif not (String.IsNullOrWhiteSpace legacyImage) then
                                        let dockerArgs =
                                            $"run -d --name {containerName} " +
                                            $"--network {instancesNetworkName} " +
                                            $"-p {hostPort}:{containerPortStr} " +
                                            $"--cpus=\"{cpu}\" " +
                                            $"--memory=\"{mem}\" " +
                                            $"-e FLAG=\"{flag}\" " +
                                            $"{legacyImage}"
                                        let! (runOut, rCode) = runDockerCommand None dockerArgs
                                        if rCode <> 0 then
                                            return Error $"Dockerコンテナ起動失敗: {runOut}"
                                        else
                                            return Ok(runOut.Trim(), "")
                                    else
                                        return Error "インスタンス用の docker-challenges フォルダまたは Docker イメージが設定されていません。"
                            }

                        match buildResult with
                        | Error e -> return Error e
                        | Ok(containerId, composeProject) ->
                            let! urlRes = resolveInstanceAccessUrl db publicUrl hostPort
                            match urlRes with
                            | Error e -> return Error e
                            | Ok(accessSlug, displayUrl) ->
                                let now = DateTimeOffset.UtcNow
                                let expiresAt = now.AddMinutes(float challenge.InstanceLifetimeMinutes)

                                let instance =
                                    { Id = Guid.NewGuid()
                                      ChallengeId = challenge.Id
                                      UserId = userId
                                      ContainerId = containerId
                                      ComposeProject = composeProject
                                      HostPort = hostPort
                                      AccessSlug = accessSlug
                                      Url = displayUrl
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

    let stopInstance (db: CtfdDbContext) (instanceId: Guid) : Task<Result<unit, string>> =
        task {
            try
                let! instance = db.ChallengeInstances.FirstOrDefaultAsync(fun i -> i.Id = instanceId)
                if isNull instance then
                    return Error "インスタンスが見つかりません"
                else
                    let composeProj = if isNull instance.ComposeProject then "" else instance.ComposeProject
                    do! stopDockerResources instance.ContainerId composeProj

                    db.Entry(instance).Property("Status").CurrentValue <- InstanceStatus.Stopped
                    let! _ = db.SaveChangesAsync()
                    return Ok ()
            with ex ->
                return Error $"インスタンス停止中にエラーが発生しました: {ex.Message}"
        }

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
                    let composeProj = if isNull instance.ComposeProject then "" else instance.ComposeProject
                    do! stopDockerResources instance.ContainerId composeProj
                    db.Entry(instance).Property("Status").CurrentValue <- InstanceStatus.Expired
                    cleanedCount <- cleanedCount + 1

                if cleanedCount > 0 then
                    let! _ = db.SaveChangesAsync()
                    Console.WriteLine($"期限切れインスタンスを {cleanedCount} 個削除しました")

                return cleanedCount
            with ex ->
                Console.WriteLine($"クリーンアップ中にエラー: {ex.Message}")
                return 0
        }

    let canCreateInstance (db: CtfdDbContext) (userId: Guid) : Task<bool> =
        task {
            let maxConcurrent = 3
            let! count =
                db.ChallengeInstances
                  .CountAsync(fun i -> i.UserId = userId && i.Status = InstanceStatus.Running)
            return count < maxConcurrent
        }
