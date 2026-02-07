namespace Frank.Datastar

open System

type ElementPatchMode =
    /// Morphs the element into the existing element.
    | Outer
    /// Replaces the inner HTML of the existing element.
    | Inner
    /// Removes the existing element.
    | Remove
    /// Replaces the existing element with the new element.
    | Replace
    /// Prepends the element inside to the existing element.
    | Prepend
    /// Appends the element inside the existing element.
    | Append
    /// Inserts the element before the existing element.
    | Before
    /// Inserts the element after the existing element.
    | After

type PatchElementNamespace =
    | Html
    | Svg
    | MathMl

module Consts =
    [<Literal>]
    let DatastarKey = "datastar"

    // Defaults
    let DefaultSseRetryDuration = TimeSpan.FromSeconds(1.0)
    let DefaultElementPatchMode = Outer
    let DefaultPatchElementNamespace = Html

    [<Literal>]
    let DefaultElementsUseViewTransitions = false

    [<Literal>]
    let DefaultPatchSignalsOnlyIfMissing = false

    [<Literal>]
    let internal ScriptDataEffectRemove = @"data-effect=""el.remove()"""

module internal Bytes =
    // Internal: Byte Constants
    let EventTypePatchElements = "datastar-patch-elements"B
    let EventTypePatchSignals = "datastar-patch-signals"B

    let DatalineSelector = "selector"B
    let DatalineMode = "mode"B
    let DatalineElements = "elements"B
    let DatalineUseViewTransition = "useViewTransition"B
    let DatalineNamespace = "namespace"B
    let DatalineSignals = "signals"B
    let DatalineOnlyIfMissing = "onlyIfMissing"B

    let bTrue = "true"B
    let bFalse = "false"B
    let bSpace = " "B
    let bQuote = @""""B

    let bOpenScriptAutoRemove = @"<script data-effect=""el.remove()"">"B
    let bOpenScript = "<script>"B
    let bCloseScript = "</script>"B
    let bBody = "body"B

    module PatchElementNamespace =
        let bHtml = "html"B
        let bSvg = "svg"B
        let bMathMl = "mathml"B

        let inline toBytes this =
            match this with
            | Html -> bHtml
            | Svg -> bSvg
            | MathMl -> bMathMl

    module ElementPatchMode =
        let bOuter = "outer"B
        let bInner = "inner"B
        let bRemove = "remove"B
        let bReplace = "replace"B
        let bPrepend = "prepend"B
        let bAppend = "append"B
        let bBefore = "before"B
        let bAfter = "after"B

        let inline toBytes this =
            match this with
            | ElementPatchMode.Outer -> bOuter
            | ElementPatchMode.Inner -> bInner
            | ElementPatchMode.Remove -> bRemove
            | ElementPatchMode.Replace -> bReplace
            | ElementPatchMode.Prepend -> bPrepend
            | ElementPatchMode.Append -> bAppend
            | ElementPatchMode.Before -> bBefore
            | ElementPatchMode.After -> bAfter
