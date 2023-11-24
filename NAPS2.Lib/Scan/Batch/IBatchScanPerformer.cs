using System.Threading;
using NAPS2.EtoForms;

namespace NAPS2.Scan.Batch;

public interface IBatchScanPerformer
{
    Task PerformBatchScan(BatchSettings settings, PatchTSettings patchTSettings, IFormBase batchForm, Action<ProcessedImage> imageCallback, Action<string> progressCallback, CancellationToken cancelToken);
}