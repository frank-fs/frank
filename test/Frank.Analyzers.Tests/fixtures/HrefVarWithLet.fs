module TestFixtures.HrefVarWithLet

open Frank.Builder
open Frank.JsonHome

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let private idVarUri = "https://example.com/param/product-id"

// This should NOT trigger FRANK003 - hrefVar is declared correctly, using a
// non-literal (module-level `let`-bound) uri argument.
//
// NOTE: an intervening `let` *inside* the `resource { }` CE body was not
// used here because ResourceBuilder only defines `Yield`/`Run` plus
// `[<CustomOperation>]` members (no `Combine`/`Delay`/`For`), so F# rejects
// mixing a plain `let` between custom operations with error FS0708
// ("may only be used if the computation expression builder defines a 'For'
// method"). The analyzer's `SynExpr.LetOrUse` handling in `collectHrefVars`
// is exercised instead via the module-level `let` feeding a non-literal
// argument into `hrefVar`.
let hrefVarWithLetResource =
    resource "/products/{id}" {
        rel "tag:example.com,2026:product"
        hrefVar "id" idVarUri
        get handler
    }
