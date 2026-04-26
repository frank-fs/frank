namespace Frank.Datastar

open System

/// Signals read to and from Datastar on the front end
type Signals = string

/// An HTML selector name
type Selector = string

[<Struct>]
type PatchElementsOptions =
    { Selector: Selector voption
      PatchMode: ElementPatchMode
      UseViewTransition: bool
      Namespace: PatchElementNamespace
      EventId: string voption
      Retry: TimeSpan }

    static member Defaults =
        { Selector = ValueNone
          PatchMode = Consts.DefaultElementPatchMode
          UseViewTransition = Consts.DefaultElementsUseViewTransitions
          Namespace = Consts.DefaultPatchElementNamespace
          EventId = ValueNone
          Retry = Consts.DefaultSseRetryDuration }

[<Struct>]
type RemoveElementOptions =
    { UseViewTransition: bool
      EventId: string voption
      Retry: TimeSpan }

    static member Defaults =
        { UseViewTransition = Consts.DefaultElementsUseViewTransitions
          EventId = ValueNone
          Retry = Consts.DefaultSseRetryDuration }

[<Struct>]
type PatchSignalsOptions =
    { OnlyIfMissing: bool
      EventId: string voption
      Retry: TimeSpan }

    static member Defaults =
        { OnlyIfMissing = Consts.DefaultPatchSignalsOnlyIfMissing
          EventId = ValueNone
          Retry = Consts.DefaultSseRetryDuration }

[<Struct>]
type ExecuteScriptOptions =
    {
        EventId: string voption
        Retry: TimeSpan
        AutoRemove: bool
        /// Pre-formed attribute strings written verbatim to the &lt;script&gt; tag
        Attributes: string[]
    }

    static member Defaults =
        { EventId = ValueNone
          Retry = Consts.DefaultSseRetryDuration
          AutoRemove = true
          Attributes = [||] }
