module Shinogi.Program

open System
open System.IO
open System.Text
open System.Threading.Tasks
open System.Linq
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration
open Microsoft.EntityFrameworkCore
open Microsoft.AspNetCore.Identity
open Microsoft.AspNetCore.Authentication.JwtBearer
open Microsoft.IdentityModel.Tokens
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.RateLimiting
open System.Threading.RateLimiting
open Microsoft.FSharp.Linq
open Microsoft.AspNetCore.Mvc
open Shinogi.Domain
open Shinogi.Data
open Shinogi.Dtos
open Shinogi.ViewModels
open Shinogi.Services
open Shinogi.Services.Scoring
open Shinogi.Services.Security

let configureServices (builder: WebApplicationBuilder) =
    builder.Services.AddDbContext<CtfdDbContext>(
        Action<DbContextOptionsBuilder>(fun opt ->
            opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")) |> ignore)
    ) |> ignore

    builder.Services.AddIdentity<CtfdUser, IdentityRole<Guid>>(fun o ->
        o.SignIn.RequireConfirmedEmail <- true
        o.Password.RequireNonAlphanumeric <- false
        o.Password.RequireUppercase <- false)
        .AddEntityFrameworkStores<CtfdDbContext>()
        .AddDefaultTokenProviders()
        |> ignore

    let keyBytes = builder.Configuration["Jwt:Key"] |> Encoding.UTF8.GetBytes |> SymmetricSecurityKey

    builder.Services
        .AddAuthentication()
        .AddJwtBearer(fun o ->
            o.TokenValidationParameters <- TokenValidationParameters(
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = keyBytes))
        |> ignore

    builder.Services.AddAuthorization(fun opts ->
        opts.AddPolicy("AdminOnly", fun p -> p.RequireRole("Admins") |> ignore)) |> ignore

    builder.Services.AddRateLimiter(fun opts ->
        opts.GlobalLimiter <- PartitionedRateLimiter.Create(fun (ctx: HttpContext) ->
            let ip =
                match ctx.Connection.RemoteIpAddress with
                | null -> "unknown"
                | addr -> addr.ToString()
            RateLimitPartition.GetFixedWindowLimiter(ip, fun _ ->
                FixedWindowRateLimiterOptions(PermitLimit = 20, Window = TimeSpan.FromMinutes 1., QueueLimit = 0))))
        |> ignore

    builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation() |> ignore
    builder.Services.AddEndpointsApiExplorer() |> ignore
    builder.Services.AddSwaggerGen() |> ignore

let loadDotEnv (path: string) =
    if File.Exists(path) then
        File.ReadAllLines(path)
        |> Array.iter (fun line ->
            let trimmed = line.Trim()
            if trimmed <> "" && not (trimmed.StartsWith("#")) then
                let idx = trimmed.IndexOf('=')
                if idx > 0 then
                    let key = trimmed.Substring(0, idx).Trim()
                    let raw = trimmed.Substring(idx + 1).Trim()
                    let value =
                        if raw.Length >= 2 then
                            let first = raw[0]
                            let last = raw[raw.Length - 1]
                            if (first = '"' && last = '"') || (first = '\'' && last = '\'') then
                                raw.Substring(1, raw.Length - 2)
                            else
                                raw
                        else
                            raw
                    if String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)) then
                        Environment.SetEnvironmentVariable(key, value))

let issueJwt (config: Microsoft.Extensions.Configuration.IConfiguration) (user: CtfdUser) =
    let claims =
        [| System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString())
           System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.UserName)
           System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, user.Email)
           System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "user") |]
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]))
    let creds = SigningCredentials(key, SecurityAlgorithms.HmacSha256)
    let token = System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                    issuer = config["Jwt:Issuer"],
                    audience = config["Jwt:Audience"],
                    claims = claims,
                    expires = DateTime.UtcNow.AddHours(6.0),
                    signingCredentials = creds)
    System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token)

let mapRoutes (app: WebApplication) =
    let requireTrustedHost (ctx: HttpContext) =
        let envHosts =
            let list = Environment.GetEnvironmentVariable("SHINOGI_TRUSTED_HOSTS")
            let fromList =
                if String.IsNullOrWhiteSpace list then
                    [||]
                else
                    list.Split(',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            let url = Environment.GetEnvironmentVariable("SHINOGI_PUBLIC_URL")
            let fromUrl =
                if String.IsNullOrWhiteSpace url then
                    [||]
                else
                    try
                        [| Uri(url).Host |]
                    with _ ->
                        [||]
            Array.append fromList fromUrl
        let allowed =
            if envHosts.Length > 0 then
                envHosts
            else
                app.Configuration.GetSection("TrustedHosts").Get<string[]>() |> Option.ofObj |> Option.defaultValue [||]
        if allowed.Length = 0 then true
        else allowed |> Array.exists (fun h -> String.Equals(h, ctx.Request.Host.Host, StringComparison.OrdinalIgnoreCase))

    app.Use(fun (next: RequestDelegate) ->
        RequestDelegate(fun ctx ->
            task {
                if not (requireTrustedHost ctx) then
                    ctx.Response.StatusCode <- StatusCodes.Status400BadRequest
                    return ()
                else
                    return! next.Invoke ctx
            } :> Task))
    |> ignore

    app.UseHttpsRedirection() |> ignore
    app.UseStaticFiles() |> ignore
    app.UseRouting() |> ignore
    app.UseAuthentication() |> ignore
    app.UseAuthorization() |> ignore
    app.UseRateLimiter() |> ignore
    app.UseSwagger() |> ignore
    app.UseSwaggerUI() |> ignore

    // ===== Auth API =====
    let auth = app.MapGroup("/api/v1/auth")
    auth.MapPost("/register", Func<RegisterDto, UserManager<CtfdUser>, CtfdDbContext, Task<IResult>>(fun dto userMgr db ->
        task {
            if String.IsNullOrWhiteSpace dto.Email || String.IsNullOrWhiteSpace dto.Password then
                return Results.BadRequest("Missing email or password")
            else
                let user = CtfdUser(Email = dto.Email, UserName = dto.Email, DisplayName = dto.DisplayName, EmailConfirmed = true)
                let! res = userMgr.CreateAsync(user, dto.Password)
                if not res.Succeeded then
                    return Results.BadRequest(res.Errors |> Seq.map (fun e -> e.Description))
                else
                    return Results.Ok(issueJwt app.Configuration user)
        } :> Task<IResult>
    )) |> ignore

    auth.MapPost("/login", Func<LoginDto, SignInManager<CtfdUser>, UserManager<CtfdUser>, Task<IResult>>(fun dto signInMgr userMgr ->
        task {
            let! user = userMgr.FindByEmailAsync(dto.Email)
            match user with
            | null -> return Results.Unauthorized()
            | u ->
                let! check = signInMgr.CheckPasswordSignInAsync(u, dto.Password, true)
                if not check.Succeeded then
                    return Results.Unauthorized()
                else
                    return Results.Ok(issueJwt app.Configuration u)
        } :> Task<IResult>
    )) |> ignore

    // ===== Challenges API =====
    let challenges = app.MapGroup("/api/v1/challenges")
    challenges.MapGet("/", Func<CtfdDbContext, IResult>(fun db ->
        db.Challenges
          .Where(fun c -> c.Published)
          .OrderBy(fun c -> c.CreatedAt)
          |> Results.Ok)) |> ignore

    let challengeCreateHandler =
        challenges.MapPost("/", Func<ChallengeCreateDto, CtfdDbContext, Task<IResult>>(fun dto db ->
            task {
                let func =
                    match dto.Function.ToLowerInvariant() with
                    | "log" -> ScoreFunction.Log
                    | "exp" -> ScoreFunction.Exp
                    | _ -> ScoreFunction.Linear
                let logic =
                    match dto.Logic.ToLowerInvariant() with
                    | "all" -> ChallengeLogic.All
                    | "teamconsensus" -> ChallengeLogic.TeamConsensus
                    | _ -> ChallengeLogic.Any
                let challenge =
                  { Id = Guid.NewGuid()
                    Name = dto.Name
                    Category = dto.Category
                    Difficulty = ""
                    Description = dto.Description
                    ValueInitial = dto.ValueInitial
                    ValueMinimum = dto.ValueMinimum
                    Decay = dto.Decay
                    Function = func
                    Logic = logic
                    MaxAttempts = dto.MaxAttempts
                    Published = false
                    ReleaseAt = None
                    CreatedAt = DateTimeOffset.UtcNow }
                db.Challenges.Add(challenge) |> ignore
                let! _ = db.SaveChangesAsync()
                return Results.Created($"/api/v1/challenges/{challenge.Id}", challenge)
            } :> Task<IResult>))
    challengeCreateHandler.RequireAuthorization("AdminOnly") |> ignore

    let flagCreateHandler =
        challenges.MapPost("/{id:guid}/flags", Func<Guid, FlagCreateDto, CtfdDbContext, Task<IResult>>(fun id dto db ->
            task {
                let! exists = db.Challenges.AnyAsync(fun c -> c.Id = id)
                if not exists then
                    return Results.NotFound()
                else
                    let flag =
                      { Id = Guid.NewGuid()
                        ChallengeId = id
                        Content = dto.Content
                        ContentHash = Security.sha256 (if dto.CaseSensitive then dto.Content else dto.Content.ToLowerInvariant())
                        CaseSensitive = dto.CaseSensitive }
                    db.Flags.Add(flag) |> ignore
                    let! _ = db.SaveChangesAsync()
                    return Results.Created($"/api/v1/challenges/{id}/flags/{flag.Id}", flag.Id)
            } :> Task<IResult>))
    flagCreateHandler.RequireAuthorization("AdminOnly") |> ignore

    // ===== Submit via JWT (user) =====
    let submitHandler =
        challenges.MapPost("/{id:guid}/submit", Func<Guid, SubmitDto, HttpContext, CtfdDbContext, Task<IResult>>(fun id dto ctx db ->
            task {
                let userIdClaim = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                if isNull userIdClaim then
                    return Results.Unauthorized()
                else
                    let userId = Guid.Parse userIdClaim.Value
                    let! challenge = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id && c.Published)
                    match challenge with
                    | null -> return Results.NotFound()
                    | c ->
                        let! attempts = db.Submissions.CountAsync(fun s -> s.AccountId = userId && s.ChallengeId = id)
                        match c.MaxAttempts with
                        | Some max when attempts >= max -> return Results.BadRequest("Max attempts reached")
                        | _ ->
                            let normalized = if isNull dto.Flag then "" else dto.Flag
                            let hashExact = Security.sha256 normalized
                            let hashLower = Security.sha256 (normalized.ToLowerInvariant())
                            let! correct = db.Flags.AnyAsync(fun f -> f.ChallengeId = id && ((f.CaseSensitive && f.ContentHash = hashExact) || ((not f.CaseSensitive) && f.ContentHash = hashLower)))
                            let! solveCount = db.Submissions.CountAsync(fun s -> s.ChallengeId = id && s.IsCorrect)
                            let awarded = if correct then Scoring.dynamicValue c solveCount else 0
                            let ip =
                                match ctx.Connection.RemoteIpAddress with
                                | null -> "unknown"
                                | addr -> addr.ToString()
                            let submission =
                                { Id = Guid.NewGuid()
                                  AccountId = userId
                                  ChallengeId = id
                                  SubmittedAt = DateTimeOffset.UtcNow
                                  IsCorrect = correct
                                  ValueAwarded = awarded
                                  Ip = ip }
                            db.Submissions.Add(submission) |> ignore
                            let! _ = db.SaveChangesAsync()
                            if correct then
                                return Results.Ok(box {| status = "correct"; value = awarded |})
                            else
                                return Results.Ok(box {| status = "incorrect" |})
            } :> Task<IResult>))
    submitHandler.RequireAuthorization() |> ignore

    // ===== Scoreboard API =====
    app.MapGet("/api/v1/scoreboard", Func<CtfdDbContext, Task<IResult>>(fun db ->
        task {
            let! submissions =
                db.Submissions
                  .Where(fun s -> s.IsCorrect)
                  .ToListAsync()
            let scores =
                submissions
                |> Seq.groupBy (fun s -> s.AccountId)
                |> Seq.map (fun (accountId, items) ->
                    { AccountId = accountId
                      DisplayName = ""
                      TeamName = ""
                      Score = items |> Seq.sumBy (fun s -> s.ValueAwarded) })
                |> Seq.sortByDescending (fun e -> e.Score)
                |> Seq.toList
            return Results.Ok(scores)
        } :> Task<IResult>
    )) |> ignore

    // ===== Team Token API =====
    let resolveTeamFromToken (ctx: HttpContext) (db: CtfdDbContext) =
        task {
            let tokenHeader = ctx.Request.Headers.["X-Team-Token"].ToString()
            if String.IsNullOrWhiteSpace tokenHeader then
                return None
            else
                match Guid.TryParse tokenHeader with
                | false, _ -> return None
                | true, tokenGuid ->
                    let! team = db.Teams.FirstOrDefaultAsync(fun t -> t.Token = tokenGuid)
                    match team with
                    | null -> return None
                    | t -> return Some t
        }

    let teamApi = app.MapGroup("/api/v1/team")

    // GET /api/v1/team/info — チーム情報取得
    teamApi.MapGet("/info", Func<HttpContext, CtfdDbContext, Task<IResult>>(fun ctx db ->
        task {
            let! teamOpt = resolveTeamFromToken ctx db
            match teamOpt with
            | None -> return Results.Unauthorized()
            | Some team ->
                let! members =
                    db.TeamMembers
                      .Where(fun m -> m.TeamId = team.Id)
                      .ToListAsync()
                let! memberInfos =
                    task {
                        let results = System.Collections.Generic.List<_>()
                        for m in members do
                            let! user = db.Users.FirstOrDefaultAsync(fun u -> u.Id = m.UserId)
                            let displayName = if isNull user then "不明" else user.DisplayName
                            let email = if isNull user then "" else user.Email
                            results.Add(box {| userId = m.UserId; displayName = displayName; email = email; role = m.Role.ToString(); joinedAt = m.JoinedAt |})
                        return results |> Seq.toList
                    }
                return Results.Ok(box {| id = team.Id; name = team.Name; createdAt = team.CreatedAt; members = memberInfos |})
        } :> Task<IResult>
    )) |> ignore

    // GET /api/v1/team/challenges — チャレンジ一覧取得
    teamApi.MapGet("/challenges", Func<HttpContext, CtfdDbContext, Task<IResult>>(fun ctx db ->
        task {
            let! teamOpt = resolveTeamFromToken ctx db
            match teamOpt with
            | None -> return Results.Unauthorized()
            | Some _ ->
                let! challenges =
                    db.Challenges
                      .Where(fun c -> c.Published)
                      .OrderBy(fun c -> c.CreatedAt)
                      .Select(fun c -> box {| id = c.Id; name = c.Name; category = c.Category; description = c.Description; valueInitial = c.ValueInitial; valueMinimum = c.ValueMinimum; decay = c.Decay; func = c.Function.ToString(); logic = c.Logic.ToString() |})
                      .ToListAsync()
                return Results.Ok(challenges)
        } :> Task<IResult>
    )) |> ignore

    // POST /api/v1/team/challenges/{id}/submit — フラグ提出（AI対応）
    teamApi.MapPost("/challenges/{id:guid}/submit", Func<Guid, SubmitDto, HttpContext, CtfdDbContext, Task<IResult>>(fun id dto ctx db ->
        task {
            let! teamOpt = resolveTeamFromToken ctx db
            match teamOpt with
            | None -> return Results.Unauthorized()
            | Some team ->
                let! aiMember =
                    db.TeamMembers.FirstOrDefaultAsync(fun m -> m.TeamId = team.Id && m.Role = MemberRole.AI)
                let submitterId =
                    match aiMember with
                    | null ->
                        // AIメンバーがいない場合はオーナーのIDを使用
                        let ownerTask = db.TeamMembers.FirstOrDefaultAsync(fun m -> m.TeamId = team.Id && m.Role = MemberRole.Owner)
                        ownerTask.GetAwaiter().GetResult()
                        |> fun o -> if isNull o then team.Id else o.UserId
                    | ai -> ai.UserId
                let! challenge = db.Challenges.FirstOrDefaultAsync(fun c -> c.Id = id && c.Published)
                match challenge with
                | null -> return Results.NotFound()
                | c ->
                    let! attempts = db.Submissions.CountAsync(fun s -> s.AccountId = submitterId && s.ChallengeId = id)
                    match c.MaxAttempts with
                    | Some max when attempts >= max -> return Results.BadRequest("試行回数の上限に達しました")
                    | _ ->
                        let normalized = if isNull dto.Flag then "" else dto.Flag
                        let hashExact = Security.sha256 normalized
                        let hashLower = Security.sha256 (normalized.ToLowerInvariant())
                        let! correct = db.Flags.AnyAsync(fun f -> f.ChallengeId = id && ((f.CaseSensitive && f.ContentHash = hashExact) || ((not f.CaseSensitive) && f.ContentHash = hashLower)))
                        let! solveCount = db.Submissions.CountAsync(fun s -> s.ChallengeId = id && s.IsCorrect)
                        let awarded = if correct then Scoring.dynamicValue c solveCount else 0
                        let ip =
                            match ctx.Connection.RemoteIpAddress with
                            | null -> "unknown"
                            | addr -> addr.ToString()
                        let submission =
                            { Id = Guid.NewGuid()
                              AccountId = submitterId
                              ChallengeId = id
                              SubmittedAt = DateTimeOffset.UtcNow
                              IsCorrect = correct
                              ValueAwarded = awarded
                              Ip = ip }
                        db.Submissions.Add(submission) |> ignore
                        let! _ = db.SaveChangesAsync()
                        if correct then
                            return Results.Ok(box {| status = "correct"; value = awarded |})
                        else
                            return Results.Ok(box {| status = "incorrect" |})
        } :> Task<IResult>
    )) |> ignore

    // GET /api/v1/team/challenges/{id}/files — 配布ファイル一覧
    teamApi.MapGet("/challenges/{id:guid}/files", Func<Guid, HttpContext, CtfdDbContext, Task<IResult>>(fun id ctx db ->
        task {
            let! teamOpt = resolveTeamFromToken ctx db
            match teamOpt with
            | None -> return Results.Unauthorized()
            | Some _ ->
                let! exists = db.Challenges.AnyAsync(fun c -> c.Id = id && c.Published)
                if not exists then
                    return Results.NotFound()
                else
                    let! files =
                        db.ChallengeFiles
                          .Where(fun f -> f.ChallengeId = id)
                          .OrderBy(fun f -> f.UploadedAt)
                          .Select(fun f -> box {| id = f.Id; originalName = f.OriginalName; uploadedAt = f.UploadedAt |})
                          .ToListAsync()
                    return Results.Ok(box {| challengeId = id; files = files |})
        } :> Task<IResult>
    )) |> ignore

    // GET /api/v1/team/challenges/{id}/files/{fileId} — 配布ファイルダウンロード
    teamApi.MapGet("/challenges/{id:guid}/files/{fileId:guid}", Func<Guid, Guid, HttpContext, CtfdDbContext, Task<IResult>>(fun id fileId ctx db ->
        task {
            let! teamOpt = resolveTeamFromToken ctx db
            match teamOpt with
            | None -> return Results.Unauthorized()
            | Some _ ->
                let! file = db.ChallengeFiles.FirstOrDefaultAsync(fun f -> f.Id = fileId && f.ChallengeId = id)
                match file with
                | null -> return Results.NotFound()
                | f ->
                    let uploadsDir = Path.Combine(app.Environment.ContentRootPath, "uploads", id.ToString())
                    let filePath = Path.Combine(uploadsDir, f.StoredName)
                    if not (File.Exists filePath) then
                        return Results.NotFound()
                    else
                        let bytes = File.ReadAllBytes(filePath)
                        return Results.File(bytes, "application/octet-stream", f.OriginalName)
        } :> Task<IResult>
    )) |> ignore

    app.MapControllerRoute(
        name = "default",
        pattern = "{controller=Home}/{action=Index}/{id?}") |> ignore

let [<EntryPoint>] main args =
    let resolveContentRoot () =
        let rec findUp (dir: DirectoryInfo) (predicate: DirectoryInfo -> bool) =
            if isNull dir then
                None
            elif predicate dir then
                Some dir.FullName
            else
                findUp dir.Parent predicate
        let baseDir = DirectoryInfo(AppContext.BaseDirectory)
        let envRoot = findUp baseDir (fun d -> File.Exists(Path.Combine(d.FullName, ".env")))
        let viewsRoot = findUp baseDir (fun d -> Directory.Exists(Path.Combine(d.FullName, "Views")))
        match envRoot, viewsRoot with
        | Some path, _ -> path
        | None, Some path -> path
        | None, None -> AppContext.BaseDirectory
    let contentRoot = resolveContentRoot()
    loadDotEnv (Path.Combine(contentRoot, ".env"))
    let options = WebApplicationOptions(ContentRootPath = contentRoot, Args = args)
    let builder = WebApplication.CreateBuilder(options)
    configureServices builder
    let app = builder.Build()
    use scope = app.Services.CreateScope()
    let db = scope.ServiceProvider.GetRequiredService<CtfdDbContext>()
    if db.Database.GetMigrations().Any() then
        db.Database.Migrate() |> ignore
    else
        db.Database.EnsureCreated() |> ignore

    let ensureTeamTables () =
        // Teams テーブル（Token列付き）
        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"Teams\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL, \"Token\" uuid NOT NULL DEFAULT gen_random_uuid(), \"JoinPassword\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamptz NOT NULL);") |> ignore
        // TeamMembers テーブル（Role列付き）
        db.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"TeamMembers\" (\"Id\" uuid PRIMARY KEY, \"TeamId\" uuid NOT NULL, \"UserId\" uuid NOT NULL, \"JoinedAt\" timestamptz NOT NULL, \"Role\" text NOT NULL DEFAULT 'Player');") |> ignore
        // Token列がなければ追加（レガシースキーマ移行）
        db.Database.ExecuteSqlRaw(
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Teams' AND column_name='Token') THEN ALTER TABLE \"Teams\" ADD COLUMN \"Token\" uuid NOT NULL DEFAULT gen_random_uuid(); END IF; END $$;") |> ignore
        // JoinPassword列がなければ追加（レガシースキーマ移行）
        db.Database.ExecuteSqlRaw(
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Teams' AND column_name='JoinPassword') THEN ALTER TABLE \"Teams\" ADD COLUMN \"JoinPassword\" text NOT NULL DEFAULT ''; END IF; END $$;") |> ignore
        // Role列がなければ追加し、レガシーIsOwner列から移行
        db.Database.ExecuteSqlRaw(
            "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='TeamMembers' AND column_name='Role') THEN ALTER TABLE \"TeamMembers\" ADD COLUMN \"Role\" text NOT NULL DEFAULT 'Player'; END IF; END $$;") |> ignore
        db.Database.ExecuteSqlRaw(
            "DO $$ BEGIN IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='TeamMembers' AND column_name='IsOwner') THEN UPDATE \"TeamMembers\" SET \"Role\" = 'Owner' WHERE \"IsOwner\" = true AND \"Role\" = 'Player'; ALTER TABLE \"TeamMembers\" DROP COLUMN \"IsOwner\"; END IF; END $$;") |> ignore
        // インデックス
        db.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Teams_Name\" ON \"Teams\" (\"Name\");") |> ignore
        db.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Teams_Token\" ON \"Teams\" (\"Token\");") |> ignore
        db.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS \"IX_TeamMembers_UserId\" ON \"TeamMembers\" (\"UserId\");") |> ignore
        db.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TeamMembers_TeamId_UserId\" ON \"TeamMembers\" (\"TeamId\", \"UserId\");") |> ignore

    ensureTeamTables ()

    // ChallengeFiles テーブル作成
    db.Database.ExecuteSqlRaw(
        "CREATE TABLE IF NOT EXISTS \"ChallengeFiles\" (\"Id\" uuid PRIMARY KEY, \"ChallengeId\" uuid NOT NULL, \"OriginalName\" text NOT NULL, \"StoredName\" text NOT NULL, \"UploadedAt\" timestamptz NOT NULL);") |> ignore

    // CtfSettings テーブル作成
    db.Database.ExecuteSqlRaw(
        "CREATE TABLE IF NOT EXISTS \"CtfSettings\" (\"Id\" uuid PRIMARY KEY, \"EventStart\" timestamptz, \"EventEnd\" timestamptz);") |> ignore
    // Flags の ContentHash ユニーク制約を複合ユニーク(ChallengeId, ContentHash)に変更
    db.Database.ExecuteSqlRaw(
        "DROP INDEX IF EXISTS \"IX_Flags_ContentHash\";") |> ignore
    db.Database.ExecuteSqlRaw(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Flags_ChallengeId_ContentHash\" ON \"Flags\" (\"ChallengeId\", \"ContentHash\");") |> ignore
    // Flags に Content列がなければ追加（元のフラグ文字列を保持）
    db.Database.ExecuteSqlRaw(
        "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Flags' AND column_name='Content') THEN ALTER TABLE \"Flags\" ADD COLUMN \"Content\" text NOT NULL DEFAULT ''; END IF; END $$;") |> ignore
    // ThemePreset列がなければ追加（レガシースキーマ移行）
    db.Database.ExecuteSqlRaw(
        "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='CtfSettings' AND column_name='ThemePreset') THEN ALTER TABLE \"CtfSettings\" ADD COLUMN \"ThemePreset\" text NOT NULL DEFAULT 'purple-network'; END IF; END $$;") |> ignore
    // Challenges に ReleaseAt / Difficulty 列を追加（レガシースキーマ移行）
    db.Database.ExecuteSqlRaw(
        "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Challenges' AND column_name='ReleaseAt') THEN ALTER TABLE \"Challenges\" ADD COLUMN \"ReleaseAt\" timestamptz; END IF; END $$;") |> ignore
    db.Database.ExecuteSqlRaw(
        "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Challenges' AND column_name='Difficulty') THEN ALTER TABLE \"Challenges\" ADD COLUMN \"Difficulty\" text NOT NULL DEFAULT ''; END IF; END $$;") |> ignore
    // ChallengeCategories / ChallengeDifficulties マスタテーブル
    db.Database.ExecuteSqlRaw(
        "CREATE TABLE IF NOT EXISTS \"ChallengeCategories\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL, \"SortOrder\" integer NOT NULL DEFAULT 0);") |> ignore
    db.Database.ExecuteSqlRaw(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_ChallengeCategories_Name\" ON \"ChallengeCategories\" (\"Name\");") |> ignore
    db.Database.ExecuteSqlRaw(
        "CREATE TABLE IF NOT EXISTS \"ChallengeDifficulties\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL, \"SortOrder\" integer NOT NULL DEFAULT 0);") |> ignore
    db.Database.ExecuteSqlRaw(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_ChallengeDifficulties_Name\" ON \"ChallengeDifficulties\" (\"Name\");") |> ignore

    // カテゴリ・難易度の初期データ
    let seedMasters () =
        task {
            let! catCount = db.ChallengeCategories.CountAsync()
            if catCount = 0 then
                let categories =
                    [| ("Crypto", 0); ("Forensics", 1); ("Misc", 2); ("Pwn", 3); ("Reversing", 4); ("Web", 5) |]
                for (name, order) in categories do
                    db.ChallengeCategories.Add({ Id = Guid.NewGuid(); Name = name; SortOrder = order }) |> ignore
                let! _ = db.SaveChangesAsync()
                Console.WriteLine("Seeded default categories.")
            let! diffCount = db.ChallengeDifficulties.CountAsync()
            if diffCount = 0 then
                let difficulties =
                    [| ("Warmup", 0); ("Easy", 1); ("Medium", 2); ("Hard", 3); ("Insane", 4) |]
                for (name, order) in difficulties do
                    db.ChallengeDifficulties.Add({ Id = Guid.NewGuid(); Name = name; SortOrder = order }) |> ignore
                let! _ = db.SaveChangesAsync()
                Console.WriteLine("Seeded default difficulties.")
        }
        |> fun t -> t.GetAwaiter().GetResult()
    seedMasters ()

    let parseBoolEnv (name: string) =
        let value = Environment.GetEnvironmentVariable(name)
        if String.IsNullOrWhiteSpace value then false
        else
            match value.Trim().ToLowerInvariant() with
            | "1" | "true" | "yes" | "y" | "on" -> true
            | _ -> false

    let seedIdentity () =
        task {
            let roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>()
            let userMgr = scope.ServiceProvider.GetRequiredService<UserManager<CtfdUser>>()
            let roles = [| "Admins"; "Player"; "AI" |]
            for roleName in roles do
                let! exists = roleMgr.RoleExistsAsync(roleName)
                if not exists then
                    let! created = roleMgr.CreateAsync(IdentityRole<Guid>(Name = roleName))
                    if not created.Succeeded then
                        Console.WriteLine($"Role create failed: {roleName}")

            let adminEmail = Environment.GetEnvironmentVariable("SHINOGI_ADMIN_EMAIL")
            let adminPassword = Environment.GetEnvironmentVariable("SHINOGI_ADMIN_PASSWORD")
            let adminDisplay = Environment.GetEnvironmentVariable("SHINOGI_ADMIN_DISPLAYNAME")
            let resetPassword = parseBoolEnv "SHINOGI_ADMIN_RESET_PASSWORD"
            if not (String.IsNullOrWhiteSpace adminEmail) && not (String.IsNullOrWhiteSpace adminPassword) then
                let! existing = userMgr.FindByEmailAsync(adminEmail)
                let mutable user = existing
                if isNull user then
                    let newUser =
                        CtfdUser(
                            Email = adminEmail,
                            UserName = adminEmail,
                            DisplayName = (if String.IsNullOrWhiteSpace adminDisplay then adminEmail else adminDisplay),
                            EmailConfirmed = true)
                    let! created = userMgr.CreateAsync(newUser, adminPassword)
                    if created.Succeeded then
                        user <- newUser
                    else
                        user <- null
                        let errors = created.Errors |> Seq.map (fun e -> e.Description) |> String.concat "; "
                        Console.WriteLine($"Admin create failed: {errors}")
                if not (isNull user) then
                    if not user.EmailConfirmed then
                        user.EmailConfirmed <- true
                        let! _ = userMgr.UpdateAsync(user)
                        ()
                    let! hasRole = userMgr.IsInRoleAsync(user, "Admins")
                    if not hasRole then
                        let! _ = userMgr.AddToRoleAsync(user, "Admins")
                        ()
                    if resetPassword then
                        let! token = userMgr.GeneratePasswordResetTokenAsync(user)
                        let! reset = userMgr.ResetPasswordAsync(user, token, adminPassword)
                        if not reset.Succeeded then
                            let errors = reset.Errors |> Seq.map (fun e -> e.Description) |> String.concat "; "
                            Console.WriteLine($"Admin password reset failed: {errors}")
            else
                Console.WriteLine("Admin seed skipped: SHINOGI_ADMIN_EMAIL or SHINOGI_ADMIN_PASSWORD missing.")
        }
        |> fun t -> t.GetAwaiter().GetResult()
    seedIdentity ()

    // テスト用シーダー（環境変数 SHINOGI_SEED_DATA=true で有効化）
    let seedTestData () =
        if not (parseBoolEnv "SHINOGI_SEED_DATA") then ()
        else
        task {
            let userMgr = scope.ServiceProvider.GetRequiredService<UserManager<CtfdUser>>()
            // 既にシード済みならスキップ
            let! existingTeam = db.Teams.AnyAsync(fun t -> t.Name = "Team Alpha")
            if existingTeam then
                Console.WriteLine("Seed data already exists, skipping.")
            else
                Console.WriteLine("Seeding test data...")

                // --- ユーザー作成 ---
                let seedUsers =
                    [| ("alice@example.com", "Alice", "Password1!")
                       ("bob@example.com", "Bob", "Password1!")
                       ("charlie@example.com", "Charlie", "Password1!")
                       ("dave@example.com", "Dave", "Password1!")
                       ("eve@example.com", "Eve", "Password1!")
                       ("frank@example.com", "Frank", "Password1!") |]

                let userIds = System.Collections.Generic.List<Guid * string>()
                for (email, displayName, password) in seedUsers do
                    let! existing = userMgr.FindByEmailAsync(email)
                    if isNull existing then
                        let newUser = CtfdUser(Email = email, UserName = email, DisplayName = displayName, EmailConfirmed = true)
                        let! created = userMgr.CreateAsync(newUser, password)
                        if created.Succeeded then
                            let! _ = userMgr.AddToRoleAsync(newUser, "Player")
                            userIds.Add((newUser.Id, displayName))
                            Console.WriteLine($"  User created: {displayName} ({email})")
                        else
                            let errs = created.Errors |> Seq.map (fun e -> e.Description) |> String.concat "; "
                            Console.WriteLine($"  User create failed: {displayName} - {errs}")
                    else
                        userIds.Add((existing.Id, displayName))

                if userIds.Count >= 6 then
                    // --- AIユーザー作成 ---
                    let! aiExisting = userMgr.FindByEmailAsync("ai-agent@example.com")
                    let mutable aiUserId = Guid.Empty
                    if isNull aiExisting then
                        let aiUser = CtfdUser(Email = "ai-agent@example.com", UserName = "ai-agent@example.com", DisplayName = "AI Agent", EmailConfirmed = true)
                        let! created = userMgr.CreateAsync(aiUser, "Password1!")
                        if created.Succeeded then
                            let! _ = userMgr.AddToRoleAsync(aiUser, "AI")
                            aiUserId <- aiUser.Id
                            Console.WriteLine("  User created: AI Agent")
                    else
                        aiUserId <- aiExisting.Id

                    // --- チーム作成 ---
                    let teamAlpha =
                        { Id = Guid.NewGuid(); Name = "Team Alpha"; Token = Guid.NewGuid(); JoinPassword = ""; CreatedAt = DateTimeOffset.UtcNow }
                    let teamBravo =
                        { Id = Guid.NewGuid(); Name = "Team Bravo"; Token = Guid.NewGuid(); JoinPassword = ""; CreatedAt = DateTimeOffset.UtcNow }
                    let teamCharlie =
                        { Id = Guid.NewGuid(); Name = "Team Charlie"; Token = Guid.NewGuid(); JoinPassword = ""; CreatedAt = DateTimeOffset.UtcNow }
                    db.Teams.Add(teamAlpha) |> ignore
                    db.Teams.Add(teamBravo) |> ignore
                    db.Teams.Add(teamCharlie) |> ignore

                    // --- チームメンバー割当 ---
                    // Team Alpha: Alice(Owner), Bob(Player), AI Agent(AI)
                    let memberAlphaOwner =
                        { Id = Guid.NewGuid(); TeamId = teamAlpha.Id; UserId = fst userIds.[0]; JoinedAt = DateTimeOffset.UtcNow; Role = MemberRole.Owner }
                    let memberAlphaPlayer =
                        { Id = Guid.NewGuid(); TeamId = teamAlpha.Id; UserId = fst userIds.[1]; JoinedAt = DateTimeOffset.UtcNow; Role = MemberRole.Player }
                    db.TeamMembers.Add(memberAlphaOwner) |> ignore
                    db.TeamMembers.Add(memberAlphaPlayer) |> ignore
                    if aiUserId <> Guid.Empty then
                        let memberAlphaAI =
                            { Id = Guid.NewGuid(); TeamId = teamAlpha.Id; UserId = aiUserId; JoinedAt = DateTimeOffset.UtcNow; Role = MemberRole.AI }
                        db.TeamMembers.Add(memberAlphaAI) |> ignore

                    // Team Bravo: Charlie(Owner), Dave(Player)
                    let memberBravoOwner =
                        { Id = Guid.NewGuid(); TeamId = teamBravo.Id; UserId = fst userIds.[2]; JoinedAt = DateTimeOffset.UtcNow; Role = MemberRole.Owner }
                    let memberBravoPlayer =
                        { Id = Guid.NewGuid(); TeamId = teamBravo.Id; UserId = fst userIds.[3]; JoinedAt = DateTimeOffset.UtcNow; Role = MemberRole.Player }
                    db.TeamMembers.Add(memberBravoOwner) |> ignore
                    db.TeamMembers.Add(memberBravoPlayer) |> ignore

                    // Team Charlie: Eve(Owner), Frank(Player)
                    let memberCharlieOwner =
                        { Id = Guid.NewGuid(); TeamId = teamCharlie.Id; UserId = fst userIds.[4]; JoinedAt = DateTimeOffset.UtcNow; Role = MemberRole.Owner }
                    let memberCharliePlayer =
                        { Id = Guid.NewGuid(); TeamId = teamCharlie.Id; UserId = fst userIds.[5]; JoinedAt = DateTimeOffset.UtcNow; Role = MemberRole.Player }
                    db.TeamMembers.Add(memberCharlieOwner) |> ignore
                    db.TeamMembers.Add(memberCharliePlayer) |> ignore

                    let! _ = db.SaveChangesAsync()
                    Console.WriteLine($"  Teams created: Alpha(token={teamAlpha.Token}), Bravo(token={teamBravo.Token}), Charlie(token={teamCharlie.Token})")

                    // --- チャレンジ＆フラグ作成 ---
                    let challenges =
                        [| ("Basic Reversing", "Reversing", "Easy", "バイナリを解析してフラグを見つけてください。", 500, 100, 20, ScoreFunction.Linear, "flag{hello_reversing}")
                           ("Web Login Bypass", "Web", "Medium", "ログイン画面の脆弱性を見つけてください。", 300, 50, 15, ScoreFunction.Log, "flag{sql_injection_101}")
                           ("Crypto Basics", "Crypto", "Hard", "暗号文を復号してフラグを取得してください。", 400, 80, 25, ScoreFunction.Exp, "flag{caesar_is_easy}") |]
                    let challengeIds = System.Collections.Generic.List<Guid>()
                    for (name, category, difficulty, desc, initial, minimum, decay, func, flagContent) in challenges do
                        let challenge =
                            { Id = Guid.NewGuid()
                              Name = name
                              Category = category
                              Difficulty = difficulty
                              Description = desc
                              ValueInitial = initial
                              ValueMinimum = minimum
                              Decay = decay
                              Function = func
                              Logic = ChallengeLogic.Any
                              MaxAttempts = None
                              Published = true
                              ReleaseAt = None
                              CreatedAt = DateTimeOffset.UtcNow }
                        db.Challenges.Add(challenge) |> ignore
                        let flag =
                            { Id = Guid.NewGuid()
                              ChallengeId = challenge.Id
                              Content = flagContent
                              ContentHash = Security.sha256 flagContent
                              CaseSensitive = true }
                        db.Flags.Add(flag) |> ignore
                        challengeIds.Add(challenge.Id)
                        Console.WriteLine($"  Challenge created: {name} ({category})")

                    let! _ = db.SaveChangesAsync()

                    // --- サンプル提出データ（スコアボードグラフ用） ---
                    let baseTime = DateTimeOffset.UtcNow.AddHours(-6.0)
                    let sampleSubmissions =
                        [| // Team Alpha の提出
                           (fst userIds.[0], challengeIds.[0], baseTime.AddMinutes(30.0), true, 450)
                           (fst userIds.[1], challengeIds.[1], baseTime.AddHours(1.0), true, 280)
                           (fst userIds.[0], challengeIds.[2], baseTime.AddHours(2.5), true, 370)
                           // Team Bravo の提出
                           (fst userIds.[2], challengeIds.[1], baseTime.AddMinutes(45.0), true, 290)
                           (fst userIds.[3], challengeIds.[0], baseTime.AddHours(1.5), true, 430)
                           (fst userIds.[2], challengeIds.[2], baseTime.AddHours(3.0), true, 350)
                           // Team Charlie の提出
                           (fst userIds.[4], challengeIds.[0], baseTime.AddHours(2.0), true, 410)
                           (fst userIds.[5], challengeIds.[2], baseTime.AddHours(4.0), true, 330) |]

                    for (userId, challengeId, time, correct, score) in sampleSubmissions do
                        let sub =
                            { Id = Guid.NewGuid()
                              AccountId = userId
                              ChallengeId = challengeId
                              SubmittedAt = time
                              IsCorrect = correct
                              ValueAwarded = score
                              Ip = "127.0.0.1" }
                        db.Submissions.Add(sub) |> ignore

                    let! _ = db.SaveChangesAsync()
                    Console.WriteLine("  Sample submissions created.")
                    Console.WriteLine("Seed data completed.")
        }
        |> fun t -> t.GetAwaiter().GetResult()
    seedTestData ()

    mapRoutes app
    app.Run()
    0





