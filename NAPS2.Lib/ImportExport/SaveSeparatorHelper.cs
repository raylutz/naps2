using NAPS2.Scan.Batch;

namespace NAPS2.ImportExport;

internal static class SaveSeparatorHelper
{
    /// <summary>
    /// Given a list of scans (each of which is a list of 1 or more images),
    /// split up the images into multiple lists as described by the SaveSeparator parameter.
    /// </summary>
    /// <param name="scans"></param>
    /// <param name="separator"></param>
    /// <param name="splitSize"></param>
    /// <returns></returns>
    public static IEnumerable<List<ProcessedImage>> SeparateScans(IEnumerable<IEnumerable<ProcessedImage>> scans, SaveSeparator separator, int splitSize = 1, Naps2Config config = null)
    {
        if (separator == SaveSeparator.FilePerScan)
        {
            foreach (var scan in scans)
            {
                var images = scan.ToList();
                if (images.Count > 0)
                {
                    yield return images;
                }
            }
        }
        else if (separator == SaveSeparator.FilePerPage)
        {
            splitSize = Math.Max(splitSize, 1);
            foreach (var scan in scans.Select(x => x.ToList()))
            {
                for (int i = 0; i < scan.Count; i += splitSize)
                {
                    yield return scan.Skip(i).Take(splitSize).ToList();
                }
            }
        }
        else if (separator == SaveSeparator.PatchT)
        {
            var images = new List<ProcessedImage>();
            foreach (var scan in scans)
            {
                foreach (var image in scan)
                {
                    bool isIncluded = config != null ? 
                        config.Get(c => c.PatchTSettings.IncludeSeparatorInBatch) : false;
                    if (config != null && config.Get(c => c.PatchTSettings.UseBarcodeAsPlaceholder))
                    {
                        if (image.PostProcessingData.Barcode.DetectedText != null)
                            config.User.Set(c => c.PatchTSettings.BarcodeName, image.PostProcessingData.Barcode.DetectedText);
                    }

                    if (image.PostProcessingData.Barcode.IsPatchT)
                    {
                        if (images.Count > 0)
                        {
                            yield return images;
                            images = new List<ProcessedImage>();
                        }
                        if (isIncluded)
                            images.Add(image);
                        else
                            image.Dispose();
                    }
                    else
                    {
                        images.Add(image);
                    }
                }
            }
            if (images.Count > 0)
            {
                yield return images;
            }
        }
        else
        {
            var images = scans.SelectMany(x => x.ToList()).ToList();
            if (images.Count > 0)
            {
                yield return images;
            }
        }
    }
}