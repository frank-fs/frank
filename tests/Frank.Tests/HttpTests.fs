module Frank.Tests.Http

open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open Frank.Http
open Newtonsoft.Json.Serialization
open NUnit.Framework 
open Swensen.Unquote.Assertions

[<CLIMutable>]
type TestType =
    { FirstName : string; LastName: string }
    with
    override x.ToString() = x.FirstName + " " + x.LastName

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
    test <@ response.Content.ReadAsStringAsync().Result = """<string xmlns="http://schemas.microsoft.com/2003/10/Serialization/">Howdy</string>""" @>

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

[<Test>]
let ``test formatWith properly format as application/json raw``() =
    let formatter = new System.Net.Http.Formatting.JsonMediaTypeFormatter()
    formatter.SerializerSettings.ContractResolver <- CamelCasePropertyNamesContractResolver()
    let body = { FirstName = "Ryan"; LastName = "Riley" }
    let content = new ObjectContent<_>(body, formatter, "application/json") :> HttpContent
    test <@ content.Headers.ContentType.MediaType = "application/json" @>
    let result = content.ReadAsStringAsync()
                                |> Async.AwaitTask
                                |> Async.RunSynchronously
    test <@ result = """{"firstName":"Ryan","lastName":"Riley"}""" @>

[<Test>]
let ``test formatWith properly format as application/json``() =
    let formatter = new System.Net.Http.Formatting.JsonMediaTypeFormatter()
    formatter.SerializerSettings.ContractResolver <- CamelCasePropertyNamesContractResolver()
    let body = { FirstName = "Ryan"; LastName = "Riley" }
    let content = body |> formatWith "application/json" formatter
    test <@ content.Headers.ContentType.MediaType = "application/json" @>
    let result = content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
    test <@ result = """{"firstName":"Ryan","lastName":"Riley"}""" @>

[<Test>]
let ``test formatWith properly format as application/xml``() =
    let formatter = new System.Net.Http.Formatting.XmlMediaTypeFormatter()
    let body = { FirstName = "Ryan"; LastName = "Riley" }
    let content = body |> formatWith "application/xml" formatter
    test <@ content.Headers.ContentType.MediaType = "application/xml" @>
    let result = content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
    test <@ result = """<Http.TestType xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/Frank.Tests"><FirstName_x0040_>Ryan</FirstName_x0040_><LastName_x0040_>Riley</LastName_x0040_></Http.TestType>""" @>

[<Test>]
let ``test formatWith properly format as application/xml and read as TestType``() =
    let formatter = new System.Net.Http.Formatting.XmlMediaTypeFormatter()
    let body = { FirstName = "Ryan"; LastName = "Riley" }
    let content = body |> formatWith "application/xml" formatter
    test <@ content.Headers.ContentType.MediaType = "application/xml" @>
    let result = content.ReadAsAsync<TestType>() |> Async.AwaitTask |> Async.RunSynchronously
    test <@ result.FirstName = body.FirstName && result.LastName = body.LastName @>

[<Test>]
let ``test readFormUrlEncoded produces a Map<string, string> of the same values``() =
    let pairs = [ "a","b"; "c","d" ]
    use body = Form pairs
    use request = new HttpRequestMessage(HttpMethod.Post, "http://example.org/", Content = body)
    let result = readFormUrlEncoded request.Content |> Async.RunSynchronously
    let expected = Map.ofList pairs
    test <@ result = expected @>
