namespace AIConsoleAppRecording;

public class RecordingSettings
{
    public RecordingMode Mode { get; set; }
    public string Language { get; set; } = "auto";
    public int? MicrophoneDeviceNumber { get; set; }
    
    public static RecordingSettings GetFromEnvironment()
    {
        var settings = new RecordingSettings();
        
        // Try to read from both User and Process level
        var defaultMode = Environment.GetEnvironmentVariable("RECORDING_MODE", EnvironmentVariableTarget.User) 
                         ?? Environment.GetEnvironmentVariable("RECORDING_MODE", EnvironmentVariableTarget.Process)
                         ?? Environment.GetEnvironmentVariable("RECORDING_MODE");
        
        if (!string.IsNullOrEmpty(defaultMode))
        {
            settings.Mode = defaultMode.ToLower() switch
            {
                "mic" or "microphone" => RecordingMode.MicrophoneOnly,
                "system" => RecordingMode.SystemOnly,
                "both" or "mixed" => RecordingMode.Both,
                _ => RecordingMode.Both
            };
        }
        
        // Check for default language from environment
        var defaultLanguage = Environment.GetEnvironmentVariable("RECORDING_LANGUAGE", EnvironmentVariableTarget.User)
                             ?? Environment.GetEnvironmentVariable("RECORDING_LANGUAGE", EnvironmentVariableTarget.Process)
                             ?? Environment.GetEnvironmentVariable("RECORDING_LANGUAGE");
        
        if (!string.IsNullOrEmpty(defaultLanguage))
        {
            settings.Language = defaultLanguage.ToLower();
        }
        
        // Check for default microphone from environment
        var defaultMic = Environment.GetEnvironmentVariable("RECORDING_MICROPHONE", EnvironmentVariableTarget.User)
                        ?? Environment.GetEnvironmentVariable("RECORDING_MICROPHONE", EnvironmentVariableTarget.Process)
                        ?? Environment.GetEnvironmentVariable("RECORDING_MICROPHONE");
        
        if (!string.IsNullOrEmpty(defaultMic) && int.TryParse(defaultMic, out var micNumber))
        {
            settings.MicrophoneDeviceNumber = micNumber;
        }
        
        return settings;
    }
}