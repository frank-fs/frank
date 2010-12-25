namespace Frack

/// Extensions to the Array module.
[<System.Runtime.CompilerServices.Extension>]
module Array =
  open System.Collections.Generic

  /// Slices out a portion of the array from the start index up to the stop index.
  let slice start stop (source:'a[]) =
    let stop' = ref stop
    if !stop' < 0 then stop' := source.Length + !stop'
    let len = !stop' - start
    [| for i in [0..(len-1)] do yield source.[i + start] |]

  [<System.Runtime.CompilerServices.Extension>]
  let Slice(arr, start, stop) = slice start stop arr
