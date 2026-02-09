namespace Shinogi.Services

open System
open System.Security.Cryptography
open System.Text

module Security =
  let sha256 (text: string) =
    let normalized = (if isNull text then "" else text.Trim()).Normalize()
    using (SHA256.Create()) (fun sha ->
      normalized
      |> Encoding.UTF8.GetBytes
      |> sha.ComputeHash
      |> Array.map (fun b -> b.ToString("x2"))
      |> String.concat "")
