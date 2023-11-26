using NAPS2.ImportExport;

namespace NAPS2.Scan.Batch
{
    public class PatchTSettings
    {
        public string BatchName { get; set; } = UiStrings.DefaultBatchName;

        public string ?BarcodeName {get; set;}

        public string ?StagingFolderName { get; set; }

        public bool SeparatorSheetStartsNewBatch { get; set; }

        public bool IncludeSeparatorInBatch { get; set; }
        public bool UseBarcodeAsPlaceholder { get; set; }

        public bool RestartSheetsNumberingPerBatch { get; set; }

        public bool RestartBatchOnError { get; set; }

        public bool CopyScansToStagingFolder { get; set; }

        public bool EraseBatchOnError { get; set; }

        public bool UseBatchAsFolderName { get; set; }

        public bool CreatePatchTLog { get; set; }

        public string? BatchLogPath { get; set; }
    }
}
