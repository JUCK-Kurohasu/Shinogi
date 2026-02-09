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
