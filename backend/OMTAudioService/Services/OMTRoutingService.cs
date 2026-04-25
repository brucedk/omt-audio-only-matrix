using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using libomtnet;
using NAudio.Wave;

namespace OMTAudioService.Services;

public class OMTRoutingService : BackgroundService
{
    private readonly ILogger<OMTRoutingService> _logger;
    private readonly OMTDiscovery _discovery;
    
    // Config states
    private ConcurrentDictionary<string, string> _channelRouting = new();
    private ConcurrentDictionary<string, ActiveRoute> _activeRoutes = new();
    
    public OMTRoutingService(ILogger<OMTRoutingService> logger)
    {
        _logger = logger;
        _discovery = OMTDiscovery.GetInstance();
    }

    public string[] GetDiscoveredSenders()
    {
        return _discovery.GetAddresses();
    }

    public void UpdateRouting(Dictionary<string, string> routing)
    {
        _channelRouting.Clear();
        foreach (var kvp in routing)
        {
            _channelRouting[kvp.Key] = kvp.Value;
        }
        ApplyRouting();
    }
    
    public Dictionary<string, string> GetRouting()
    {
        return new Dictionary<string, string>(_channelRouting);
    }

    private void ApplyRouting()
    {
        var oldRoutes = _activeRoutes.Keys.ToList();
        var newRoutes = new Dictionary<string, (string Source, string Output)>();

        foreach (var kvp in _channelRouting)
        {
            string source = "";
            string output = "";
            try {
                var doc = JsonDocument.Parse(kvp.Value);
                if (doc.RootElement.TryGetProperty("source", out var sProp)) source = sProp.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("output", out var oProp)) output = oProp.GetString() ?? "";
            } catch {
                source = kvp.Value;
            }

            if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(output))
            {
                newRoutes[kvp.Key] = (source, output);
            }
        }

        // Remove old/changed routes
        foreach (var key in oldRoutes)
        {
            if (!newRoutes.ContainsKey(key) || 
                newRoutes[key].Source != _activeRoutes[key].Source || 
                newRoutes[key].Output != _activeRoutes[key].OutputId)
            {
                if (_activeRoutes.TryRemove(key, out var route))
                {
                    route.Dispose();
                }
            }
        }

        // Add new routes
        foreach (var kvp in newRoutes)
        {
            if (!_activeRoutes.ContainsKey(kvp.Key))
            {
                var route = new ActiveRoute(kvp.Value.Source, kvp.Value.Output, _logger);
                _activeRoutes[kvp.Key] = route;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OMT Routing Service is starting.");
        
        // Wait a few seconds for discovery to start
        await Task.Delay(3000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var senders = _discovery.GetAddresses();
            // _logger.LogDebug($"Discovered {senders.Length} OMT senders.");
            await Task.Delay(5000, stoppingToken);
        }
    }

    private class ActiveRoute : IDisposable
    {
        public string Source { get; }
        public string OutputId { get; }
        private CancellationTokenSource _cts;
        private Task _runTask;
        private ILogger _logger;

        public ActiveRoute(string source, string outputId, ILogger logger)
        {
            Source = source;
            OutputId = outputId;
            _logger = logger;
            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunAsync(_cts.Token));
        }

        private async Task RunAsync(CancellationToken token)
        {
            try 
            {
                using var omtReceive = new OMTReceive(Source, OMTFrameType.Audio, OMTPreferredVideoFormat.UYVY, OMTReceiveFlags.None);

                var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var device = enumerator.GetDevice(OutputId);

                using var wasapiOut = new NAudio.Wave.WasapiOut(device, NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 20);
                
                BufferedWaveProvider waveProvider = null;
                byte[] buffer = new byte[192000]; 

                _logger.LogInformation($"Starting audio routing from {Source} to {device.FriendlyName}");

                while (!token.IsCancellationRequested)
                {
                    OMTMediaFrame frame = new OMTMediaFrame();
                    if (omtReceive.Receive(OMTFrameType.Audio, 500, ref frame))
                    {
                        if (frame.Type == OMTFrameType.Audio && frame.Data != IntPtr.Zero && frame.DataLength > 0)
                        {
                            int bitsPerSample = (frame.DataLength / frame.Channels) / frame.SamplesPerChannel * 8;
                            if (waveProvider == null)
                            {
                                WaveFormat format;
                                if (bitsPerSample == 32) 
                                    format = WaveFormat.CreateIeeeFloatWaveFormat(frame.SampleRate, frame.Channels);
                                else 
                                    format = new WaveFormat(frame.SampleRate, bitsPerSample, frame.Channels);
                                
                                waveProvider = new BufferedWaveProvider(format)
                                {
                                    DiscardOnBufferOverflow = true,
                                    BufferDuration = TimeSpan.FromSeconds(2)
                                };
                                wasapiOut.Init(waveProvider);
                                wasapiOut.Play();
                            }

                            if (bitsPerSample == 32)
                            {
                                int numSamples = frame.SamplesPerChannel;
                                int channels = frame.Channels;
                                float[] planarData = new float[numSamples * channels];
                                Marshal.Copy(frame.Data, planarData, 0, planarData.Length);

                                float[] interleavedData = new float[numSamples * channels];
                                for (int ch = 0; ch < channels; ch++)
                                {
                                    for (int i = 0; i < numSamples; i++)
                                    {
                                        interleavedData[i * channels + ch] = planarData[ch * numSamples + i];
                                    }
                                }

                                int bytesCount = interleavedData.Length * 4;
                                if (buffer.Length < bytesCount) buffer = new byte[bytesCount];
                                Buffer.BlockCopy(interleavedData, 0, buffer, 0, bytesCount);
                                waveProvider.AddSamples(buffer, 0, bytesCount);
                            }
                            else if (bitsPerSample == 16)
                            {
                                int numSamples = frame.SamplesPerChannel;
                                int channels = frame.Channels;
                                short[] planarData = new short[numSamples * channels];
                                Marshal.Copy(frame.Data, planarData, 0, planarData.Length);

                                short[] interleavedData = new short[numSamples * channels];
                                for (int ch = 0; ch < channels; ch++)
                                {
                                    for (int i = 0; i < numSamples; i++)
                                    {
                                        interleavedData[i * channels + ch] = planarData[ch * numSamples + i];
                                    }
                                }

                                int bytesCount = interleavedData.Length * 2;
                                if (buffer.Length < bytesCount) buffer = new byte[bytesCount];
                                Buffer.BlockCopy(interleavedData, 0, buffer, 0, bytesCount);
                                waveProvider.AddSamples(buffer, 0, bytesCount);
                            }
                            else
                            {
                                if (buffer.Length < frame.DataLength)
                                {
                                    buffer = new byte[frame.DataLength];
                                }
                                Marshal.Copy(frame.Data, buffer, 0, frame.DataLength);
                                waveProvider.AddSamples(buffer, 0, frame.DataLength);
                            }
                        }
                    }
                }
                
                wasapiOut.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in route for source {Source} to output {OutputId}");
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _runTask.Wait(2000); } catch { }
            _cts.Dispose();
        }
    }
}
