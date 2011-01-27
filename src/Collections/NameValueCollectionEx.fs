namespace Frack.Collections

[<AutoOpen>]
[<System.Runtime.CompilerServices.Extension>]
module NameValueCollectionEx =
  /// Extends NameValueCollection with methods to transform it to an enumerable.
  [<System.Runtime.CompilerServices.Extension>]
  let AsEnumerable(this:System.Collections.Specialized.NameValueCollection) =
    seq { for key in this.Keys do yield (key, this.[key]) }

  /// Extends NameValueCollection with methods to transform it to a dictionary.
  [<System.Runtime.CompilerServices.Extension>]
  let ToDictionary(this:System.Collections.Specialized.NameValueCollection) = this |> AsEnumerable |> dict
                                  
  /// Extends NameValueCollection with methods to transform it to an enumerable, map or dictionary.
  type System.Collections.Specialized.NameValueCollection with
    member this.AsEnumerable() = this |> AsEnumerable
    member this.ToDictionary() = this |> ToDictionary
    member this.ToMap() = this.AllKeys |> Array.fold (fun h k -> Map.add k this.[k] h) Map.empty