namespace Frank.JsonHome

open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.Options

/// Fails startup (via ASP.NET Core's `ValidateOnStart`) when two or more
/// resources declare the same JSON Home `rel` -- `resources` in the served
/// document is a JSON object keyed by `Rel`, so a collision otherwise
/// silently drops one resource with no diagnostic (see JsonHome.fs's
/// `writeDocument` comment). Ignores its bound `JsonHomeOptions` value
/// entirely: validates the derived, app-wide resource surface via the
/// injected `IApiDescriptionGroupCollectionProvider`, the same source
/// `JsonHome.documentHandler` reads per-request.
[<Sealed>]
type internal DuplicateRelValidator =
    new: provider: IApiDescriptionGroupCollectionProvider -> DuplicateRelValidator
    interface IValidateOptions<JsonHomeOptions>

/// Returns the given `JsonHomeOptions` value unconditionally, after running
/// it through the injected validators. `JsonHomeOptions` is an immutable
/// record with no parameterless constructor, so it cannot flow through the
/// default `IOptionsFactory<T>` (`Activator.CreateInstance` +
/// `IConfigureOptions<T>.Configure(Action<T>)` mutation). This factory
/// makes `IOptions<JsonHomeOptions>.Value` and `useJsonHome`'s own
/// closure-captured options the same instance -- no second, independently
/// configured copy of the same settings. Because replacing the default
/// `IOptionsFactory<T>` opts out of its behavior entirely, this factory
/// re-implements the one piece of that behavior `AddOptionsWithValidateOnStart`
/// depends on: running every registered `IValidateOptions<JsonHomeOptions>`
/// (including `DuplicateRelValidator`) and raising `OptionsValidationException`
/// on failure, same as `OptionsFactory<TOptions>.Create` does. Configure/
/// post-configure steps are not replicated since `value` is already a
/// complete, fixed instance.
[<Sealed>]
type internal FixedJsonHomeOptionsFactory =
    new: value: JsonHomeOptions * validators: IValidateOptions<JsonHomeOptions> seq -> FixedJsonHomeOptionsFactory
    interface IOptionsFactory<JsonHomeOptions>
