namespace Frank.Types

[<AutoOpen>]
module ArrayEx =
  let slice start end' (source:'a[]) =
    let e = ref end'
    if !e < 0 then e := source.Length + !e
    let len = !e - start
    [| for i in [0..(len-1)] do yield source.[i + start] |]
