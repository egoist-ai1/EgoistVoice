using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Egoist.Voice.Services;

internal interface IMicrophoneDeviceCatalog : IDisposable
{
    event EventHandler? DevicesChanged;

    IReadOnlyList<MicrophoneDeviceInfo> GetActiveDevices();
    MMDevice OpenCaptureDevice(string? deviceId);
}

/// <summary>
/// Owns the Core Audio endpoint enumerator and reduces COM notifications to one safe inventory
/// invalidation event. Device identifiers never leave local settings or benchmark-free UI state.
/// </summary>
internal sealed class MicrophoneDeviceCatalog : IMicrophoneDeviceCatalog, IMMNotificationClient
{
    private readonly object _sync = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private int _notificationQueued;
    private volatile bool _disposed;

    internal MicrophoneDeviceCatalog()
    {
        var result = _enumerator.RegisterEndpointNotificationCallback(this);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<MicrophoneDeviceInfo> GetActiveDevices()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string? defaultId = null;
            try
            {
                if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
                {
                    using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(
                        DataFlow.Capture,
                        Role.Multimedia);
                    defaultId = defaultDevice.ID;
                }
            }
            catch (COMException)
            {
                // An unplug can race enumeration. The next notification refreshes the list.
            }

            var devices = _enumerator.EnumerateAudioEndPoints(
                DataFlow.Capture,
                DeviceState.Active);
            try
            {
                return devices
                    .Select(device => new MicrophoneDeviceInfo(
                        device.ID,
                        NormalizeName(device.FriendlyName),
                        string.Equals(device.ID, defaultId, StringComparison.Ordinal)))
                    .OrderByDescending(device => device.IsDefault)
                    .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
            finally
            {
                foreach (var device in devices)
                {
                    device.Dispose();
                }
            }
        }
    }

    public MMDevice OpenCaptureDevice(string? deviceId)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                var device = string.IsNullOrWhiteSpace(deviceId)
                    ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                    : _enumerator.GetDevice(deviceId);
                if (device.DataFlow != DataFlow.Capture || !device.State.HasFlag(DeviceState.Active))
                {
                    device.Dispose();
                    throw new MicrophoneUnavailableException("Выбранный микрофон сейчас недоступен.");
                }
                return device;
            }
            catch (MicrophoneUnavailableException)
            {
                throw;
            }
            catch (Exception exception) when (exception is COMException or ArgumentException)
            {
                throw new MicrophoneUnavailableException(
                    string.IsNullOrWhiteSpace(deviceId)
                        ? "Системный микрофон сейчас недоступен."
                        : "Выбранный микрофон сейчас недоступен.",
                    exception);
            }
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => RaiseDevicesChanged();
    public void OnDeviceAdded(string pwstrDeviceId) => RaiseDevicesChanged();
    public void OnDeviceRemoved(string deviceId) => RaiseDevicesChanged();

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if ((flow is DataFlow.Capture or DataFlow.All) && role == Role.Multimedia)
        {
            RaiseDevicesChanged();
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => RaiseDevicesChanged();

    private void RaiseDevicesChanged()
    {
        if (_disposed || Interlocked.Exchange(ref _notificationQueued, 1) != 0)
        {
            return;
        }

        // Core Audio explicitly calls these methods from its notification thread. Endpoint
        // enumeration and WASAPI restarts happen on the pool instead of blocking that callback.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Interlocked.Exchange(ref _notificationQueued, 0);
            if (!_disposed)
            {
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch (COMException)
            {
                // Process teardown is already the terminal lifetime boundary.
            }
            _enumerator.Dispose();
        }
    }

    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Микрофон без имени" : value.Trim();
}

internal static class MicrophoneSelectionPolicy
{
    internal static string? NormalizeDeviceId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static bool IsAvailable(
        string? selectedDeviceId,
        IReadOnlyList<MicrophoneDeviceInfo> devices) =>
        selectedDeviceId is null
            ? devices.Any(device => device.IsDefault)
            : devices.Any(device => string.Equals(
                device.Id,
                selectedDeviceId,
                StringComparison.Ordinal));

    internal static string DisplayName(
        string? selectedDeviceId,
        IReadOnlyList<MicrophoneDeviceInfo> devices)
    {
        if (selectedDeviceId is null)
        {
            var currentDefault = devices.FirstOrDefault(device => device.IsDefault);
            return currentDefault is null
                ? "Системный микрофон недоступен"
                : $"Системный · {currentDefault.Name}";
        }

        return devices.FirstOrDefault(device => string.Equals(
                device.Id,
                selectedDeviceId,
                StringComparison.Ordinal))?.Name
            ?? "Выбранный микрофон недоступен";
    }
}

internal enum MicrophoneTopologyAction
{
    InventoryChanged,
    RestartOnDefault,
    PauseUnavailable
}

internal static class MicrophoneLifecyclePolicy
{
    internal static MicrophoneTopologyAction EvaluateTopologyChange(
        string? selectedDeviceId,
        bool isPaused,
        string? activeDeviceId,
        IReadOnlyList<MicrophoneDeviceInfo> devices)
    {
        if (!MicrophoneSelectionPolicy.IsAvailable(selectedDeviceId, devices))
        {
            return MicrophoneTopologyAction.PauseUnavailable;
        }

        if (selectedDeviceId is null && !isPaused)
        {
            var defaultId = devices.First(device => device.IsDefault).Id;
            if (!string.Equals(defaultId, activeDeviceId, StringComparison.Ordinal))
            {
                return MicrophoneTopologyAction.RestartOnDefault;
            }
        }

        return MicrophoneTopologyAction.InventoryChanged;
    }
}
