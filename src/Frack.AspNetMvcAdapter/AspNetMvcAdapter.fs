namespace Frack.Adapters
module AspNetMvcAdapter =
  type FrackMvcAsyncHandler(requestContext) =
    inherit System.Web.Mvc.MvcHandler(requestContext)
    /// Publicly exposes the ProcessRequest member of MvcHandler that takes an HttpContextBase.
    member this.Invoke(context: System.Web.HttpContextBase) =
      base.ProcessRequest(context)
    member this.BeginInvoke(context: System.Web.HttpContextBase, callback: System.AsyncCallback, state: obj) =
      base.BeginProcessRequest(context, callback, state)
    member this.EndInvoke(asyncResult) =
      base.EndProcessRequest(asyncResult)