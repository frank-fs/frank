namespace Frack
open System
open System.Collections.Generic
open Owin

[<AbstractClass>]
type Application =
  /// <summary>Creates an Owin.IApplication.</summary>
  static member Create(beginInvoke:Func<_,_,_,_>, endInvoke:Func<_,_>) =
    { new Owin.IApplication with
        member this.BeginInvoke(request, callback, state) = beginInvoke.Invoke(request, callback, state)
        member this.EndInvoke(result) = endInvoke.Invoke(result) }

  /// <summary>Creates an Owin.IApplication.</summary>
  static member Create(beginInvoke:IRequest * AsyncCallback * obj -> IAsyncResult, endInvoke:IAsyncResult -> IResponse) = 
    Application.Create(Func<_,_,_,_>(fun r cb s -> beginInvoke(r,cb,s)), Func<_,_>(endInvoke))

  /// <summary>Creates an Owin.IApplication.</summary>
  static member Create(invoke:Func<_,_>) =
    Application.Create(invoke.BeginInvoke, invoke.EndInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  static member Create(asyncInvoke: IRequest -> Async<IResponse>) =
    let beginInvoke, endInvoke, cancelInvoke = Async.AsBeginEnd(asyncInvoke)
    Application.Create(beginInvoke, endInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  static member Create(invoke: IRequest -> IResponse) =
    let asyncInvoke req = async { return invoke req }
    Application.Create(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  static member Create(asyncInvoke: IRequest -> Async<string * IDictionary<string, seq<string>> * 'a>) =
    let asyncInvoke req = async {
      let! (status, headers, body) = asyncInvoke req
      return Response.Create(status, headers, body) }
    Application.Create(asyncInvoke)

  /// <summary>Creates an Owin.IApplication.</summary>
  static member Create(invoke: IRequest -> string * IDictionary<string, seq<string>> * 'a) =
    let asyncInvoke req = async {
      let (status, headers, body) = invoke req
      return Response.Create(status, headers, body) }
    Application.Create(asyncInvoke)
