using BTCPayServer.Tests;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Plugins.Tests.MoneroPluginUITest;

namespace BTCPayServer.Plugins.Tests;

public class MoneroPluginUITest : PlaywrightBaseTest, IClassFixture<MoneroPluginServerTesterFixture>
{
    private readonly MoneroPluginServerTesterFixture _fixture;

    public MoneroPluginUITest(MoneroPluginServerTesterFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }
    public string TestDir { get; private set; }

    [Fact]
    public async Task EnableMoneroPaymentTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        // Todo
    }

    public class MoneroPluginServerTesterFixture : IDisposable
    {
        public ServerTester ServerTester { get; private set; }

        public void Dispose()
        {
            ServerTester?.Dispose();
            ServerTester = null;
        }

        public void Initialize(PlaywrightBaseTest testInstance)
        {
            if (ServerTester == null)
            {
                var testDir = Path.Combine(Directory.GetCurrentDirectory(), "MoneroPluginUITest");
                ServerTester = testInstance.CreateServerTester(testDir, true);
                ServerTester.StartAsync().GetAwaiter().GetResult();
            }
        }
    }
}
