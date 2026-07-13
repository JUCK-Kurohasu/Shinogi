namespace Shinogi.Services

open System
open Shinogi.Domain

module Scoring =
  let dynamicValue (c: Challenge) (solveCount: int) =
    let clamp v = max c.ValueMinimum v
    match c.Function with
    | ScoreFunction.Linear -> clamp (c.ValueInitial - c.Decay * solveCount)
    | ScoreFunction.Log -> clamp (float c.ValueInitial / Math.Log(float (solveCount + 2)) |> int)
    | ScoreFunction.Exp -> clamp (float c.ValueInitial * Math.Exp(-0.05 * float solveCount) |> int)

  /// ユーザーごとの純スコア（正解提出の合計 − 開放ヒントのコスト合計）。
  /// submissions は IsCorrect のみ渡すこと。凍結処理は呼び出し側でフィルタする。
  let netScoresByAccount (submissions: Submission seq) (unlocks: HintUnlock seq) : Map<Guid, int> =
    let earned =
      submissions
      |> Seq.groupBy (fun s -> s.AccountId)
      |> Seq.map (fun (uid, items) -> uid, items |> Seq.sumBy (fun s -> s.ValueAwarded))
      |> Map.ofSeq
    let spent =
      unlocks
      |> Seq.groupBy (fun u -> u.AccountId)
      |> Seq.map (fun (uid, items) -> uid, items |> Seq.sumBy (fun u -> u.Cost))
      |> Map.ofSeq
    let allIds =
      Set.union
        (earned |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
        (spent |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
    allIds
    |> Seq.map (fun uid ->
        let e = earned |> Map.tryFind uid |> Option.defaultValue 0
        let s = spent |> Map.tryFind uid |> Option.defaultValue 0
        uid, e - s)
    |> Map.ofSeq

  /// 単一ユーザーの純スコア。
  let netScoreOf (scores: Map<Guid, int>) (accountId: Guid) =
    scores |> Map.tryFind accountId |> Option.defaultValue 0
