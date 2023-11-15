﻿namespace NAPS2.ImportExport.Images;

internal class SaveImagesOperation : OperationBase
{
    private readonly IOverwritePrompt _overwritePrompt;
    private readonly ImageContext _imageContext;

    public SaveImagesOperation(IOverwritePrompt overwritePrompt, ImageContext imageContext)
    {
        _overwritePrompt = overwritePrompt;
        _imageContext = imageContext;

        ProgressTitle = MiscResources.SaveImagesProgress;
        AllowCancel = true;
        AllowBackground = true;
    }

    public string? FirstFileSaved { get; private set; }
    private bool _destFolder = false;
    private bool _isBatchLog = false;

    /// <summary>
    /// Saves the provided collection of images to a file with the given name. The image type is inferred from the file extension.
    /// If multiple images are provided, they will be saved to files with numeric identifiers, e.g. img1.jpg, img2.jpg, etc..
    /// </summary>
    /// <param name="fileName">The name of the file to save. For multiple images, this is modified by appending a number before the extension.</param>
    /// <param name="placeholders"></param>
    /// <param name="images">The collection of images to save.</param>
    /// <param name="overwriteFile"></param>
    /// <param name="batch"></param>
    /// <param name="imageSettings"></param>
    public bool Start(string fileName, Placeholders placeholders, IList<ProcessedImage> images,
        ImageSettings imageSettings, string? overwriteFile = null, bool batch = false, bool destFolder = false, bool batchLog = false, int i = 0)
    {
        _destFolder = destFolder;
        _isBatchLog = batchLog;
        Status = new OperationStatus
        {
            MaxProgress = images.Count
        };

        RunAsync(() =>
        {
            try
            {
                var subFileName = placeholders.Substitute(fileName, batch);
                if (Directory.Exists(subFileName))
                {
                    // Not supposed to be a directory, but ok...
                    fileName = Path.Combine(subFileName, "$(n).jpg");
                    subFileName = placeholders.Substitute(fileName, batch);
                }
                var format = ImageContext.GetFileFormatFromExtension(subFileName);
                if (format == ImageFileFormat.Unspecified)
                {
                    throw new ArgumentException($"Could not infer file format from extension: {subFileName}");
                }

                if (format == ImageFileFormat.Tiff && !imageSettings.SinglePageTiff)
                {
                    if (File.Exists(subFileName))
                    {
                        if (subFileName != overwriteFile &&
                            _overwritePrompt.ConfirmOverwrite(subFileName) != OverwriteResponse.Yes)
                        {
                            return false;
                        }
                        if (FileSystemHelper.IsFileInUse(subFileName, out var ex))
                        {
                            InvokeError(MiscResources.FileInUse, ex!);
                            return false;
                        }
                    }
                    Status.StatusText = string.Format(MiscResources.SavingFormat, Path.GetFileName(subFileName));
                    FirstFileSaved = subFileName;
                    FileSystemHelper.EnsureParentDirExists(subFileName);
                    using var renderedImages = images.Select(x => x.Render()).ToDisposableList();
                    return _imageContext.TiffWriter.SaveTiff(renderedImages.InnerList, subFileName,
                        imageSettings.TiffCompression.ToTiffCompressionType(), ProgressHandler);
                }

                //int i = 0;
                int digits = (int) Math.Floor(Math.Log10(images.Count)) + 1;
                foreach (ProcessedImage image in images)
                {
                    if (CancelToken.IsCancellationRequested)
                    {
                        return false;
                    }
                    Status.CurrentProgress = i;
                    InvokeStatusChanged();

                    if (images.Count == 1 && File.Exists(subFileName))
                    {
                        var overwriteResponse = subFileName == overwriteFile
                            ? OverwriteResponse.Yes
                            : _overwritePrompt.ConfirmOverwrite(subFileName);
                        if (overwriteResponse == OverwriteResponse.No)
                        {
                            continue;
                        }
                        if (overwriteResponse == OverwriteResponse.Abort)
                        {
                            return false;
                        }
                        if (FileSystemHelper.IsFileInUse(subFileName, out var ex))
                        {
                            InvokeError(MiscResources.FileInUse, ex!);
                            return false;
                        }
                    }
                    if (images.Count == 1)
                    {
                        Status.StatusText = string.Format(MiscResources.SavingFormat, Path.GetFileName(subFileName));
                        InvokeStatusChanged();
                        DoSaveImage(image, subFileName, format, imageSettings);
                        FirstFileSaved = subFileName;
                    }
                    else
                    {
                        var fileNameN = placeholders.Substitute(fileName, true, i,
                            digits);
                        Status.StatusText = string.Format(MiscResources.SavingFormat, Path.GetFileName(fileNameN));
                        InvokeStatusChanged();
                        DoSaveImage(image, fileNameN, format, imageSettings);

                        if (i == 0)
                        {
                            FirstFileSaved = fileNameN;
                        }
                    }
                    i++;
                }

                return FirstFileSaved != null;
            }
            catch (UnauthorizedAccessException ex)
            {
                InvokeError(MiscResources.DontHavePermission, ex);
            }
            catch (Exception ex)
            {
                Log.ErrorException(MiscResources.ErrorSaving, ex);
                InvokeError(MiscResources.ErrorSaving, ex);
            }
            return false;
        });
        Success.ContinueWith(task =>
        {
            if (task.Result)
            {
                Log.Event(EventType.SaveImages, new EventParams
                {
                    Name = MiscResources.SaveImages,
                    Pages = images.Count,
                    FileFormat = Path.GetExtension(fileName)
                });
            }
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        return true;
    }

    private void DoSaveImage(ProcessedImage image, string path, ImageFileFormat format, ImageSettings imageSettings)
    {
        FileSystemHelper.EnsureParentDirExists(path);
        using var renderedImage = image.Render();
        if (format == ImageFileFormat.Tiff)
        {
            bool status = _imageContext.TiffWriter.SaveTiff(new[] { renderedImage }, path,
                imageSettings.TiffCompression.ToTiffCompressionType(), CancelToken);
            if (_isBatchLog)
            {
                if (!_destFolder)
                {
                    if (status)
                        Log.Info($": Ballot {path.Substring(path.LastIndexOf("\\") + 1)}: {status}");
                    else
                    {
                        Log.Info($": Ballot {path.Substring(path.LastIndexOf("\\") + 1)}:        - Problem Ballot - Skipped");
                        Log.Info($"Stopped on ballot {path.Substring(path.LastIndexOf("\\") + 1)}. Ballot misread.");
                    }
                }
            }
        }
        else
        {
            // Quality will be ignored when not needed
            // TODO: Scale quality differently for jpeg2000?
            var quality = imageSettings.JpegQuality.Clamp(0, 100);
            renderedImage.Save(path, format, new ImageSaveOptions { Quality = quality });
        }
    }
}