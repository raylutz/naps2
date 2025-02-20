using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.Remoting;
using NAPS2.Scan;
using NAPS2.Scan.Exceptions;
using NAPS2.Scan.Internal;

namespace NAPS2.EtoForms.Ui;

public class SharedDeviceForm : EtoDialogBase
{
    private readonly IScanPerformer _scanPerformer;
    private readonly ErrorOutput _errorOutput;

    private readonly TextBox _displayName = new();
    private readonly RadioButton _wiaDriver;
    private readonly RadioButton _twainDriver;
    private readonly RadioButton _appleDriver;
    private readonly RadioButton _saneDriver;
    private readonly TextBox _deviceName = new() { Enabled = false };
    private readonly Button _chooseDevice = new() { Text = UiStrings.ChooseDevice };
    private readonly Button _ok = new() { Text = UiStrings.OK };
    private readonly Button _cancel = new() { Text = UiStrings.Cancel };

    private ScanDevice? _currentDevice;
    private bool _result;
    private bool _suppressChangeEvent;

    public SharedDeviceForm(Naps2Config config, IScanPerformer scanPerformer, ErrorOutput errorOutput) : base(config)
    {
        _scanPerformer = scanPerformer;
        _errorOutput = errorOutput;
        _wiaDriver = new RadioButton { Text = UiStrings.WiaDriver };
        _twainDriver = new RadioButton(_wiaDriver) { Text = UiStrings.TwainDriver };
        _appleDriver = new RadioButton(_wiaDriver) { Text = UiStrings.AppleDriver };
        _saneDriver = new RadioButton(_wiaDriver) { Text = UiStrings.SaneDriver };
        _wiaDriver.CheckedChanged += Driver_CheckedChanged;
        _twainDriver.CheckedChanged += Driver_CheckedChanged;
        _appleDriver.CheckedChanged += Driver_CheckedChanged;
        _saneDriver.CheckedChanged += Driver_CheckedChanged;
        _ok.Click += Ok_Click;
        _cancel.Click += Cancel_Click;

        _chooseDevice.Click += ChooseDevice;
        _deviceName.KeyDown += DeviceName_KeyDown;
    }

    protected override void BuildLayout()
    {
        // TODO: Don't show if only one driver is available
        var driverElements = new List<LayoutElement>();
        if (PlatformCompat.System.IsWiaDriverSupported)
        {
            driverElements.Add(_wiaDriver.Scale());
        }
        if (PlatformCompat.System.IsTwainDriverSupported)
        {
            driverElements.Add(_twainDriver.Scale());
        }
        if (PlatformCompat.System.IsAppleDriverSupported)
        {
            driverElements.Add(_appleDriver.Scale());
        }
        if (PlatformCompat.System.IsSaneDriverSupported)
        {
            driverElements.Add(_saneDriver.Scale());
        }

        Title = UiStrings.SharedDeviceFormTitle;
        Icon = new Icon(1f, Icons.wireless16.ToEtoImage());

        FormStateController.DefaultExtraLayoutSize = new Size(60, 0);
        FormStateController.FixedHeightLayout = true;

        LayoutController.Content = L.Column(
            L.Row(
                L.Column(
                    C.Label(UiStrings.DisplayNameLabel),
                    _displayName,
                    L.Row(
                        driverElements.ToArray()
                    ),
                    C.Spacer(),
                    C.Label(UiStrings.DeviceLabel),
                    L.Row(
                        _deviceName.Scale(),
                        _chooseDevice
                    )
                ).Scale(),
                new ImageView { Image = Icons.scanner_48.ToEtoImage() }
            ),
            C.Filler(),
            L.Row(
                C.Filler(),
                L.OkCancel(
                    _ok,
                    _cancel)
            )
        );
    }

    public bool Result => _result;

    public SharedDevice SharedDevice { get; set; } = new()
    {
        Name = "",
        Device = null,
        Driver = ScanOptionsValidator.SystemDefaultDriver
    };

    public ScanDevice? CurrentDevice
    {
        get => _currentDevice;
        set
        {
            _currentDevice = value;
            _deviceName.Text = value?.Name ?? "";
        }
    }

    private Driver DeviceDriver
    {
        get => _twainDriver.Checked ? Driver.Twain
            : _wiaDriver.Checked ? Driver.Wia
            : _appleDriver.Checked ? Driver.Apple
            : _saneDriver.Checked ? Driver.Sane
            : ScanOptionsValidator.SystemDefaultDriver;
        set
        {
            if (value == Driver.Twain)
            {
                _twainDriver.Checked = true;
            }
            else if (value == Driver.Wia)
            {
                _wiaDriver.Checked = true;
            }
            else if (value == Driver.Apple)
            {
                _appleDriver.Checked = true;
            }
            else if (value == Driver.Sane)
            {
                _saneDriver.Checked = true;
            }
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Don't trigger any onChange events
        _suppressChangeEvent = true;

        _displayName.Text = SharedDevice.Name;
        CurrentDevice ??= SharedDevice.Device;

        DeviceDriver = SharedDevice.Driver;

        // Start triggering onChange events again
        _suppressChangeEvent = false;
    }

    private async void ChooseDevice(object? sender, EventArgs args)
    {
        SharedDevice = SharedDevice with { Driver = DeviceDriver };
        try
        {
            var profile = new ScanProfile { DriverName = DeviceDriver.ToString().ToLowerInvariant() };
            var device = await _scanPerformer.PromptForDevice(profile, NativeHandle);
            if (device != null)
            {
                if (string.IsNullOrEmpty(_displayName.Text) ||
                    CurrentDevice != null && CurrentDevice.Name == _displayName.Text)
                {
                    _displayName.Text = device.Name;
                }
                CurrentDevice = device;
            }
        }
        catch (ScanDriverException ex)
        {
            if (ex is ScanDriverUnknownException)
            {
                Log.ErrorException(ex.Message, ex.InnerException!);
                _errorOutput.DisplayError(ex.Message, ex);
            }
            else
            {
                _errorOutput.DisplayError(ex.Message);
            }
        }
        catch (Exception ex)
        {
            Log.ErrorException(ex.Message, ex);
            _errorOutput.DisplayError(ex.Message, ex);
        }
    }

    private void SaveSettings()
    {
        // TODO: What to do if device not selected?
        SharedDevice = new SharedDevice
        {
            Name = _displayName.Text,
            Driver = DeviceDriver,
            Device = CurrentDevice,
        };
    }

    private void Ok_Click(object? sender, EventArgs e)
    {
        // Note: If CurrentDevice is null, that's fine. A prompt will be shown when scanning.

        if (_displayName.Text == "")
        {
            _errorOutput.DisplayError(MiscResources.NameMissing);
            return;
        }
        _result = true;
        SaveSettings();
        Close();
    }

    private void Cancel_Click(object? sender, EventArgs e)
    {
        Close();
    }

    private void Driver_CheckedChanged(object? sender, EventArgs e)
    {
        if (((RadioButton) sender!).Checked && !_suppressChangeEvent)
        {
            SharedDevice = SharedDevice with { Device = null };
            CurrentDevice = null;
        }
    }

    private void DeviceName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Keys.Delete)
        {
            CurrentDevice = null;
        }
    }
}