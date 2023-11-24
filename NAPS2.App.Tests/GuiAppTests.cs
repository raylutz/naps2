using System.Threading;
using NAPS2.App.Tests.Targets;
using NAPS2.Remoting;
using NAPS2.Sdk.Tests;
using Xunit;

namespace NAPS2.App.Tests;

public class GuiAppTests : ContextualTests
{
    [GuiTheory]
    [ClassData(typeof(AppTestData))]
    public void CreatesWindow(IAppTestTarget target)
    {
        var process = AppTestHelper.StartGuiProcess(target.Gui, FolderPath);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                AppTestHelper.WaitForVisibleWindow(process);
                Assert.Equal("ScanEngine - ScanEngine", process.MainWindowTitle);
                Assert.True(process.CloseMainWindow());
            }
            else
            {
                Thread.Sleep(1000);
                Assert.True(Pipes.SendMessage(process, Pipes.MSG_CLOSE_WINDOW));
            }
            Assert.True(process.WaitForExit(5000));
            AppTestHelper.AssertNoErrorLog(FolderPath);
        }
        finally
        {
            AppTestHelper.Cleanup(process);
        }
    }
}