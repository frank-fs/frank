namespace Frank.Datastar

open System

[<Struct>]
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

[<Struct>]
type PatchElementNamespace =
    | Html
    | Svg
    | MathMl

module Consts =
    [<Literal>]
    val DatastarKey : string = "datastar"

    val DefaultSseRetryDuration : TimeSpan
    val DefaultElementPatchMode : ElementPatchMode
    val DefaultPatchElementNamespace : PatchElementNamespace

    [<Literal>]
    val DefaultElementsUseViewTransitions : bool = false

    [<Literal>]
    val DefaultPatchSignalsOnlyIfMissing : bool = false

    [<Literal>]
    val internal ScriptDataEffectRemove : string = @"data-effect=""el.remove()"""

module internal Bytes =
    val EventTypePatchElements : byte[]
    val EventTypePatchSignals : byte[]

    val DatalineSelector : byte[]
    val DatalineMode : byte[]
    val DatalineElements : byte[]
    val DatalineUseViewTransition : byte[]
    val DatalineViewTransitionSelector : byte[]
    val DatalineNamespace : byte[]
    val DatalineSignals : byte[]
    val DatalineOnlyIfMissing : byte[]

    val bTrue : byte[]
    val bFalse : byte[]
    val bSpace : byte[]
    val bQuote : byte[]

    val bOpenScriptAutoRemove : byte[]
    val bOpenScript : byte[]
    val bCloseScript : byte[]
    val bBody : byte[]

    module PatchElementNamespace =
        val bHtml : byte[]
        val bSvg : byte[]
        val bMathMl : byte[]

        val inline toBytes : PatchElementNamespace -> byte[]

    module ElementPatchMode =
        val bOuter : byte[]
        val bInner : byte[]
        val bRemove : byte[]
        val bReplace : byte[]
        val bPrepend : byte[]
        val bAppend : byte[]
        val bBefore : byte[]
        val bAfter : byte[]

        val inline toBytes : ElementPatchMode -> byte[]
