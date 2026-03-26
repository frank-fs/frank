/// Shared test helper: simple EndpointDataSource for test scenarios.
/// ResourceEndpointDataSource is internal, so tests need their own subclass.
/// Linked into each test project via <Compile Include="../TestEndpointDataSource.fs" />.
module Frank.Tests.Shared.TestEndpointDataSource

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Primitives

type TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _
