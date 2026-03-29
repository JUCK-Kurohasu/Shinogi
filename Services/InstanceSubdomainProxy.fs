namespace Shinogi.Services

open System
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Shinogi.Data
open Shinogi.Domain
open Yarp.ReverseProxy.Forwarder

/// SHINOGI_INSTANCE_HOST_SUFFIX（例: 127.0.0.1.nip.io）配下のホストをコンテナの HostPort へ YARP で転送する。
module InstanceSubdomainProxy =
    let useInstanceSubdomainProxy (app: WebApplication) =
        let suffixEnv = Environment.GetEnvironmentVariable("SHINOGI_INSTANCE_HOST_SUFFIX")
        if String.IsNullOrWhiteSpace suffixEnv then
            ()
        else
            let suffix = suffixEnv.Trim().ToLowerInvariant()
            app.Use(fun next ->
                RequestDelegate(fun ctx ->
                    task {
                        let host = ctx.Request.Host.Host.ToLowerInvariant()
                        let slugOpt =
                            if not (host.EndsWith("." + suffix, StringComparison.Ordinal)) then
                                None
                            else
                                let subLen = host.Length - suffix.Length - 1
                                if subLen <= 0 then
                                    None
                                else
                                    Some(host.Substring(0, subLen))
                        match slugOpt with
                        | None -> return! next.Invoke(ctx)
                        | Some slug ->
                            let db = ctx.RequestServices.GetRequiredService<CtfdDbContext>()
                            let! inst =
                                db.ChallengeInstances.AsNoTracking()
                                  .FirstOrDefaultAsync(fun i ->
                                      i.AccessSlug = slug && i.Status = InstanceStatus.Running)
                            if isNull inst then
                                ctx.Response.StatusCode <- StatusCodes.Status404NotFound
                                return! ctx.Response.WriteAsync(
                                    "Instance not found or has expired.",
                                    ctx.RequestAborted
                                )
                            else
                                let dest = $"http://127.0.0.1:{inst.HostPort}"
                                let forwarder = ctx.RequestServices.GetRequiredService<IHttpForwarder>()
                                let invoker = ctx.RequestServices.GetRequiredService<HttpMessageInvoker>()
                                let cfg = ForwarderRequestConfig(ActivityTimeout = TimeSpan.FromMinutes 10.)
                                let! ferr =
                                    forwarder.SendAsync(ctx, dest, invoker, cfg, HttpTransformer.Default)
                                if ferr <> ForwarderError.None && not ctx.Response.HasStarted then
                                    ctx.Response.StatusCode <- StatusCodes.Status502BadGateway
                    }
                    :> Task))
            |> ignore
