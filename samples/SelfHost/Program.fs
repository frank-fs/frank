// Learn more about F# at http://fsharp.net

#if INTERACTIVE
#r "System"
#r "System.Core"
#r "System.ServiceModel"
#r "System.ServiceModel.Web"
#r @"..\..\packages\FSharpx.Core.1.4.120213\lib\FSharpx.Core.dll"
#r @"..\..\packages\FSharpx.Core.1.4.120213\lib\FSharpx.Http.dll"
#r @"..\..\packages\System.Json.4.0.20126.16343\lib\net40\System.Json.dll"
#r @"..\..\packages\System.Net.Http.2.0.20126.16343\lib\net40\System.Net.Http.dll"
#r @"..\..\packages\System.Net.Http.2.0.20126.16343\lib\net40\System.Net.Http.WebRequest.dll"
#r @"..\..\packages\System.Net.Http.Formatting.4.0.20126.16343\lib\net40\System.Net.Http.Formatting.dll"
#r @"..\..\packages\AspNetWebApi.Core.4.0.20126.16343\lib\net40\System.Web.Http.dll"
#r @"..\..\packages\System.Web.Http.Common.4.0.20126.16343\lib\net40\System.Web.Http.Common.dll"
#r @"..\..\packages\AspNetWebApi.SelfHost.4.0.20126.16343\lib\net40\System.Web.Http.SelfHost.dll"
#r @"..\..\packages\ImpromptuInterface.5.6.6\lib\net40\ImpromptuInterface.dll"
#r @"..\..\packages\ImpromptuInterface.FSharp.1.1.0\lib\net40\ImpromptuInterface.FSharp.dll"
#load @"..\..\src\System.Net.Http.fs"
#load @"..\..\src\Frank.fs"
#load @"..\..\src\Middleware.fs"
#load @"..\..\src\System.Web.Http.fs"
#endif

open System
open System.Globalization
open System.Json
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Web.Http
open System.Web.Http.SelfHost
open Frank
open ImpromptuInterface.FSharp

module Model =
  type ContactId = int

  type Contact =
    { ContactId: ContactId
      Name: string
      Address: string
      City: string
      State: string
      Zip: string
      Email: string
      Twitter: string
    }
    with
    member x.Self = String.Format(CultureInfo.CurrentCulture, "contact/{0}", x.ContactId)

module Data =
  open Model

  type internal ContactsRepositoryMessage
    = GetAll of AsyncReplyChannel<Contact list>
    | Get    of ContactId * AsyncReplyChannel<Contact option>
    | Post   of Contact * AsyncReplyChannel<unit>
    | Update of Contact * AsyncReplyChannel<unit>
    | Delete of ContactId * AsyncReplyChannel<unit>

  type ContactsRepository() =
    let agent = MailboxProcessor<_>.Start(fun inbox ->
      let rec loop(contacts, nextId) = async {
        let! msg = inbox.Receive()
        match msg with
        | GetAll(reply) ->
            reply.Reply(contacts)
            return! loop(contacts, nextId)
        | Get(id, reply) ->
            reply.Reply(contacts |> List.tryFind (fun c -> c.ContactId = id))
            return! loop(contacts, nextId)
        | Post(contact, reply) ->
            reply.Reply()
            return! loop({ contact with ContactId = nextId }::contacts, nextId + 1)
        | Update(contact, reply) ->
            reply.Reply()
            return! loop(let contacts = contacts |> List.filter (fun c -> c.ContactId <> contact.ContactId) in contact::contacts, nextId)
        | Delete(id, reply) ->
            reply.Reply()
            return! loop(contacts |> List.filter (fun c -> c.ContactId <> id), nextId)
      }
      loop([], 1)
    )
    member x.AsyncGetAll() = agent.PostAndAsyncReply(GetAll)
    member x.AsyncGet(id) = agent.PostAndAsyncReply(fun ch -> Get(id, ch))
    member x.AsyncPost(contact) = agent.PostAndAsyncReply(fun ch -> Post(contact, ch))
    member x.AsyncUpdate(contact) = agent.PostAndAsyncReply(fun ch -> Update(contact, ch))
    member x.AsyncDelete(id) = agent.PostAndAsyncReply(fun ch -> Delete(id, ch))
    member x.GetAll() = agent.PostAndReply(GetAll)
    member x.Get(id) = agent.PostAndReply(fun ch -> Get(id, ch))
    member x.Post(contact) = agent.PostAndReply(fun ch -> Post(contact, ch))
    member x.Update(contact) = agent.PostAndReply(fun ch -> Update(contact, ch))
    member x.Delete(id) = agent.PostAndReply(fun ch -> Delete(id, ch))

  // load initial data
  let contacts = ContactsRepository()
  contacts.Post({ ContactId = 0; Name = "Glenn Block"; Address = "1 Microsoft Way"; City = "Redmond"; State = "Washington"; Zip = "98112"; Email = "gblock@microsoft.com"; Twitter = "gblock" })
  contacts.Post({ ContactId = 0; Name = "Howard Dierking"; Address = "1 Microsoft Way"; City = "Redmond"; State = "Washington"; Zip = "98112"; Email = "howard@microsoft.com"; Twitter = "howard_dierking" })
  contacts.Post({ ContactId = 0; Name = "Yavor Georgiev"; Address = "1 Microsoft Way"; City = "Redmond"; State = "Washington"; Zip = "98112"; Email = "yavorg@microsoft.com"; Twitter = "digthepony" })
  contacts.Post({ ContactId = 0; Name = "Jeff Handley"; Address = "1 Microsoft Way"; City = "Redmond"; State = "Washington"; Zip = "98112"; Email = "jeff.handley@microsoft.com"; Twitter = "jeffhandley" })
  contacts.Post({ ContactId = 0; Name = "Deepesh Mohnani"; Address = "1 Microsoft Way"; City = "Redmond"; State = "Washington"; Zip = "98112"; Email = "deepm@microsoft.com"; Twitter = "deepeshm" })
  contacts.Post({ ContactId = 0; Name = "Brad Olenick"; Address = "1 Microsoft Way"; City = "Redmond"; State = "Washington"; Zip = "98112"; Email = "brado@microsoft.com"; Twitter = "brado_23" })
  contacts.Post({ ContactId = 0; Name = "Ron Jacobs"; Address = "1 Microsoft Way"; City = "Redmond"; State = "Washington"; Zip = "98112"; Email = "rojacobs@microsoft.com"; Twitter = "ronljacobs" })

module Formatters =
  open Model

  type ContactPngFormatter() as x =
    inherit BufferedMediaTypeFormatter()
    do x.SupportedMediaTypes.Add(new MediaTypeHeaderValue("image/png"))
    override x.CanWriteType(type') = true
    override x.OnWriteToStream(type', value, stream, contentHeaders, formatterContext, context) = ()
//      match value with
//      | :? Contact as contact ->
//        let imageId = contact.ContactId % 8
//      | _ -> ()

  type VCardFormatter() as x =
    inherit BufferedMediaTypeFormatter()
    do x.SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/directory"))
    override x.CanWriteType(type') = true
    override x.OnWriteToStream(type', value, stream, contentHeaders, formatterContext, context) = ()

module Resources =
  open Model

  // Common formatters
  let formatters = [| new JsonMediaTypeFormatter() :> MediaTypeFormatter
                      new XmlMediaTypeFormatter() :> MediaTypeFormatter
                      new FormUrlEncodedMediaTypeFormatter() :> MediaTypeFormatter |]
  
  // Respond with a web page containing "Hello, world!" and a form submission to use the POST method of the resource.
  let helloWorld request = async {
    return HttpResponseMessage.ReplyTo(request, new StringContent(@"<!doctype html>
<meta charset=utf-8>
<title>Hello</title>
<p>Hello, world!
<form action=""/"" method=""post"">
<input type=""hidden"" name=""text"" value=""testing"">
<input type=""submit"">", System.Text.Encoding.UTF8, "text/html"), ``Content-Type`` "text/html")
  }

  let helloResource = route "/" <| get helloWorld

  (* Contacts resource *)

  let all = negotiateMediaType formatters <| fun _ -> Data.contacts.AsyncGetAll()

  let create (request: HttpRequestMessage) = async {
    let! value = request.Content.AsyncReadAs<JsonValue>()
    let formData = value.AsDynamic()
    let contact =
      { ContactId = 0
        Name = formData?name
        Address = formData?address
        City = formData?city
        State = formData?state
        Zip = formData?zip
        Email = formData?email
        Twitter = formData?twitter
      }
    do! Data.contacts.AsyncPost(contact)
    return HttpResponseMessage.ReplyTo(request, body = contact, statusCode = HttpStatusCode.Created, formatters = formatters)
  }

  let contacts = route "/contacts" (get all <|> post create)

  (* Contact resource *)

  let single (request: HttpRequestMessage) = async {
    let path = request.RequestUri.Segments
    let id = int (path.[path.Length - 1])
    match Data.contacts.Get id with
    | Some contact -> return HttpResponseMessage.ReplyTo(request, contact, HttpStatusCode.OK, formatters)
    | _ -> return new HttpResponseMessage(HttpStatusCode.NotFound)
  }

  let update request = async {
    return new HttpResponseMessage()
  }

  let old request = async {
    return new HttpResponseMessage()
  }

  let contact = routeTemplate @"/contact/(\d+)" (fun template request -> let result = FSharpx.Regex.tryMatch template request.RequestUri.AbsolutePath in result.IsSome) (get single <|> put update <|> delete old)

(* Configure and run the application *)

let app = merge [ Resources.helloResource; Resources.contacts; Resources.contact ] |> Middleware.log

let baseUri = "http://127.0.0.1:1000"
let config = new HttpSelfHostConfiguration(baseUri)
config.Register app

let server = new HttpSelfHostServer(config)
server.OpenAsync().Wait()

Console.WriteLine("Running on " + baseUri)
Console.WriteLine("Press any key to stop.")
Console.ReadKey() |> ignore

server.CloseAsync().Wait()
