namespace Frack
module Response =
  open System.Collections.Generic
  open Microsoft.Http
  open Frack
  
  /// Creates a Frack response from a Microsoft.Http.HttpResponseMessage
  let toFrack (response:HttpResponseMessage) : int * IDictionary<string,string> * bytestring =
    let headers = response.Headers
                    |> Seq.map (fun (KeyValue(k,v)) -> (k, System.String.Join(",",v)))
                    |> dict
    (int response.StatusCode, headers, response.Content.ReadAsByteArray() |> Array.toSeq)
