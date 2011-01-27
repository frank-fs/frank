namespace Frack.Collections

/// Extensions to dictionaries.
module Dict =
  open System.Collections.Generic
  let empty<'a, 'b when 'a : equality> : IDictionary<'a, 'b> = dict Seq.empty
  let toSeq d = d |> Seq.map (fun (KeyValue(k,v)) -> (k,v))
  let toArray (d:IDictionary<_,_>) = d |> toSeq |> Seq.toArray
  let toList (d:IDictionary<_,_>) = d |> toSeq |> Seq.toList