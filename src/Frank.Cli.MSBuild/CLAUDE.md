# Frank.Cli.MSBuild

MSBuild integration for the Frank CLI tool — `.props`/`.targets` shipped to consumers.

## Gotchas

- **`.props` vs `.targets` evaluation timing.** Properties in `.props` that reference SDK-computed values (`IntermediateOutputPath`, `TargetFramework`) resolve to empty because `.props` is imported before the SDK sets them. Even static `PropertyGroup` in `.targets` can be too early when consuming projects import targets inline. Use a `Target` with inner `PropertyGroup` for true late-binding of SDK-dependent defaults.
- **DLL resource verification via `dotnet fsi`.** Quick embedded resource check without external tools: `dotnet fsi -e "let a = System.Reflection.Assembly.LoadFrom(\"path.dll\") in a.GetManifestResourceNames() |> Array.iter (printfn \"%s\")"`
- **NuGet tool cache serves stale binaries.** When reinstalling local dotnet tools from `nupkg/`, clear the global cache first: `rm -rf ~/.nuget/packages/<tool-name>` before `dotnet tool install`. `dotnet clean` + `dotnet pack` alone don't invalidate the cache.
