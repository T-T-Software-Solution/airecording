using Microsoft.Extensions.Configuration;
using AIConsoleAppRecording;
using NAudio.Wave;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== AIConsoleAppRecording - Audio Transcription Tool ===\n");
        
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();
        
        try
        {
            ValidateConfiguration(configuration);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Configuration error: {ex.Message}");
            Console.WriteLine("\nPlease set the required secrets using:");
            Console.WriteLine("dotnet user-secrets set \"Azure:EndpointUrl\" \"<your-azure-endpoint-url>\"");
            Console.WriteLine("dotnet user-secrets set \"Azure:ApiKey\" \"<your-azure-api-key>\"");
            Console.WriteLine("dotnet user-secrets set \"Notion:ApiToken\" \"<your-notion-token>\"");
            Console.WriteLine("dotnet user-secrets set \"Notion:DatabaseId\" \"<your-notion-database-id>\"");
            return;
        }
        
        // Get settings from environment or ask user
        var settings = RecordingSettings.GetFromEnvironment();
        
        // If recording mode not set in environment, ask user
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RECORDING_MODE")))
        {
            Console.WriteLine("Select recording mode:");
            Console.WriteLine("1. Microphone only");
            Console.WriteLine("2. System audio only");
            Console.WriteLine("3. Both (mixed)");
            Console.Write("Enter choice (1-3): ");
            
            var choice = Console.ReadLine();
            settings.Mode = choice?.Trim() switch
            {
                "1" => RecordingMode.MicrophoneOnly,
                "2" => RecordingMode.SystemOnly,
                "3" => RecordingMode.Both,
                _ => RecordingMode.Both
            };
        }
        else
        {
            Console.WriteLine($"Using recording mode from environment: {settings.Mode}");
        }
        
        // If language not set in environment, ask user
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RECORDING_LANGUAGE")))
        {
            Console.WriteLine("\nSelect transcription language:");
            Console.WriteLine("1. Auto-detect");
            Console.WriteLine("2. English (en)");
            Console.WriteLine("3. Thai (th)");
            Console.Write("Enter choice (1-3): ");
            
            var langChoice = Console.ReadLine();
            settings.Language = langChoice?.Trim() switch
            {
                "1" => "auto",
                "2" => "en",
                "3" => "th",
                _ => "auto"
            };
        }
        else
        {
            Console.WriteLine($"Using language from environment: {settings.Language}");
        }
        
        // If microphone not set in environment and recording includes mic, ask user
        if (settings.Mode != RecordingMode.SystemOnly && 
            !settings.MicrophoneDeviceNumber.HasValue &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RECORDING_MICROPHONE")))
        {
            AudioService.ListMicrophones();
            
            if (WaveInEvent.DeviceCount > 1)
            {
                Console.Write("\nSelect microphone number (or press Enter for default): ");
                var micChoice = Console.ReadLine();
                if (!string.IsNullOrEmpty(micChoice) && int.TryParse(micChoice, out var micNumber))
                {
                    if (micNumber >= 0 && micNumber < WaveInEvent.DeviceCount)
                    {
                        settings.MicrophoneDeviceNumber = micNumber;
                    }
                }
            }
        }
        else if (settings.MicrophoneDeviceNumber.HasValue)
        {
            Console.WriteLine($"Using microphone from environment: Device #{settings.MicrophoneDeviceNumber}");
        }
        
        string? audioFilePath = null;
        
        try
        {
            using var audioService = new AudioService();
            audioFilePath = await audioService.RecordAudioAsync(settings);
            Console.WriteLine("Recording complete.\n");
            
            var azureService = new AzureWhisperService(configuration);
            Console.WriteLine("Starting transcription...");
            var transcription = await azureService.TranscribeAsync(audioFilePath, settings.Language);
            Console.WriteLine("Transcription successful.\n");
            
            if (string.IsNullOrWhiteSpace(transcription))
            {
                Console.WriteLine("Warning: Transcription is empty.");
                return;
            }
            
            Console.WriteLine("Transcribed text:");
            Console.WriteLine("─" + new string('─', 50));
            Console.WriteLine(transcription);
            Console.WriteLine("─" + new string('─', 50) + "\n");
            
            bool notionSuccess = false;
            try
            {
                var notionService = new NotionService(configuration);
                await notionService.CreatePageAsync(transcription);
                notionSuccess = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to create Notion page: {ex.Message}");
                Console.WriteLine("Continuing to save locally...\n");
            }
            
            bool localSaveSuccess = false;
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var fileName = $"Transcript-{timestamp}.txt";
                await File.WriteAllTextAsync(fileName, transcription);
                Console.WriteLine($"Transcript saved to: {Path.GetFullPath(fileName)}");
                localSaveSuccess = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to save transcript locally: {ex.Message}");
            }
            
            if (notionSuccess || localSaveSuccess)
            {
                Console.WriteLine("\nOperation completed successfully!");
                
                // Show environment variable tips
                Console.WriteLine("\n💡 TIP: You can set default values using environment variables:");
                Console.WriteLine("   RECORDING_MODE=both       (mic/system/both)");
                Console.WriteLine("   RECORDING_LANGUAGE=th     (auto/en/th)");
                Console.WriteLine("   RECORDING_MICROPHONE=0    (device number)");
            }
            else
            {
                Console.WriteLine("\nOperation failed - transcript could not be saved.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Details: {ex.InnerException.Message}");
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
            {
                try
                {
                    File.Delete(audioFilePath);
                    Console.WriteLine("\nTemporary audio file cleaned up.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nWarning: Could not delete temporary file: {ex.Message}");
                }
            }
        }
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
    
    static void ValidateConfiguration(IConfiguration configuration)
    {
        var requiredSettings = new[]
        {
            "Azure:EndpointUrl",
            "Azure:ApiKey",
            "Notion:ApiToken",
            "Notion:DatabaseId"
        };
        
        var missingSettings = requiredSettings
            .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
            .ToList();
        
        if (missingSettings.Any())
        {
            throw new InvalidOperationException($"Missing required settings: {string.Join(", ", missingSettings)}");
        }
    }
}