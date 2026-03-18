module internal Frank.Statecharts.Validation.StringDistance

/// Compute Jaro similarity between two strings (0.0 to 1.0).
let jaro (s1: string) (s2: string) : float =
    if s1 = s2 then 1.0
    elif s1.Length = 0 || s2.Length = 0 then 0.0
    else
        let matchWindow = max (max s1.Length s2.Length / 2 - 1) 0
        let s1Matches = Array.create s1.Length false
        let s2Matches = Array.create s2.Length false
        let mutable matches = 0.0
        let mutable transpositions = 0.0
        for i in 0 .. s1.Length - 1 do
            let start = max 0 (i - matchWindow)
            let stop = min (i + matchWindow + 1) s2.Length
            for j in start .. stop - 1 do
                if not s2Matches.[j] && s1.[i] = s2.[j] then
                    s1Matches.[i] <- true
                    s2Matches.[j] <- true
                    matches <- matches + 1.0
        if matches = 0.0 then 0.0
        else
            let mutable k = 0
            for i in 0 .. s1.Length - 1 do
                if s1Matches.[i] then
                    while not s2Matches.[k] do k <- k + 1
                    if s1.[i] <> s2.[k] then transpositions <- transpositions + 1.0
                    k <- k + 1
            (matches / float s1.Length + matches / float s2.Length + (matches - transpositions / 2.0) / matches) / 3.0

/// Compute Jaro-Winkler similarity (0.0 to 1.0).
let jaroWinkler (s1: string) (s2: string) : float =
    let j = jaro s1 s2
    let prefixLength =
        Seq.zip s1 s2
        |> Seq.takeWhile (fun (a, b) -> a = b)
        |> Seq.length
        |> min 4
    j + (float prefixLength * 0.1 * (1.0 - j))
