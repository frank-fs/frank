### New in 6.0.0 (Released 2020-06-02)
* Update to .NET Core 3.1
* Use Endpoint Routing
* Pave the way for built-in generation of Open API spec

### New in 5.0.0 (Released 2019-01-05)
* Starting over based on ASP.NET Core Routing and Hosting
* New MIT license
* Computation expression for configuring IWebHostBuilder
* Computation expression for specifying HTTP resources
* Sample using simple ASP.NET Core web application
* Sample using standard Giraffe template web application

### New in 4.0.0 - (Released 2018/03/27)
* Update to .NETStandard 2.0 and .NET 4.6.1
* Now more easily used with Azure Functions or ASP.NET Core

### New in 3.1.1 - (Released 2014/12/07)
* Use FSharp.Core from NuGet

### New in 3.1.0 - (Released 2014/10/13)
* Remove dependency on F#x
* Signatures remain equivalent, but some type aliases have been removed.

### New in 3.0.19 - (Released 2014/10/13)
* Merge all implementations into one file and add .fsi signature

### New in 3.0.18 - (Released 2014/10/12)
* Use Paket for package management
* FSharp.Core 4.3.1.0
* NOTE: Jumped to 3.0.18 due to bad build script configuration

### New in 3.0.0 - (Released 2014/05/24)
* Updated dependencies to Web API 2.1 and .NET 4.5

### New in 2.0.3 - (Released 2014/02/07)
* Add SourceLink to link to GitHub sources (courtesy Cameron Taggart).

### New in 2.0.2 - (Released 2014/01/26)
* Remove FSharp.Core.3 as a package dependency.

### New in 2.0.0 - (Released 2014/01/07)
* Generate documentation with every release
* Fix a minor bug in routing (leading '/' was not stripped)
* Reference FSharp.Core.3 NuGet package
* Release assembly rather than current source packages:
 * FSharp.Net.Http
 * FSharp.Web.Http
 * Frank
* Adopt the FSharp.ProjectScaffold structure

### New in 1.1.1 - (Released 2014/01/01)
* Correct spacing and specify additional types in HttpContent extensions.

### New in 1.1.0 - (Released 2014/01/01)
* Remove descriptor-based implementation.

### New in 1.0.2 - (Released 2013/12/10)
* Restore Frank dependency on FSharp.Web.Http. Otherwise, devs will have to create their own routing mechanisms. A better solution is on its way.

### New in 1.0.1 - (Released 2013/12/10)
* Change Web API dependency to Microsoft.AspNet.WebApi.Core.

### New in 1.0.0 - (Released 2013/12/10)
* First official release.
* Use an Option type for empty content.
