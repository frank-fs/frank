open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Text
open Frank
open FSharpx
open FSharpx.Reader
open Newtonsoft.Json.Linq
open NUnit.Framework 
open Swensen.Unquote.Assertions

[<Test>]
let ``test respond without body``() =
    let response = new HttpRequestMessage() |> respond HttpStatusCode.OK ignore None
    test <@ response.StatusCode = HttpStatusCode.OK @>
    test <@ response.Content = Unchecked.defaultof<HttpContent> @>

[<Test>]
let ``test respond with StringContent``() =
    let body = "Howdy"
    let response = new HttpRequestMessage() |> OK ignore (Some(new StringContent(body)))
    test <@ response.StatusCode = HttpStatusCode.OK @>
    test <@ response.Content.ReadAsStringAsync().Result = body @>

[<Test>]
let ``test respond with negotiated body``() =
    let body = "Howdy"
    let response = new HttpRequestMessage() |> OK ignore (Some(new ObjectContent<_>(body, new XmlMediaTypeFormatter(), "text/plain")))
    test <@ response.StatusCode = HttpStatusCode.OK @>
    test <@ response.Content.ReadAsStringAsync().Result = @"<string xmlns=""http://schemas.microsoft.com/2003/10/Serialization/"">Howdy</string>" @>

[<Test>]
let ``test options``() =
    let response = options [HttpMethod.Get; HttpMethod.Post] (new HttpRequestMessage()) |> Async.RunSynchronously
    test <@ response.StatusCode = HttpStatusCode.OK @>
    test <@ response.Content.Headers.Allow.Contains("GET") @>
    test <@ response.Content.Headers.Allow.Contains("POST") @>
    test <@ not <| response.Content.Headers.Allow.Contains("PUT") @>
    test <@ not <| response.Content.Headers.Allow.Contains("DELETE") @>

[<Test>]
let ``test 405 Method Not Allowed``() =
    let response = ``405 Method Not Allowed`` [HttpMethod.Get; HttpMethod.Post] (new HttpRequestMessage()) |> Async.RunSynchronously
    test <@ response.StatusCode = HttpStatusCode.MethodNotAllowed @>
    test <@ response.Content.Headers.Allow.Contains("GET") @>
    test <@ response.Content.Headers.Allow.Contains("POST") @>
    test <@ not <| response.Content.Headers.Allow.Contains("PUT") @>
    test <@ not <| response.Content.Headers.Allow.Contains("DELETE") @>

[<Test>]
let ``test 406 Not Acceptable``() =
    let response = ``406 Not Acceptable`` <| new HttpRequestMessage() |> Async.RunSynchronously
    test <@ response.StatusCode = HttpStatusCode.NotAcceptable @>

type TestType() =
    let mutable firstName = ""
    let mutable lastName = ""
    member x.FirstName
        with get() = firstName
        and set(v) = firstName <- v
    member x.LastName
        with get() = lastName
        and set(v) = lastName <- v
    override x.ToString() = firstName + " " + lastName

[<Test>]
let ``test formatWith properly format as application/json raw``() =
    let formatter = new System.Net.Http.Formatting.JsonMediaTypeFormatter()
    let body = TestType(FirstName = "Ryan", LastName = "Riley")
    let content = new ObjectContent<_>(body, formatter, "application/json") :> HttpContent
    test <@ content.Headers.ContentType.MediaType = "application/json" @>
    let result = content.ReadAsStringAsync()
                             |> Async.AwaitTask
                             |> Async.RunSynchronously
    test <@ result = "{\"firstName\":\"Ryan\",\"lastName\":\"Riley\"}" @>

[<Test>]
let ``test formatWith properly format as application/json``() =
    let formatter = new System.Net.Http.Formatting.JsonMediaTypeFormatter()
    let body = TestType(FirstName = "Ryan", LastName = "Riley")
    let content = body |> formatWith "application/json" formatter
    test <@ content.Headers.ContentType.MediaType = "application/json" @>
    let result = content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
    test <@ result = "{\"firstName\":\"Ryan\",\"lastName\":\"Riley\"}" @>

[<Test>]
let ``test formatWith properly format as application/xml``() =
    let formatter = new System.Net.Http.Formatting.XmlMediaTypeFormatter()
    let body = TestType(FirstName = "Ryan", LastName = "Riley")
    let content = body |> formatWith "application/xml" formatter
    test <@ content.Headers.ContentType.MediaType = "application/xml" @>
    let result = content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
    test <@ result = @"<Program.TestType xmlns:i=""http://www.w3.org/2001/XMLSchema-instance"" xmlns=""http://schemas.datacontract.org/2004/07/""><firstName>Ryan</firstName><lastName>Riley</lastName></Program.TestType>" @>

[<Test>]
let ``test formatWith properly format as application/xml and read as TestType``() =
    let formatter = new System.Net.Http.Formatting.XmlMediaTypeFormatter()
    let body = TestType(FirstName = "Ryan", LastName = "Riley")
    let content = body |> formatWith "application/xml" formatter
    test <@ content.Headers.ContentType.MediaType = "application/xml" @>
    let result = content.ReadAsAsync<TestType>() |> Async.AwaitTask |> Async.RunSynchronously
    test <@ result.FirstName = body.FirstName && result.LastName = body.LastName @>

[<EntryPoint>]
let main args =
    0