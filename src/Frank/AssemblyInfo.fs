namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Frank")>]
[<assembly: AssemblyProductAttribute("Frank")>]
[<assembly: AssemblyDescriptionAttribute("A functional web application DSL for ASP.NET Web API.")>]
[<assembly: AssemblyVersionAttribute("1.2.0")>]
[<assembly: AssemblyFileVersionAttribute("1.2.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.2.0"
