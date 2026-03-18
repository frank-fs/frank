module Frank.Cli.Core.Output.AnsiColors

open System

let isColorEnabled () =
    let noColor = Environment.GetEnvironmentVariable("NO_COLOR")
    isNull noColor && Console.IsOutputRedirected |> not

let bold text =
    if isColorEnabled () then sprintf "\033[1m%s\033[0m" text else text

let red text =
    if isColorEnabled () then sprintf "\033[31m%s\033[0m" text else text

let green text =
    if isColorEnabled () then sprintf "\033[32m%s\033[0m" text else text

let yellow text =
    if isColorEnabled () then sprintf "\033[33m%s\033[0m" text else text
