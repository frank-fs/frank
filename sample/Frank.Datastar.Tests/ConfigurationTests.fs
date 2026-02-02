namespace Frank.Datastar.Tests

open NUnit.Framework

/// Tests for configuration loading (US5)
[<TestFixture>]
type ConfigurationTests() =

    [<Test>]
    member _.``Configuration loads when valid DATASTAR_SAMPLE is set``() =
        // This test runs because configuration already loaded successfully
        // If DATASTAR_SAMPLE was missing or invalid, the test would not reach this point
        let config = TestConfiguration.getConfig ()
        Assert.That(config.SampleName, Is.Not.Null.And.Not.Empty, "Sample name should be loaded from environment")

    [<Test>]
    member _.``Configuration reports sample name in test output``() =
        let config = TestConfiguration.getConfig ()
        TestContext.WriteLine($"Sample: {config.SampleName}")
        TestContext.WriteLine($"Base URL: {config.BaseUrl}")
        TestContext.WriteLine($"Timeout: {config.TimeoutMs}ms")
        Assert.Pass($"Testing sample: {config.SampleName}")

    [<Test>]
    member _.``Discovered samples exclude Frank.Datastar.Tests``() =
        let config = TestConfiguration.getConfig ()
        Assert.That(
            config.AvailableSamples,
            Does.Not.Contain("Frank.Datastar.Tests"),
            "Available samples should not include the test project itself"
        )

    [<Test>]
    member _.``All discovered samples start with Frank.Datastar prefix``() =
        let config = TestConfiguration.getConfig ()

        for sample in config.AvailableSamples do
            Assert.That(
                sample.StartsWith("Frank.Datastar."),
                Is.True,
                $"Sample '{sample}' should start with 'Frank.Datastar.'"
            )

    [<Test>]
    member _.``BaseUrl has default value when not specified``() =
        let config = TestConfiguration.getConfig ()
        Assert.That(config.BaseUrl, Is.Not.Null.And.Not.Empty, "Base URL should have a value")

    [<Test>]
    member _.``TimeoutMs has positive value``() =
        let config = TestConfiguration.getConfig ()
        Assert.That(config.TimeoutMs, Is.GreaterThan(0), "Timeout should be positive")
