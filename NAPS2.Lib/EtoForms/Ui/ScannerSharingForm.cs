using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Remoting;

namespace NAPS2.EtoForms.Ui;

public class ScannerSharingForm : EtoDialogBase
{
    private readonly ISharedDeviceManager _sharedDeviceManager;

    private readonly IListView<SharedDevice> _listView;

    private readonly Command _addCommand;
    private readonly Command _editCommand;
    private readonly Command _deleteCommand;

    public ScannerSharingForm(Naps2Config config, SharedDevicesListViewBehavior listViewBehavior, ISharedDeviceManager sharedDeviceManager)
        : base(config)
    {
        _sharedDeviceManager = sharedDeviceManager;

        _listView = EtoPlatform.Current.CreateListView(listViewBehavior);
        _addCommand = new ActionCommand(DoAdd)
        {
            MenuText = UiStrings.New,
            Image = Icons.add_small.ToEtoImage()
        };
        _editCommand = new ActionCommand(DoEdit)
        {
            MenuText = UiStrings.Edit,
            Image = Icons.pencil_small.ToEtoImage()
        };
        _deleteCommand = new ActionCommand(DoDelete)
        {
            MenuText = UiStrings.Delete,
            Image = Icons.cross_small.ToEtoImage(),
            Shortcut = Keys.Delete
        };

        _listView.ImageSize = 48;
        _listView.SelectionChanged += SelectionChanged;

        _addCommand.Enabled = true;
        _editCommand.Enabled = false;
        _deleteCommand.Enabled = false;
        ReloadDevices();

        var contextMenu = new ContextMenu();
        _listView.ContextMenu = contextMenu;
        contextMenu.AddItems(
            new ButtonMenuItem(_editCommand),
            new ButtonMenuItem(_deleteCommand));
        contextMenu.Opening += ContextMenuOpening;
    }

    protected override void BuildLayout()
    {
        Title = UiStrings.ScannerSharingFormTitle;
        Icon = new Icon(1f, Icons.wireless16.ToEtoImage());

        FormStateController.DefaultExtraLayoutSize = new Size(200, 0);

        LayoutController.Content = L.Column(
            C.Label(UiStrings.ScannerSharingIntro).DynamicWrap(400),
            C.Spacer(),
            _listView.Control.Scale(),
            L.Row(
                L.Column(
                    L.Row(
                        C.Button(_addCommand, ButtonImagePosition.Left),
                        C.Button(_editCommand, ButtonImagePosition.Left),
                        C.Button(_deleteCommand, ButtonImagePosition.Left)
                    )
                ),
                C.Filler(),
                C.CancelButton(this, UiStrings.Done)
            ));
    }

    public Action<ProcessedImage>? ImageCallback { get; set; }

    private SharedDevice? SelectedDevice => _listView.Selection.SingleOrDefault();

    private void ReloadDevices()
    {
        _listView.SetItems(_sharedDeviceManager.SharedDevices);
    }

    private void SelectionChanged(object? sender, EventArgs e)
    {
        _editCommand.Enabled = _listView.Selection.Count == 1;
        _deleteCommand.Enabled = _listView.Selection.Count > 0;
    }

    private void ContextMenuOpening(object? sender, EventArgs e)
    {
        _editCommand.Enabled = SelectedDevice != null;
        _deleteCommand.Enabled = SelectedDevice != null;
    }

    private void DoAdd()
    {
        var fedit = FormFactory.Create<SharedDeviceForm>();
        fedit.ShowModal();
        if (fedit.Result)
        {
            _sharedDeviceManager.AddSharedDevice(fedit.SharedDevice);
            ReloadDevices();
        }
    }

    private void DoEdit()
    {
        var originalDevice = SelectedDevice;
        if (originalDevice != null)
        {
            var fedit = FormFactory.Create<SharedDeviceForm>();
            fedit.SharedDevice = originalDevice;
            fedit.ShowModal();
            if (fedit.Result)
            {
                _sharedDeviceManager.ReplaceSharedDevice(originalDevice, fedit.SharedDevice);
                ReloadDevices();
            }
        }
    }

    private void DoDelete()
    {
        if (SelectedDevice != null)
        {
            string message = string.Format(UiStrings.ConfirmDeleteSharedDevice, SelectedDevice.Name);
            if (MessageBox.Show(message, MiscResources.Delete, MessageBoxButtons.YesNo, MessageBoxType.Warning) ==
                DialogResult.Yes)
            {
                _sharedDeviceManager.RemoveSharedDevice(SelectedDevice);
                ReloadDevices();
            }
        }
    }
}