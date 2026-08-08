namespace Frank.JsonHome

open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.ApiExplorer

/// Fails startup when two or more resources declare the same JSON Home `rel`
/// -- `resources` in the served document is a JSON object keyed by `Rel`, so
/// a collision otherwise silently drops one resource's entry with no
/// diagnostic (see JsonHome.fs's `writeDocument` comment).
///
/// Runs as an `IStartupFilter`, deliberately NOT through the
/// `Microsoft.Extensions.Options` validation pipeline
/// (`AddOptionsWithValidateOnStart`): `IApiDescriptionGroupCollectionProvider`
/// only reflects Frank's registered resources once `WebHostBuilder.Run`'s
/// `Configure` delegate has run `UseEndpoints` (src/Frank/WebHostBuilder.fs).
/// That happens *after* `Host.StartAsync` runs `IStartupValidator.Validate()`
/// -- so an `IValidateOptions<T>`-based check would always see zero
/// endpoints and never fire, and reading the provider that early also
/// permanently poisons its process-lifetime cache with an empty snapshot
/// (the cache is keyed on `IActionDescriptorCollectionProvider`'s version,
/// which never changes in a Frank app), leaving every served document empty.
/// An `IStartupFilter` wraps `next`: calling `next.Invoke(app)` first
/// guarantees `UseEndpoints` has already run, so this check both fires
/// correctly and is the first read that populates the cache -- with the
/// correct, non-empty snapshot -- before Kestrel accepts a request.
///
/// Failure is still raised as an `OptionsValidationException` even though no
/// `IOptions<T>` resolution is involved: it is a ready-made carrier for a
/// named, multi-message configuration failure, and inventing a new exception
/// type here would buy nothing.
[<Sealed>]
type internal DuplicateRelStartupFilter =
    new: provider: IApiDescriptionGroupCollectionProvider -> DuplicateRelStartupFilter
    interface IStartupFilter
