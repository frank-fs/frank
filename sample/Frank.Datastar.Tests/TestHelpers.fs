module TestHelpers

open System
open System.Threading.Tasks
open Microsoft.Playwright

/// Wraps a wait operation with enhanced timeout error context
let private withTimeoutContext (description: string) (waitTask: Task) : Task =
    task {
        try
            do! waitTask
        with
        | :? TimeoutException as ex ->
            raise (TimeoutException($"Timeout waiting for: {description}. {ex.Message}", ex))
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout waiting for: {description}. {ex.Message}", ex))
    }
    :> Task

/// Waits for an element's text content to match the expected value exactly
let waitForText (page: IPage) (selector: string) (expected: string) (timeoutMs: int) : Task =
    task {
        let js = $"() => document.querySelector('{selector}')?.textContent?.trim() === '{expected}'"
        let options = PageWaitForFunctionOptions(Timeout = Nullable(float32 timeoutMs))
        let description = $"element '{selector}' to have text '{expected}'"
        try
            let! _ = page.WaitForFunctionAsync(js, null, options)
            ()
        with
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout ({timeoutMs}ms) waiting for {description}", ex))
    }
    :> Task

/// Waits for an element's text content to contain the expected substring
let waitForTextContains (page: IPage) (selector: string) (substring: string) (timeoutMs: int) : Task =
    task {
        let js = $"() => document.querySelector('{selector}')?.textContent?.includes('{substring}')"
        let options = PageWaitForFunctionOptions(Timeout = Nullable(float32 timeoutMs))
        let description = $"element '{selector}' to contain text '{substring}'"
        try
            let! _ = page.WaitForFunctionAsync(js, null, options)
            ()
        with
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout ({timeoutMs}ms) waiting for {description}", ex))
    }
    :> Task

/// Waits for at least one element matching the selector to become visible
let waitForVisible (page: IPage) (selector: string) (timeoutMs: int) : Task =
    task {
        let locator = page.Locator(selector).First
        let options = LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable(float32 timeoutMs))
        let description = $"element '{selector}' to become visible"
        try
            do! locator.WaitForAsync(options)
        with
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout ({timeoutMs}ms) waiting for {description}", ex))
    }
    :> Task

/// Waits for an element to become hidden or removed from DOM
let waitForHidden (page: IPage) (selector: string) (timeoutMs: int) : Task =
    task {
        let locator = page.Locator(selector)
        let options = LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable(float32 timeoutMs))
        let description = $"element '{selector}' to become hidden"
        try
            do! locator.WaitForAsync(options)
        with
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout ({timeoutMs}ms) waiting for {description}", ex))
    }
    :> Task

/// Waits for an element to exist in the DOM (even if not visible)
let waitForAttached (page: IPage) (selector: string) (timeoutMs: int) : Task =
    task {
        let locator = page.Locator(selector)
        let options = LocatorWaitForOptions(State = WaitForSelectorState.Attached, Timeout = Nullable(float32 timeoutMs))
        let description = $"element '{selector}' to be attached to DOM"
        try
            do! locator.WaitForAsync(options)
        with
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout ({timeoutMs}ms) waiting for {description}", ex))
    }
    :> Task

/// Waits for an element to be removed from the DOM
let waitForDetached (page: IPage) (selector: string) (timeoutMs: int) : Task =
    task {
        let locator = page.Locator(selector)
        let options = LocatorWaitForOptions(State = WaitForSelectorState.Detached, Timeout = Nullable(float32 timeoutMs))
        let description = $"element '{selector}' to be removed from DOM"
        try
            do! locator.WaitForAsync(options)
        with
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout ({timeoutMs}ms) waiting for {description}", ex))
    }
    :> Task

/// Waits for an input element to have a specific value
let waitForInputValue (page: IPage) (selector: string) (expected: string) (timeoutMs: int) : Task =
    task {
        let js = $"() => document.querySelector('{selector}')?.value === '{expected}'"
        let options = PageWaitForFunctionOptions(Timeout = Nullable(float32 timeoutMs))
        let description = $"input '{selector}' to have value '{expected}'"
        try
            let! _ = page.WaitForFunctionAsync(js, null, options)
            ()
        with
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout ({timeoutMs}ms) waiting for {description}", ex))
    }
    :> Task

/// Waits for an element count to match expected value
let waitForCount (page: IPage) (selector: string) (expected: int) (timeoutMs: int) : Task =
    task {
        let js = $"() => document.querySelectorAll('{selector}').length === {expected}"
        let options = PageWaitForFunctionOptions(Timeout = Nullable(float32 timeoutMs))
        let description = $"count of '{selector}' to equal {expected}"
        try
            let! _ = page.WaitForFunctionAsync(js, null, options)
            ()
        with
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout ({timeoutMs}ms) waiting for {description}", ex))
    }
    :> Task

/// Waits for specific number of list items
let waitForListItems (page: IPage) (listSelector: string) (itemSelector: string) (minCount: int) (timeoutMs: int) : Task =
    task {
        let js = $"() => document.querySelectorAll('{listSelector} {itemSelector}').length >= {minCount}"
        let options = PageWaitForFunctionOptions(Timeout = Nullable(float32 timeoutMs))
        let description = $"count of '{listSelector} {itemSelector}' to be >= {minCount}"
        try
            let! _ = page.WaitForFunctionAsync(js, null, options)
            ()
        with
        | :? PlaywrightException as ex when ex.Message.Contains("Timeout") ->
            raise (TimeoutException($"Timeout ({timeoutMs}ms) waiting for {description}", ex))
    }
    :> Task

/// Gets all text content from elements matching a selector
let getAllTextContent (page: IPage) (selector: string) : Task<string list> =
    task {
        let locator = page.Locator(selector)
        let! texts = locator.AllTextContentsAsync()
        return texts |> Seq.toList
    }

/// Checks if an element is visible
let isVisible (page: IPage) (selector: string) : Task<bool> =
    task {
        let locator = page.Locator(selector)
        return! locator.IsVisibleAsync()
    }

/// Gets text content of an element, returning None if element doesn't exist
let tryGetTextContent (page: IPage) (selector: string) : Task<string option> =
    task {
        let locator = page.Locator(selector)
        let! count = locator.CountAsync()

        if count > 0 then
            let! text = locator.TextContentAsync()
            return Some text
        else
            return None
    }
