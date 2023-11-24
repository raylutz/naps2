using Eto.Forms;
using NAPS2.Config.Model;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.ImportExport;

namespace NAPS2.EtoForms.Ui;

public class BatchPromptForm : EtoDialogBase
{
    private readonly Button _scanButton;
    private readonly LayoutVisibility _batchNameVis = new(false);
    private readonly FilePathWithPlaceholders _batchName;

    private readonly TransactionConfigScope<CommonConfig> _userTransact;
    private readonly Naps2Config _transactionConfig;

    public BatchPromptForm(Naps2Config config) : base(config)
    {
        var scanNextCommand = new ActionCommand(() =>
        {
            Result = true;
            Close();
        })
        {
            Text = UiStrings.Scan,
            Image = Icons.control_play_blue_small.ToEtoImage()
        };
        _scanButton = C.Button(scanNextCommand, ButtonImagePosition.Left);
        DefaultButton = _scanButton;

        _batchName = new(this);
        _batchName.TextChanged += _batchName_TextChanged;

        _userTransact = Config.User.BeginTransaction();
        _transactionConfig = Config.WithTransaction(_userTransact);
    }

    private void _batchName_TextChanged(object? sender, EventArgs e)
    {
        _userTransact.Set(c => c.PatchTSettings.BatchName, _batchName.Text);
        _userTransact.Commit();
    }

    public int ScanNumber { get; set; }

    public bool Result { get; private set; }

    protected override void BuildLayout()
    {
        Title = UiStrings.BatchPromptFormTitle;

        FormStateController.SaveFormState = false;
        FormStateController.RestoreFormState = false;
        FormStateController.Resizable = false;

        if (_transactionConfig.Get(c => c.PatchTSettings.UseBatchAsFolderName) == true)
            _batchNameVis.IsVisible = true;

        LayoutController.Content = L.Column(
            C.Label(string.Format(UiStrings.ReadyForScan, ScanNumber)).NaturalWidth(200),
            C.Filler(),
            L.Column(
                C.Label(UiStrings.BatchNameLabel),
                _batchName
            ).Visible(_batchNameVis),
            C.Filler(),
            L.Row(
                L.OkCancel(
                    _scanButton.Scale(),
                    C.CancelButton(this, UiStrings.Done).Scale())
            )
        );
    }
}