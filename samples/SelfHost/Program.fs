// Learn more about F# at http://fsharp.net

open System
open System.Globalization
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Web.Http
open System.Web.Http.HttpResource
open System.Web.Http.SelfHost
open Frank
open FSharp.Control
open FSharpx.Option
open Newtonsoft.Json.Linq

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
    override x.CanReadType(type') = false
    override x.CanWriteType(type') = true
    override x.WriteToStream(type', value, stream, contentHeaders) = ()
//      match value with
//      | :? Contact as contact ->
//        let imageId = contact.ContactId % 8
//      | _ -> ()

  type VCardFormatter() as x =
    inherit BufferedMediaTypeFormatter()
    do x.SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/directory"))
    override x.CanReadType(type') = false
    override x.CanWriteType(type') = true
    override x.WriteToStream(type', value, stream, contentHeaders) = ()

  // Common formatters
  let formatters = [| new JsonMediaTypeFormatter() :> MediaTypeFormatter
                      new XmlMediaTypeFormatter() :> MediaTypeFormatter
                      new FormUrlEncodedMediaTypeFormatter() :> MediaTypeFormatter |]

module HelloResource =

  
  // Respond with a web page containing "Hello, world!" and a form submission to use the POST method of the resource.
  let helloWorld request =
    respond HttpStatusCode.OK 
    <| ``Content-Type`` "text/html"
    <| Some(Formatted (@"<!doctype html>
<meta charset=utf-8>
<title>Hello</title>
<p>Hello, world!
<form action=""/"" method=""post"">
<input type=""hidden"" name=""text"" value=""testing"">
<input type=""submit"">", System.Text.Encoding.UTF8, "text/html"))
    <| request
    |> async.Return

  let echo = runConneg Formatters.formatters <| fun request -> request.Content.ReadAsStringAsync() |> Async.AwaitTask
    
  let helloResource = route "/" (get helloWorld <|> post echo)

module ContactsResource =
  open Model

  let all = runConneg Formatters.formatters <| fun _ -> Data.contacts.AsyncGetAll()

  // Dynamic lookup helper for JToken
  let (?) (data: JToken) name = data.SelectToken(name) |> string

  let create (request: HttpRequestMessage) = async {
    let! formData = request.Content.ReadAsAsync<JToken>()
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
    match negotiateMediaType Formatters.formatters request with
    | Some mediaType ->
        let formatter = Formatters.formatters |> Seq.find (fun f -> f.SupportedMediaTypes.Contains(MediaTypeHeaderValue(mediaType)))
        let content = formatWith mediaType formatter contact
        return request |> respond HttpStatusCode.Created ignore (Some content)
    | _ -> return! ``406 Not Acceptable`` request
  }

  let contacts = route "/contacts" (get all <|> post create)

  (* Contact resource *)

  let single (request: HttpRequestMessage) = async {
    let id = getParam<int> request "id" |> Option.get
    let result = maybe {
      let! contact = Data.contacts.Get id
      let! mediaType = negotiateMediaType Formatters.formatters request
      let formatter = Formatters.formatters |> Seq.find (fun f -> f.SupportedMediaTypes.Contains(MediaTypeHeaderValue(mediaType)))
      let content = formatWith mediaType formatter contact
      return request |> respond HttpStatusCode.OK ignore (Some content)
    }
    match result with
    | Some r -> return r
    | _ -> return! ``404 Not Found`` request
  }

  let update request = async {
    return new HttpResponseMessage()
  }

  let old request = async {
    return new HttpResponseMessage()
  }

  let contact = route "/contact/{id}" (get single <|> put update <|> delete old)

(* Configure and run the application *)

let baseUri = "http://127.0.0.1:1000"
let config = new HttpSelfHostConfiguration(baseUri)
config |> register [ HelloResource.helloResource; ContactsResource.contacts; ContactsResource.contact ]

let server = new HttpSelfHostServer(config)
server.OpenAsync().Wait()

Console.WriteLine("Running on " + baseUri)
Console.WriteLine("Press any key to stop.")
Console.ReadKey() |> ignore

server.CloseAsync().Wait()
