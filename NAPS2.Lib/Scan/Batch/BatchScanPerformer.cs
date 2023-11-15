using System.Globalization;
using System.Threading;
using Eto.Forms;
using Google.Protobuf.WellKnownTypes;
using NAPS2.EtoForms;
using NAPS2.EtoForms.Ui;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Images;
using NAPS2.Ocr;
using NAPS2.Pdf;

namespace NAPS2.Scan.Batch;

public class BatchScanPerformer : IBatchScanPerformer
{
    private readonly IScanPerformer _scanPerformer;
    private readonly IPdfExporter _pdfExporter;
    private readonly IOperationFactory _operationFactory;
    private readonly IFormFactory _formFactory;
    private readonly Naps2Config _config;
    private readonly IProfileManager _profileManager;
    private readonly ThumbnailController _thumbnailController;

    public BatchScanPerformer(IScanPerformer scanPerformer, IPdfExporter pdfExporter,
        IOperationFactory operationFactory,
        IFormFactory formFactory, Naps2Config config, IProfileManager profileManager,
        ThumbnailController thumbnailController)
    {
        _scanPerformer = scanPerformer;
        _pdfExporter = pdfExporter;
        _operationFactory = operationFactory;
        _formFactory = formFactory;
        _config = config;
        _profileManager = profileManager;
        _thumbnailController = thumbnailController;
    }

    public async Task PerformBatchScan(BatchSettings settings, PatchTSettings patchTSettings, IFormBase batchForm,
        Action<ProcessedImage> imageCallback, Action<string> progressCallback, CancellationToken cancelToken)
    {
        var state = new BatchState(_scanPerformer, _pdfExporter, _operationFactory, _formFactory, _config,
            _profileManager, _thumbnailController, settings, patchTSettings, progressCallback, cancelToken, batchForm, imageCallback);
        await state.Do();
    }

    private class BatchState
    {
        private readonly IScanPerformer _scanPerformer;
        private readonly IPdfExporter _pdfExporter;
        private readonly IOperationFactory _operationFactory;
        private readonly IFormFactory _formFactory;
        private readonly Naps2Config _config;
        private readonly IProfileManager _profileManager;

        private readonly BatchSettings _settings;
        private readonly PatchTSettings _patchTSettings;
        private readonly Action<string> _progressCallback;
        private readonly CancellationToken _cancelToken;
        private readonly IFormBase _batchForm;
        private readonly Action<ProcessedImage> _loadImageCallback;

        private ScanProfile _profile;
        private ScanParams _scanParams;
        private List<List<ProcessedImage>> _scans;
        private int i_val = 0;

        public BatchState(IScanPerformer scanPerformer, IPdfExporter pdfExporter, IOperationFactory operationFactory,
            IFormFactory formFactory, Naps2Config config, IProfileManager profileManager,
            ThumbnailController thumbnailController, BatchSettings settings, PatchTSettings patchTSettings,
            Action<string> progressCallback, CancellationToken cancelToken, IFormBase batchForm,
            Action<ProcessedImage> loadImageCallback)
        {
            _scanPerformer = scanPerformer;
            _pdfExporter = pdfExporter;
            _operationFactory = operationFactory;
            _formFactory = formFactory;
            _config = config;
            _profileManager = profileManager;
            _settings = settings;
            _patchTSettings = patchTSettings;
            _progressCallback = progressCallback;
            _cancelToken = cancelToken;
            _batchForm = batchForm;
            _loadImageCallback = loadImageCallback;

            _profile = _profileManager.Profiles.First(x => x.DisplayName == _settings.ProfileDisplayName);
            _scanParams = new ScanParams
            {
                DetectPatchT = _settings.OutputType == BatchOutputType.MultipleFiles &&
                               _settings.SaveSeparator == SaveSeparator.PatchT,
                NoUI = true,
                NoAutoSave = _config.Get(c => c.DisableAutoSave),
                OcrParams = _settings.OutputType == BatchOutputType.Load
                    ? _config.OcrAfterScanningParams()
                    : GetSavePathExtension().ToLower() == ".pdf"
                        ? _config.DefaultOcrParams()
                        : OcrParams.Empty,
                OcrCancelToken = _cancelToken,
                ThumbnailSize = thumbnailController.RenderSize
            };
            _scans = new List<List<ProcessedImage>>();
        }

        public async Task Do()
        {
            try
            {
                _cancelToken.ThrowIfCancellationRequested();
                await Input();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                _cancelToken.ThrowIfCancellationRequested();
                // Save at least some data so it isn't lost
                await Output();
                throw;
            }

            try
            {
                _cancelToken.ThrowIfCancellationRequested();
                await Output();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task Input()
        {
            await Task.Run(async () =>
            {
                if (_settings.ScanType == BatchScanType.Single)
                {
                    await InputOneScan(-1);
                }
                else if (_settings.ScanType == BatchScanType.MultipleWithDelay)
                {
                    for (int i = 0; i < _settings.ScanCount; i++)
                    {
                        _progressCallback(string.Format(MiscResources.BatchStatusWaitingForScan, i + 1));
                        if (i != 0)
                        {
                            ThreadSleepWithCancel(TimeSpan.FromSeconds(_settings.ScanIntervalSeconds),
                                _cancelToken);
                            _cancelToken.ThrowIfCancellationRequested();
                        }

                        if (!await InputOneScan(i))
                        {
                            return;
                        }
                    }
                }
                else if (_settings.ScanType == BatchScanType.MultipleWithPrompt)
                {
                    int i = 0;
                    do
                    {
                        _progressCallback(string.Format(MiscResources.BatchStatusWaitingForScan, i + 1));
                        if (!await InputOneScan(i++))
                        {
                            return;
                        }
                        _cancelToken.ThrowIfCancellationRequested();
                    } while (PromptForNextScan());
                }
            });
        }

        private void ThreadSleepWithCancel(TimeSpan sleepDuration, CancellationToken cancelToken)
        {
            cancelToken.WaitHandle.WaitOne(sleepDuration);
        }

        private async Task<bool> InputOneScan(int scanNumber)
        {
            var scan = new List<ProcessedImage>();
            int pageNumber = 1;
            _progressCallback(scanNumber == -1
                ? string.Format(MiscResources.BatchStatusPage, pageNumber++)
                : string.Format(MiscResources.BatchStatusScanPage, pageNumber++, scanNumber + 1));
            _cancelToken.ThrowIfCancellationRequested();
            try
            {
                await DoScan(scanNumber, scan, pageNumber);
            }
            catch (OperationCanceledException)
            {
                _scans.Add(scan);
                throw;
            }
            if (scan.Count == 0)
            {
                // Presume cancelled
                return false;
            }
            _scans.Add(scan);
            return true;
        }

        private async Task DoScan(int scanNumber, List<ProcessedImage> scan, int pageNumber)
        {
            var handle = Invoker.Current.InvokeGet(() => (_batchForm as Window)?.NativeHandle ?? IntPtr.Zero);
            var images =
                _scanPerformer.PerformScan(_profile, _scanParams, handle, _cancelToken);
            await foreach(var image in images)
            {
                scan.Add(image);
                _cancelToken.ThrowIfCancellationRequested();
                _progressCallback(scanNumber == -1
                    ? string.Format(MiscResources.BatchStatusPage, pageNumber++)
                    : string.Format(MiscResources.BatchStatusScanPage, pageNumber++, scanNumber + 1));
            }
        }

        private bool PromptForNextScan()
        {
            return Invoker.Current.InvokeGet(() =>
            {
                var promptForm = _formFactory.Create<BatchPromptForm>();
                promptForm.ScanNumber = _scans.Count + 1;
                promptForm.ShowModal();
                return promptForm.Result;
            });
        }

        private async Task Output()
        {
            _progressCallback(MiscResources.BatchStatusSaving);

            var placeholders = Placeholders.All.WithDate(DateTime.Now);
            var allImages = _scans.SelectMany(x => x).ToList();

            if (_settings.OutputType == BatchOutputType.Load)
            {
                foreach (var image in allImages)
                {
                    _loadImageCallback(image);
                }
            }
            else if (_settings.OutputType == BatchOutputType.SingleFile)
            {
                await Save(placeholders, 0, allImages);
                foreach (var img in allImages)
                {
                    img.Dispose();
                }
            }
            else if (_settings.OutputType == BatchOutputType.MultipleFiles)
            {
                if (_patchTSettings.CreatePatchTLog)
                    Log.Info($": ***** Tabulator State Reset. *****");
                foreach (var imageList in SaveSeparatorHelper.SeparateScans(_scans, _settings.SaveSeparator, config: _config))
                {
                    if (_patchTSettings.CreatePatchTLog)
                        Log.Info($": BATCH {Placeholders.All.Substitute(_patchTSettings.BatchName, true, i_val)} Scanning (auto-detect) started -------->");
                    await Save(placeholders, i_val++, imageList);

                    foreach (var img in imageList)
                    {
                        img.Dispose();
                    }
                }

                if (_patchTSettings.RestartSheetsNumberingPerBatch)
                {
                    i_val = 0;
                }
            }
        }

        private async Task Save(Placeholders placeholders, int i, List<ProcessedImage> images)
        {
            if (images.Count == 0)
            {
                return;
            }
            bool isBatchLog = _patchTSettings.CreatePatchTLog;
            var barcodeName = "";
            if (_config.Get(c => c.PatchTSettings.BarcodeName) != null)            {
                barcodeName = _config.Get(c => c.PatchTSettings.BarcodeName) + "-";
            }

            var savePath = _settings.SavePath;
            var subPath = placeholders.Substitute(_settings.SavePath!, true, i);

            if (GetSavePathExtension().ToLower() == ".pdf")
            {
                if (_patchTSettings.SeparatorSheetStartsNewBatch)
                {
                    var dirPath = savePath!.Substring(0, savePath.Length - savePath.Substring(savePath.LastIndexOf("\\")).Length);
                    Directory.CreateDirectory(dirPath + $"\\{_config.Get(c => c.PatchTSettings.BatchName)}{i + 1}");
                    subPath = subPath.Insert(subPath.LastIndexOf("\\"), $"\\{_config.Get(c => c.PatchTSettings.BatchName)}{i + 1}");
                }
                if (File.Exists(subPath))
                {
                    subPath = placeholders.Substitute(subPath, true, 0, 1);
                }
                try
                {
                    var exportParams = new PdfExportParams(
                        _config.Get(c => c.PdfSettings.Metadata),
                        _config.Get(c => c.PdfSettings.Encryption),
                        _config.Get(c => c.PdfSettings.Compat));
                    await _pdfExporter.Export(subPath, images, exportParams, _config.DefaultOcrParams(), _cancelToken);
                }
                finally
                {
                    foreach (var image in images)
                    {
                        image.Dispose();
                    }
                }
            }
            else
            {
                var op = _operationFactory.Create<SaveImagesOperation>();
                if (_patchTSettings.SeparatorSheetStartsNewBatch) {
                    var dirPath = savePath!.Substring(0, savePath.Length - savePath.Substring(savePath.LastIndexOf("\\")).Length);
                    var batchName = Placeholders.All.Substitute(_config.Get(c => c.PatchTSettings.BatchName), true, i);

                    if (_patchTSettings.CopyScansToStagingFolder)
                    {
                        // Make staging folder full path
                        var fileName = savePath.Substring(savePath.LastIndexOf("\\") + 1);
                        var stagingDirPath = _patchTSettings.StagingFolderName!;
                        stagingDirPath += $"\\{batchName}-{barcodeName}";
                        stagingDirPath = Placeholders.All.Substitute(stagingDirPath, true, i) + fileName;

                        // Save to the staging folder
                        ProcessedImage[] tempImages = new ProcessedImage[images.Count];
                        for (int j = 0; j < images.Count; j++)
                            tempImages[j] = images[j].Clone();

                        i *= images.Count;
                        op.Start(stagingDirPath, placeholders, tempImages, _config.Get(c => c.ImageSettings), batch: true, batchLog: isBatchLog, i: i);
                        bool success = await op.Success;

                        if (success)
                        {
                            if (isBatchLog) {
                                Log.Info($": BATCH {batchName} Scanning ended <-------");
                                Log.Info($": Accepted batch {batchName} of {images.Count} ballots");
                            }

                            // Make a final folder path
                            dirPath += $"\\{batchName}";
                            Directory.CreateDirectory(dirPath);
                            dirPath += $"\\{batchName}-{barcodeName}";
                            var finalFolderPath = dirPath + savePath.Substring(savePath.LastIndexOf("\\") + 1);
                            if (isBatchLog)
                                Log.Info($": BATCH {batchName} Accepting {images.Count} ballots...");

                            op.Start(finalFolderPath, placeholders, images, _config.Get(c => c.ImageSettings), batch: true, destFolder: true, batchLog: isBatchLog, i: i);
                            success = await op.Success;
                            if (success && isBatchLog)
                                Log.Info($": Uploaded batch {batchName} to Secondary Path {dirPath.Substring(0, dirPath.Length - dirPath.Substring(dirPath.LastIndexOf("\\")).Length)}");
                        }
                        else {
                            if (isBatchLog)
                                Log.Info("All following files ballots skipped");

                            if (_patchTSettings.EraseBatchOnError)
                            {
                                DirectoryInfo di = new DirectoryInfo(_patchTSettings.StagingFolderName!);

                                foreach (var image in di.GetFiles())
                                    image.Delete();

                                Log.Info("Batch is erased");
                            }
                        }
                    }
                    else
                    {
                        dirPath += $"\\{batchName}\\{batchName}-";
                        subPath = dirPath + $"{barcodeName}" + savePath.Substring(savePath.LastIndexOf("\\") + 1);

                        op.Start(subPath, placeholders, images, _config.Get(c => c.ImageSettings), batch: true, batchLog: isBatchLog, i: i);
                        bool success = await op.Success;
                        if (success && isBatchLog)
                            Log.Info($": Uploaded batch {batchName} to Secondary Path {dirPath}");
                    }
                }
            }
        }

        private string GetSavePathExtension()
        {
            if (string.IsNullOrEmpty(_settings.SavePath))
            {
                throw new ArgumentException();
            }
            return Path.GetExtension(_settings.SavePath)!;
        }
    }
}