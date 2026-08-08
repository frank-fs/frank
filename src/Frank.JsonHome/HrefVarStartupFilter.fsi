namespace Frank.JsonHome

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.ApiExplorer

/// Raised by HrefVarStartupFilter when one or more resources have a
/// hrefVar/route-template mismatch. Carries every mismatch found, not just
/// the first.
exception HrefVarValidationException of messages: string list

/// Runs HrefVarValidation.diff against every resource in the running
/// application's ApiSurface, once the request pipeline (including routing)
/// has been built -- see research.md R1 for why IStartupFilter, not
/// IValidateOptions, is the correct hook for this check. Not yet wired into
/// useJsonHome's DI registration -- see WebHostBuilderExtensions.fs (T010).
[<Sealed>]
type HrefVarStartupFilter =
    new: apiDescriptions: IApiDescriptionGroupCollectionProvider -> HrefVarStartupFilter
    interface IStartupFilter
