using System.Runtime.CompilerServices;

namespace Egoist.Voice.Tests;

public sealed class CaptureOperationQueueLifetimeTests
{
    [Fact]
    public async Task Completed_operation_releases_its_large_result_while_queue_remains_open()
    {
        var capture = new ControlledCapture();
        var queue = new CaptureOperationQueue(capture);
        var result = RunAndDropResult(queue);
        try
        {
            // Let the worker leave its completion stack; the test owns no strong result reference.
            await Task.Delay(30);
            for (var attempt = 0; attempt < 3 && result.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(10);
            }
            Assert.False(result.IsAlive);
            GC.KeepAlive(queue);
            Assert.Equal(0, capture.DisposeCount);
        }
        finally { await queue.ShutdownAsync(); }
        Assert.Equal(1, capture.DisposeCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunAndDropResult(CaptureOperationQueue queue)
    {
        var result = queue.RunAsync(static () => Task.FromResult(new float[256_000]))
            .GetAwaiter().GetResult();
        return new WeakReference(result);
    }
}
