namespace Frank.Alps

open Microsoft.AspNetCore.Http
open Frank.Builder

type AlpsOptions =
    { /// Path the document is served from.
      Path: string
      /// Link relation type used when advertising the document.
      Rel: string }

    /// Path "/.well-known/alps.json", rel "profile".
    static member Default: AlpsOptions

/// The app-wide ALPS document: served at a fixed path (default `/.well-known/alps.json`), registered
/// via `useAlps` exactly the way `useJsonHome` registers its own document (`src/Frank.JsonHome`).
module AlpsDocument =
    [<Literal>]
    val MediaType: string = "application/alps+json"

    /// Raises if any non-semantic descriptor's bound endpoint's HTTP method(s) don't match its
    /// `DescriptorType` (`Safe` -> GET/HEAD, `Idempotent` -> PUT/DELETE, `Unsafe` -> POST). Semantic
    /// descriptors are never validated -- they aren't transitions bound to a method.
    val validate: pairs: (Endpoint * Descriptor) list -> unit

    /// The resource that serves the document itself, restricted to `profile`. Add its Endpoints to
    /// WebHostSpec.Endpoints rather than a separate app.UseEndpoints(...) call: that dispatches
    /// through the same, single, structurally-last routing stage every other Frank resource uses, so
    /// it runs after any authentication/authorization middleware the app has composed -- regardless
    /// of where useAlps appears in the webHost {} block. Mirrors
    /// `Frank.JsonHome.JsonHome.documentResource`'s own reasoning and doc comment verbatim.
    val documentResource: options: AlpsOptions -> profile: Descriptor list -> Resource

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with

        [<CustomOperation("useAlps")>]
        member UseAlps: spec: WebHostSpec * profile: Descriptor list -> WebHostSpec

        [<CustomOperation("useAlps")>]
        member UseAlps: spec: WebHostSpec * profile: Descriptor list * configure: (AlpsOptions -> AlpsOptions) -> WebHostSpec
