namespace Frack
open System

module Response =
  /// <summary>Creates an Owin.IResponse.</summary>
  let FromFactory(status, headers, getBody:Func<seq<_>>) =
    { new Owin.IResponse with
        member this.Status = status
        member this.Headers = headers
        member this.GetBody() = getBody.Invoke() |> Seq.map (fun o -> o :> obj) }

  /// <summary>Creates an Owin.IResponse.</summary>
  let FromEnumerable(status, headers, body:seq<_>) = FromFactory(status, headers, Func<_>(fun () -> body)) 

  /// <summary>Creates an Owin.IResponse.</summary>
  let Create(status, headers, body) = FromFactory(status, headers, Func<_>(fun () -> seq { yield body })) 
