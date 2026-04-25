using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using libomtnet;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace OMTAudioService.Services;

public class OMTBroadcastService : IDisposable
{
    private readonly ILogger<OMTBroadcastService> _logger;
    private ConcurrentDictionary<string, ActiveBroadcast> _activeBroadcasts = new();
    private ConcurrentDictionary<string, string> _broadcastConfig = new();

    public OMTBroadcastService(ILogger<OMTBroadcastService> logger)
    {
        _logger = logger;
    }

    public void UpdateBroadcasts(Dictionary<string, string> config)
    {
        _broadcastConfig.Clear();
        foreach (var kvp in config)
        {
            _broadcastConfig[kvp.Key] = kvp.Value;
        }
        ApplyConfig();
    }

    public Dictionary<string, string> GetBroadcasts()
    {
        return new Dictionary<string, string>(_broadcastConfig);
    }

    private void ApplyConfig()
    {
        var newRoutes = new Dictionary<string, string>(_broadcastConfig);

        foreach (var key in _activeBroadcasts.Keys.ToList())
        {
            if (!newRoutes.ContainsKey(key) || newRoutes[key] != _activeBroadcasts[key].Name)
            {
                if (_activeBroadcasts.TryRemove(key, out var broadcast))
                {
                    broadcast.Dispose();
                }
            }
        }

        foreach (var kvp in newRoutes)
        {
            if (!_activeBroadcasts.ContainsKey(kvp.Key))
            {
                try {
                    var broadcast = new ActiveBroadcast(kvp.Key, kvp.Value, _logger);
                    _activeBroadcasts[kvp.Key] = broadcast;
                } catch (Exception ex) {
                    _logger.LogError(ex, $"Failed to start broadcast {kvp.Value} on device {kvp.Key}");
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (var broadcast in _activeBroadcasts.Values)
        {
            broadcast.Dispose();
        }
        _activeBroadcasts.Clear();
    }

    private class ActiveBroadcast : IDisposable
    {
        public string DeviceId { get; }
        public string Name { get; }
        
        private WasapiCapture _capture;
        private OMTSend _send;
        private ILogger _logger;
        private byte[] _planarBuffer;

        public ActiveBroadcast(string deviceId, string name, ILogger logger)
        {
            DeviceId = deviceId;
            Name = name;
            _logger = logger;

            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(deviceId);

            _send = new OMTSend(name, OMTQuality.High);

            _capture = new WasapiCapture(device);
            _planarBuffer = new byte[_capture.WaveFormat.AverageBytesPerSecond]; // roughly 1s buffer

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            
            _logger.LogInformation($"Started broadcasting capture of {device.FriendlyName} as '{name}'");
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            var format = _capture.WaveFormat;
            int channels = format.Channels;
            int bytesPerSample = format.BitsPerSample / 8;
            int numSamples = (e.BytesRecorded / channels) / bytesPerSample;

            if (_planarBuffer.Length < e.BytesRecorded)
            {
                _planarBuffer = new byte[e.BytesRecorded];
            }

            // De-interleave from NAudio format to OMT Planar format
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
            {
                unsafe
                {
                    fixed (byte* srcByte = e.Buffer, dstByte = _planarBuffer)
                    {
                        float* src = (float*)srcByte;
                        float* dst = (float*)dstByte;
                        for (int c = 0; c < channels; c++)
                        {
                            for (int s = 0; s < numSamples; s++)
                            {
                                dst[c * numSamples + s] = src[s * channels + c];
                            }
                        }
                    }
                }
            }
            else
            {
                // Fallback, just copy interleaved
                Buffer.BlockCopy(e.Buffer, 0, _planarBuffer, 0, e.BytesRecorded);
            }

            OMTMediaFrame frame = new OMTMediaFrame();
            frame.Type = OMTFrameType.Audio;
            frame.Channels = channels;
            frame.SampleRate = format.SampleRate;
            frame.SamplesPerChannel = numSamples;
            frame.DataLength = e.BytesRecorded;
            frame.Timestamp = DateTime.UtcNow.Ticks * 100; // 100ns units

            unsafe
            {
                fixed (byte* ptr = _planarBuffer)
                {
                    frame.Data = (IntPtr)ptr;
                    _send.Send(frame);
                }
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                _logger.LogError(e.Exception, $"Recording stopped for {Name} with error");
            }
        }

        public void Dispose()
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
                _capture.Dispose();
                _capture = null;
            }
            if (_send != null)
            {
                if (_send is IDisposable disposableSend)
                {
                    disposableSend.Dispose();
                }
                _send = null;
            }
        }
    }
}
