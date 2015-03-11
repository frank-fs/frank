﻿namespace Frank

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Text
open Frank
open Newtonsoft.Json.Linq
open Newtonsoft.Json.Serialization
open NUnit.Framework 
open Swensen.Unquote.Assertions

[<CLIMutable>]
type TestType =
    { FirstName : string; LastName: string }
    with
    override x.ToString() = x.FirstName + " " + x.LastName

module Tests =

    [<Test>]
    let ``test respond without body``() =
        let response = new HttpRequestMessage() |> respond HttpStatusCode.OK ignore None
        response.StatusCode =? HttpStatusCode.OK
        response.Content =? Unchecked.defaultof<HttpContent>

    [<Test>]
    let ``test respond with StringContent``() =
        let body = "Howdy"
        let response = new HttpRequestMessage() |> OK ignore (Some(new StringContent(body)))
        response.StatusCode =? HttpStatusCode.OK
        response.Content.ReadAsStringAsync().Result =? body

    [<Test>]
    let ``test respond with negotiated body``() =
        let body = "Howdy"
        let response = new HttpRequestMessage() |> OK ignore (Some(new ObjectContent<_>(body, new XmlMediaTypeFormatter(), "text/plain")))
        response.StatusCode =? HttpStatusCode.OK
        response.Content.ReadAsStringAsync().Result =? """<string xmlns="http://schemas.microsoft.com/2003/10/Serialization/">Howdy</string>"""

    [<Test>]
    let ``test options``() =
        let response = options [HttpMethod.Get; HttpMethod.Post] (new HttpRequestMessage()) |> Async.RunSynchronously
        response.StatusCode =? HttpStatusCode.OK
        response.Content.Headers.Allow.Contains("GET") =? true
        response.Content.Headers.Allow.Contains("POST") =? true
        response.Content.Headers.Allow.Contains("PUT") =? false
        response.Content.Headers.Allow.Contains("DELETE") =? false

    [<Test>]
    let ``test 405 Method Not Allowed``() =
        let response = ``405 Method Not Allowed`` [HttpMethod.Get; HttpMethod.Post] (new HttpRequestMessage()) |> Async.RunSynchronously
        response.StatusCode =? HttpStatusCode.MethodNotAllowed
        response.Content.Headers.Allow.Contains("GET") =? true
        response.Content.Headers.Allow.Contains("POST") =? true
        response.Content.Headers.Allow.Contains("PUT") =? false
        response.Content.Headers.Allow.Contains("DELETE") =? false

    [<Test>]
    let ``test 406 Not Acceptable``() =
        let response = ``406 Not Acceptable`` <| new HttpRequestMessage() |> Async.RunSynchronously
        response.StatusCode =? HttpStatusCode.NotAcceptable

    [<Test>]
    let ``test formatWith properly format as application/json raw``() =
        let formatter = new System.Net.Http.Formatting.JsonMediaTypeFormatter()
        formatter.SerializerSettings.ContractResolver <- CamelCasePropertyNamesContractResolver()
        let body = { FirstName = "Ryan"; LastName = "Riley" }
        let content = new ObjectContent<_>(body, formatter, "application/json") :> HttpContent
        content.Headers.ContentType.MediaType =? "application/json"
        let result = content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
        result =? """{"firstName":"Ryan","lastName":"Riley"}"""

    [<Test>]
    let ``test formatWith properly format as application/json``() =
        let formatter = new System.Net.Http.Formatting.JsonMediaTypeFormatter()
        formatter.SerializerSettings.ContractResolver <- CamelCasePropertyNamesContractResolver()
        let body = { FirstName = "Ryan"; LastName = "Riley" }
        let content = body |> formatWith "application/json" formatter
        content.Headers.ContentType.MediaType =? "application/json"
        let result = content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
        result =? """{"firstName":"Ryan","lastName":"Riley"}"""

    [<Test>]
    let ``test formatWith properly format as application/xml``() =
        let formatter = new System.Net.Http.Formatting.XmlMediaTypeFormatter()
        let body = { FirstName = "Ryan"; LastName = "Riley" }
        let content = body |> formatWith "application/xml" formatter
        content.Headers.ContentType.MediaType =? "application/xml"
        let result = content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
        result =? """<TestType xmlns:i="http://www.w3.org/2001/XMLSchema-instance" xmlns="http://schemas.datacontract.org/2004/07/Frank"><FirstName_x0040_>Ryan</FirstName_x0040_><LastName_x0040_>Riley</LastName_x0040_></TestType>"""

    [<Test>]
    let ``test formatWith properly format as application/xml and read as TestType``() =
        let formatter = new System.Net.Http.Formatting.XmlMediaTypeFormatter()
        let body = { FirstName = "Ryan"; LastName = "Riley" }
        let content = body |> formatWith "application/xml" formatter
        content.Headers.ContentType.MediaType =? "application/xml"
        let result = content.ReadAsAsync<TestType>() |> Async.AwaitTask |> Async.RunSynchronously
        result.FirstName =? body.FirstName
        result.LastName =? body.LastName
