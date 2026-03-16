namespace Frank.Cli.Core.Help

/// Fuzzy string matching for "did you mean?" suggestions.
module FuzzyMatch =

    /// Compute the Levenshtein edit distance between two strings.
    let levenshteinDistance (a: string) (b: string) : int =
        let m = a.Length
        let n = b.Length

        if m = 0 then n
        elif n = 0 then m
        else
            // Single-row DP optimization: O(n) space
            let prev = Array.init (n + 1) id
            let curr = Array.zeroCreate (n + 1)

            for i in 1..m do
                curr.[0] <- i

                for j in 1..n do
                    let cost = if a.[i - 1] = b.[j - 1] then 0 else 1
                    curr.[j] <- min (min (prev.[j] + 1) (curr.[j - 1] + 1)) (prev.[j - 1] + cost)

                // Copy curr to prev for next iteration
                System.Array.Copy(curr, prev, n + 1)

            prev.[n]

    /// Find suggestions from a list of candidates, sorted by distance (closest first).
    /// Includes candidates where: distance <= maxDistance OR input is a prefix of candidate.
    let suggest (input: string) (candidates: string list) (maxDistance: int) : (string * int) list =
        let inputLower = input.ToLowerInvariant()

        candidates
        |> List.map (fun candidate ->
            let candidateLower = candidate.ToLowerInvariant()
            let distance = levenshteinDistance inputLower candidateLower
            let isPrefix = candidateLower.StartsWith(inputLower) && inputLower.Length > 0
            (candidate, distance, isPrefix))
        |> List.filter (fun (_, distance, isPrefix) -> distance <= maxDistance || isPrefix)
        |> List.sortBy (fun (_, distance, _) -> distance)
        |> List.map (fun (candidate, distance, _) -> (candidate, distance))
