namespace Frank.Datastar.Tests

open System
open System.Threading.Tasks
open NUnit.Framework
open Microsoft.Playwright

/// Base class for all Playwright-based tests.
/// Manages browser lifecycle and provides fresh page per test.
[<AbstractClass>]
type TestBase() =
    let mutable playwright: IPlaywright = null
    let mutable browser: IBrowser = null
    let mutable context: IBrowserContext = null
    let mutable page: IPage = null

    let config = TestConfiguration.getConfig ()

    /// The current page for this test
    member _.Page = page

    /// The browser context for this test
    member _.Context = context

    /// The configured base URL
    member _.BaseUrl = config.BaseUrl

    /// The configured timeout in milliseconds
    member _.TimeoutMs = config.TimeoutMs

    /// The sample being tested
    member _.SampleName = config.SampleName

    /// Launches the browser once per test fixture
    [<OneTimeSetUp>]
    member _.SetupBrowser() : Task =
        task {
            TestContext.WriteLine($"Testing sample: {config.SampleName}")
            TestContext.WriteLine($"Base URL: {config.BaseUrl}")
            TestContext.WriteLine($"Timeout: {config.TimeoutMs}ms")

            let! pw = Playwright.CreateAsync()
            playwright <- pw

            // Check for headed mode
            let headed =
                Environment.GetEnvironmentVariable("HEADED")
                |> Option.ofObj
                |> Option.map (fun s -> s = "1" || s.ToLower() = "true")
                |> Option.defaultValue false

            let! b = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = not headed))
            browser <- b
        }

    /// Creates a fresh browser context and page for each test
    [<SetUp>]
    member _.SetupPage() : Task =
        task {
            let! ctx = browser.NewContextAsync()
            context <- ctx

            let! p = ctx.NewPageAsync()
            page <- p

            // Navigate to base URL and handle connection errors
            try
                let options = PageGotoOptions(Timeout = Nullable(float32 config.TimeoutMs))
                let! _ = page.GotoAsync(config.BaseUrl, options)
                ()
            with ex ->
                let message =
                    $"Cannot connect to {config.BaseUrl}. Ensure the sample server is running: dotnet run --project sample/{config.SampleName}/"

                raise (Exception(message, ex))
        }

    /// Closes page and context after each test
    [<TearDown>]
    member _.TeardownPage() : Task =
        task {
            if not (isNull page) then
                do! page.CloseAsync()
                page <- null

            if not (isNull context) then
                do! context.CloseAsync()
                context <- null
        }

    /// Closes browser after all tests in fixture
    [<OneTimeTearDown>]
    member _.TeardownBrowser() =
        if not (isNull browser) then
            browser.CloseAsync() |> Async.AwaitTask |> Async.RunSynchronously
            browser <- null

        if not (isNull playwright) then
            playwright.Dispose()
            playwright <- null
