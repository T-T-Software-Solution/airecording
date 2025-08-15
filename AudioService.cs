using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace AIConsoleAppRecording;

public class AudioService : IDisposable
{
    private WasapiLoopbackCapture? _systemCapture;
    private WaveInEvent? _micCapture;
    private WaveFileWriter? _systemWriter;
    private WaveFileWriter? _micWriter;
    private WaveFileWriter? _mixedWriter;
    private readonly Stopwatch _stopwatch = new();
    private long _bytesRecorded = 0;
    private float _currentSystemVolume = 0;
    private float _currentMicVolume = 0;
    private bool _isRecording = false;
    private CancellationTokenSource? _recordingCancellation;

    public async Task<string> RecordAudioAsync(RecordingSettings settings)
    {
        Console.WriteLine($"\nPress [Enter] to start recording ({GetModeDescription(settings.Mode)})...");
        if (settings.MicrophoneDeviceNumber.HasValue)
        {
            Console.WriteLine($"Using microphone device #{settings.MicrophoneDeviceNumber}");
        }
        Console.ReadLine();

        var tempPath = Path.Combine(Path.GetTempPath(), $"recording_{Guid.NewGuid()}.wav");
        
        try
        {
            StartRecording(tempPath, settings);
            
            _recordingCancellation = new CancellationTokenSource();
            var progressTask = Task.Run(() => DisplayProgress(_recordingCancellation.Token, settings.Mode));
            
            Console.ReadLine();
            
            _recordingCancellation.Cancel();
            await progressTask;
            
            StopRecording();
            
            // If recording both, mix the audio files
            if (settings.Mode == RecordingMode.Both)
            {
                var mixedPath = await MixAudioFiles(tempPath);
                if (!string.IsNullOrEmpty(mixedPath))
                {
                    tempPath = mixedPath;
                }
            }
            
            var fileInfo = new FileInfo(tempPath);
            Console.WriteLine($"\nRecording saved to: {tempPath}");
            Console.WriteLine($"File size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB");
            
            // Validate file size (minimum ~10KB for 0.1 seconds of audio)
            if (fileInfo.Length < 10240)
            {
                throw new InvalidOperationException("Recording is too short or no audio was captured.");
            }
            
            return tempPath;
        }
        catch (Exception ex)
        {
            StopRecording();
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw new InvalidOperationException($"Failed to record audio: {ex.Message}", ex);
        }
    }

    private void StartRecording(string filePath, RecordingSettings settings)
    {
        if (settings.Mode == RecordingMode.MicrophoneOnly || settings.Mode == RecordingMode.Both)
        {
            _micCapture = new WaveInEvent();
            
            // Set specific microphone device if provided
            if (settings.MicrophoneDeviceNumber.HasValue)
            {
                _micCapture.DeviceNumber = settings.MicrophoneDeviceNumber.Value;
            }
            
            _micCapture.WaveFormat = new WaveFormat(44100, 16, 1);
            
            var micPath = settings.Mode == RecordingMode.Both 
                ? Path.ChangeExtension(filePath, ".mic.wav")
                : filePath;
            
            _micWriter = new WaveFileWriter(micPath, _micCapture.WaveFormat);
            
            _micCapture.DataAvailable += (sender, e) =>
            {
                if (_micWriter != null && e.BytesRecorded > 0)
                {
                    _micWriter.Write(e.Buffer, 0, e.BytesRecorded);
                    if (settings.Mode == RecordingMode.MicrophoneOnly)
                        _bytesRecorded += e.BytesRecorded;
                    
                    UpdateVolume(e.Buffer, e.BytesRecorded, ref _currentMicVolume);
                }
            };
            
            _micCapture.StartRecording();
        }

        if (settings.Mode == RecordingMode.SystemOnly || settings.Mode == RecordingMode.Both)
        {
            _systemCapture = new WasapiLoopbackCapture();
            
            var systemPath = settings.Mode == RecordingMode.Both 
                ? Path.ChangeExtension(filePath, ".system.wav")
                : filePath;
            
            _systemWriter = new WaveFileWriter(systemPath, _systemCapture.WaveFormat);
            
            _systemCapture.DataAvailable += (sender, e) =>
            {
                if (_systemWriter != null && e.BytesRecorded > 0)
                {
                    _systemWriter.Write(e.Buffer, 0, e.BytesRecorded);
                    if (settings.Mode != RecordingMode.Both)
                        _bytesRecorded += e.BytesRecorded;
                    
                    UpdateVolume(e.Buffer, e.BytesRecorded, ref _currentSystemVolume);
                }
            };
            
            _systemCapture.StartRecording();
        }

        _isRecording = true;
        _stopwatch.Start();
        _bytesRecorded = 0;
    }

    private void UpdateVolume(byte[] buffer, int bytesRecorded, ref float volume)
    {
        float max = 0;
        for (int i = 0; i < bytesRecorded; i += 2)
        {
            if (i + 1 < bytesRecorded)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                float sampleFloat = sample / 32768f;
                max = Math.Max(max, Math.Abs(sampleFloat));
            }
        }
        volume = max;
    }

    private Task<string> MixAudioFiles(string basePath)
    {
        var micPath = Path.ChangeExtension(basePath, ".mic.wav");
        var systemPath = Path.ChangeExtension(basePath, ".system.wav");
        
        if (!File.Exists(micPath) || !File.Exists(systemPath))
        {
            // If one file is missing, return the one that exists
            if (File.Exists(micPath)) return Task.FromResult(micPath);
            if (File.Exists(systemPath)) return Task.FromResult(systemPath);
            return Task.FromResult(basePath);
        }

        try
        {
            Console.WriteLine("Mixing audio streams...");
            
            using var micReader = new AudioFileReader(micPath);
            using var systemReader = new AudioFileReader(systemPath);
            
            // Convert mic to stereo if needed
            ISampleProvider micStereo = micReader.WaveFormat.Channels == 1 
                ? new MonoToStereoSampleProvider(micReader) 
                : micReader;
            
            // Ensure both have same sample rate
            ISampleProvider micResampled = micReader.WaveFormat.SampleRate != systemReader.WaveFormat.SampleRate
                ? new WdlResamplingSampleProvider(micStereo, systemReader.WaveFormat.SampleRate)
                : micStereo;
            
            // Mix the two sources
            var mixer = new MixingSampleProvider(new[] { micResampled, systemReader });
            
            // Write mixed output
            WaveFileWriter.CreateWaveFile16(basePath, mixer);
            
            // Clean up temporary files
            try { File.Delete(micPath); } catch { }
            try { File.Delete(systemPath); } catch { }
            
            return Task.FromResult(basePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not mix audio files: {ex.Message}");
            // Return system audio as fallback
            return Task.FromResult(File.Exists(systemPath) ? systemPath : micPath);
        }
    }

    private void StopRecording()
    {
        _isRecording = false;
        _stopwatch.Stop();
        
        _micCapture?.StopRecording();
        _micCapture?.Dispose();
        _micCapture = null;
        
        _systemCapture?.StopRecording();
        _systemCapture?.Dispose();
        _systemCapture = null;
        
        _micWriter?.Dispose();
        _micWriter = null;
        
        _systemWriter?.Dispose();
        _systemWriter = null;
        
        _mixedWriter?.Dispose();
        _mixedWriter = null;
    }

    private void DisplayProgress(CancellationToken cancellationToken, RecordingMode mode)
    {
        Console.WriteLine();
        
        while (!cancellationToken.IsCancellationRequested && _isRecording)
        {
            var elapsed = _stopwatch.Elapsed;
            var sizeMB = _bytesRecorded / (1024.0 * 1024.0);
            
            string volumeDisplay = mode switch
            {
                RecordingMode.MicrophoneOnly => $"Mic: {GetVolumeBar(_currentMicVolume)}",
                RecordingMode.SystemOnly => $"System: {GetVolumeBar(_currentSystemVolume)}",
                RecordingMode.Both => $"Mic: {GetVolumeBar(_currentMicVolume)} | System: {GetVolumeBar(_currentSystemVolume)}",
                _ => ""
            };
            
            Console.Write($"\rRecording: {elapsed:mm\\:ss} | {volumeDisplay} | Press [Enter] to stop");
            
            Thread.Sleep(100);
        }
    }

    private string GetVolumeBar(float volume)
    {
        int barLength = (int)(volume * 20);
        return $"[{new string('▌', barLength).PadRight(20, '─')}]";
    }

    private string GetModeDescription(RecordingMode mode)
    {
        return mode switch
        {
            RecordingMode.MicrophoneOnly => "microphone only",
            RecordingMode.SystemOnly => "system audio only",
            RecordingMode.Both => "microphone + system audio",
            _ => "unknown"
        };
    }

    public static void ListMicrophones()
    {
        int deviceCount = WaveInEvent.DeviceCount;
        Console.WriteLine("\nAvailable microphones:");
        for (int i = 0; i < deviceCount; i++)
        {
            var deviceInfo = WaveInEvent.GetCapabilities(i);
            Console.WriteLine($"{i}: {deviceInfo.ProductName}");
        }
    }

    public void Dispose()
    {
        StopRecording();
        _recordingCancellation?.Dispose();
    }
}

public enum RecordingMode
{
    MicrophoneOnly,
    SystemOnly,
    Both
}