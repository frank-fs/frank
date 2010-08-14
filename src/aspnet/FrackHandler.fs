namespace Frack.AspNet
open System.Text
open System.Web
open Frack.Env
open Frack.Extensions

type FrackHandler() =
  interface IHttpHandler with
    member this.IsReusable = false // Can I make this reusable?
    member this.ProcessRequest(context) =
      let errors = new StringBuilder()
      let env = createEnvironment (context.ToContextBase()) errors
      env |> ignore // How to pass this to my Frack app?

type FrackHandlerFactory() =
  interface IHttpHandlerFactory with
    member this.GetHandler(context, requestType, url, pathTranslated) = new FrackHandler() :> IHttpHandler
    member this.ReleaseHandler(handler) = handler |> ignore
      