# Quickstart: RDF SPARQL Validation

**Feature**: 015-rdf-sparql-validation
**Date**: 2026-03-15

## Prerequisites

- .NET 10.0 SDK installed
- Frank solution builds successfully (`dotnet build Frank.sln`)
- Frank.LinkedData and Frank.Provenance are implemented and producing RDF output

## Project Setup

The test project lives at `test/Frank.RdfValidation.Tests/` and must be added to `Frank.sln`.

### Create Project

```bash
# From repository root
dotnet new expecto -lang F# -o test/Frank.RdfValidation.Tests --framework net10.0
dotnet sln Frank.sln add test/Frank.RdfValidation.Tests/Frank.RdfValidation.Tests.fsproj --solution-folder test
```

### Project File

The `.fsproj` follows the established pattern from `Frank.LinkedData.Tests`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <GenerateProgramFile>false</GenerateProgramFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="TestHelpers.fs" />
    <Compile Include="RdfParsingTests.fs" />
    <Compile Include="SparqlResourceQueryTests.fs" />
    <Compile Include="ProvenanceGraphTests.fs" />
    <Compile Include="GraphCoherenceTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0" />
    <PackageReference Include="dotNetRdf.Core" Version="3.5.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.LinkedData/Frank.LinkedData.fsproj" />
    <ProjectReference Include="../../src/Frank.Provenance/Frank.Provenance.fsproj" />
  </ItemGroup>
</Project>
```

### Key dotNetRdf Types for SPARQL

```fsharp
open VDS.RDF                    // IGraph, Graph, TripleStore, Triple, INode
open VDS.RDF.Parsing            // TurtleParser, RdfXmlParser, SparqlQueryParser
open VDS.RDF.Query              // LeviathanQueryProcessor, SparqlResultSet
open VDS.RDF.Query.Datasets     // InMemoryDataset
open VDS.RDF.Writing            // (if needed for debug output)
```

## Running Tests

```bash
# Run all RDF validation tests
dotnet test test/Frank.RdfValidation.Tests/

# Run specific test module
dotnet test test/Frank.RdfValidation.Tests/ --filter "RdfParsing"

# Run with verbose output
dotnet test test/Frank.RdfValidation.Tests/ -v detailed
```

## Test Pattern Summary

Each test follows this flow:

1. **Arrange**: Create TestHost with Frank app (LinkedData + Provenance middleware)
2. **Act**: Send HTTP request with RDF Accept header -> parse response into dotNetRdf graph -> execute SPARQL query
3. **Assert**: Verify SPARQL result set matches expectations

### Example: SPARQL SELECT Query Test

```fsharp
testAsync "US2-SC1: SPARQL SELECT finds all resources with their rdf:type" {
    // 1. Create TestHost and get RDF response
    use host = createLinkedDataTestHost ()
    let server = host.GetTestServer()
    use client = server.CreateClient()
    let request = new HttpRequestMessage(HttpMethod.Get, "/person/1")
    request.Headers.Add("Accept", "text/turtle")
    let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

    // 2. Parse into graph and run SPARQL
    use graph = loadTurtleGraph body
    let results = executeSparql graph """
        # Find all resources and their RDF types
        SELECT ?resource ?type
        WHERE {
            ?resource a ?type .
        }
    """

    // 3. Assert
    Expect.isGreaterThan results.Count 0 "Should find at least one typed resource"
}
```
