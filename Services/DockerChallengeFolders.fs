namespace Shinogi.Services

open System
open System.IO
open System.Text.RegularExpressions

/// `docker-challenges` 直下のチャレンジフォルダ列挙・パス検証。
module DockerChallengeFolders =

    let private namePattern = Regex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]*$", RegexOptions.CultureInvariant)

    let relativeRoot = "docker-challenges"

    let isValidFolderName (name: string) =
        not (String.IsNullOrWhiteSpace name) && namePattern.IsMatch(name.Trim())

    /// コンテンツルート配下で、Dockerfile または compose があるサブフォルダ名（ソート済み）。
    let list (contentRoot: string) : string list =
        let root = Path.Combine(contentRoot, relativeRoot)
        if not (Directory.Exists root) then
            []
        else
            Directory.GetDirectories(root)
            |> Array.choose (fun full ->
                let name = Path.GetFileName(full)
                if not (isValidFolderName name) then
                    None
                else
                    let hasCompose =
                        File.Exists(Path.Combine(full, "docker-compose.yml"))
                        || File.Exists(Path.Combine(full, "docker-compose.yaml"))
                    let hasDockerfile = File.Exists(Path.Combine(full, "Dockerfile"))
                    if hasCompose || hasDockerfile then Some name else None)
            |> Array.sort
            |> List.ofArray

    /// 解決済みの絶対パス。パストラバーサルや存在しない場合は Error。
    let tryResolveDirectory (contentRoot: string) (folderName: string) : Result<string, string> =
        let name = folderName.Trim()
        if not (isValidFolderName name) then
            Error "無効なチャレンジフォルダ名です。"
        else
            let baseRoot = Path.GetFullPath(Path.Combine(contentRoot, relativeRoot))
            let dir = Path.GetFullPath(Path.Combine(baseRoot, name))
            if not (dir.StartsWith(baseRoot, StringComparison.OrdinalIgnoreCase)) then
                Error "チャレンジディレクトリの解決に失敗しました。"
            elif not (Directory.Exists dir) then
                Error $"チャレンジディレクトリが見つかりません: {name}"
            else
                Ok dir
