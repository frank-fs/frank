namespace Frack
module Extensions =
  /// Extends System.Collections.Specialized.NameValueCollection with methods to transform it to a Map or IDictionary.
  type System.Collections.Specialized.NameValueCollection with
    member this.ToMap() =
      let folder (h:Map<string,string>) (key:string) =
        Map.add key this.[key] h 
      this.AllKeys |> Array.fold (folder) Map.empty
  
    member this.ToDictionary() =
      let folder acc (key:string) =
        (key, this.[key]) :: acc 
      this.AllKeys |> Array.fold (folder) List.empty |> dict