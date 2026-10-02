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
    private readonly DeviceNotificationDispatcher _notifications;
    private volatile bool _disposed;

    internal MicrophoneDeviceCatalog()
    {
        _notifications = new(() => { if (!_disposed) DevicesChanged?.Invoke(this, EventArgs.Empty); });
        try
        {
            var result = _enumerator.RegisterEndpointNotificationCallback(this);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }
        catch
        {
            _notifications.Dispose();
            _enumerator.Dispose();
            throw;
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
            return MicrophoneInventoryReader.Read(devices, device => new MicrophoneDeviceInfo(
                device.ID, NormalizeName(device.FriendlyName),
                string.Equals(device.ID, defaultId, StringComparison.Ordinal)));
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
        if (!_disposed) _notifications.Request();
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
            _notifications.Dispose();
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

// MMDeviceCollection creates new wrappers on every enumeration. Read/dispose each owned
// wrapper in one pass; a second foreach would dispose different objects and double the work.
internal static class MicrophoneInventoryReader
{
    internal static IReadOnlyList<MicrophoneDeviceInfo> Read<TDevice>(
        IEnumerable<TDevice> devices, Func<TDevice, MicrophoneDeviceInfo> read) where TDevice : IDisposable
    {
        var result = new List<MicrophoneDeviceInfo>();
        foreach (var device in devices)
        {
            using (device) result.Add(read(device));
        }
        return result.OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

// One outstanding worker, with one dirty bit for notifications arriving during enumeration.
// No timer, idle polling, or waits on the Core Audio notification thread.
internal sealed class DeviceNotificationDispatcher : IDisposable
{
    private readonly Action _notify;
    private readonly Action<Action> _schedule;
    private int _queued;
    private int _pending;
    private int _disposed;

    internal DeviceNotificationDispatcher(Action notify, Action<Action>? schedule = null)
    {
        _notify = notify;
        _schedule = schedule ?? (work => ThreadPool.QueueUserWorkItem(_ => work()));
    }

    internal void Request()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Exchange(ref _pending, 1);
        ScheduleIfNeeded();
    }

    private void ScheduleIfNeeded()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.CompareExchange(ref _queued, 1, 0) != 0) return;
        _schedule(Drain);
    }

    private void Drain()
    {
        try
        {
            while (Volatile.Read(ref _disposed) == 0 && Interlocked.Exchange(ref _pending, 0) != 0)
            {
                try { _notify(); }
                catch (Exception exception) { AppLog.Write("Microphone inventory subscriber threw", exception); }
            }
        }
        finally
        {
            Volatile.Write(ref _queued, 0);
            if (Volatile.Read(ref _pending) != 0) ScheduleIfNeeded();
        }
    }

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);
        Interlocked.Exchange(ref _pending, 0);
    }
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
