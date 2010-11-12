namespace Frack

/// Extensions to the Array module.
module Array =
  /// Slices out a portion of the array from the start index up to the stop index.
  let slice start stop (source:'a[]) =
    let stop' = ref stop
    if !stop' < 0 then stop' := source.Length + !stop'
    let len = !stop' - start
    [| for i in [0..(len-1)] do yield source.[i + start] |]

/// Extensions to dictionaries.
module Dict =
  open System.Collections.Generic

  let toSeq d = d |> Seq.map (fun (KeyValue(k,v)) -> (k,v))
  let toArray (d:IDictionary<_,_>) = d |> toSeq |> Seq.toArray
  let toList (d:IDictionary<_,_>) = d |> toSeq |> Seq.toList

[<AutoOpen>]
module Extensions =
  open System.Collections.Specialized
  open System.Text

  /// Extends System.Collections.Specialized.NameValueCollection with methods to transform it to an enumerable, map or dictionary.
  type NameValueCollection with
    member this.AsEnumerable() = seq { for key in this.Keys do yield (key, this.[key]) }
    member this.ToDictionary() = dict (this.AsEnumerable())
    member this.ToMap() =
      let folder (h:Map<_,_>) (key:string) =
        Map.add key this.[key] h 
      this.AllKeys |> Array.fold (folder) Map.empty
