using Microsoft.Extensions.Configuration;
using AIConsoleAppRecording;
using NAudio.Wave;
using System.Text;
using System.Runtime.InteropServices;

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
        
        // Get settings from environment
        var settings = RecordingSettings.GetFromEnvironment();
        
        // Check if recording mode was set from environment (check User level explicitly)
        var envMode = Environment.GetEnvironmentVariable("RECORDING_MODE", EnvironmentVariableTarget.User) 
                     ?? Environment.GetEnvironmentVariable("RECORDING_MODE", EnvironmentVariableTarget.Process)
                     ?? Environment.GetEnvironmentVariable("RECORDING_MODE");
        if (!string.IsNullOrEmpty(envMode))
        {
            Console.WriteLine($"Using recording mode from environment: {settings.Mode}");
        }
        else
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
        
        // Check if language was set from environment (check User level explicitly)
        var envLang = Environment.GetEnvironmentVariable("RECORDING_LANGUAGE", EnvironmentVariableTarget.User)
                     ?? Environment.GetEnvironmentVariable("RECORDING_LANGUAGE", EnvironmentVariableTarget.Process)
                     ?? Environment.GetEnvironmentVariable("RECORDING_LANGUAGE");
        if (!string.IsNullOrEmpty(envLang))
        {
            Console.WriteLine($"Using language from environment: {settings.Language}");
        }
        else
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
        
        // Check if microphone was set from environment (check User level explicitly)
        var envMic = Environment.GetEnvironmentVariable("RECORDING_MICROPHONE", EnvironmentVariableTarget.User)
                    ?? Environment.GetEnvironmentVariable("RECORDING_MICROPHONE", EnvironmentVariableTarget.Process)
                    ?? Environment.GetEnvironmentVariable("RECORDING_MICROPHONE");
        if (settings.Mode != RecordingMode.SystemOnly)
        {
            if (!string.IsNullOrEmpty(envMic) && settings.MicrophoneDeviceNumber.HasValue)
            {
                Console.WriteLine($"Using microphone from environment: Device #{settings.MicrophoneDeviceNumber}");
            }
            else if (!settings.MicrophoneDeviceNumber.HasValue)
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
            
            // Generate title and summary if Azure AI is configured
            string? aiTitle = null;
            string? summary = null;
            var hasAzureAI = !string.IsNullOrWhiteSpace(configuration["AzureAI:EndpointUrl"]) && 
                            !string.IsNullOrWhiteSpace(configuration["AzureAI:ApiKey"]);
            
            if (hasAzureAI)
            {
                try
                {
                    var summaryService = new AzureSummaryService(configuration);
                    (aiTitle, summary) = await summaryService.SummarizeAsync(transcription, settings.Language);
                    
                    if (!string.IsNullOrWhiteSpace(aiTitle))
                    {
                        Console.WriteLine("Generated title:");
                        Console.WriteLine("─" + new string('─', 50));
                        Console.WriteLine(aiTitle);
                        Console.WriteLine("─" + new string('─', 50));
                    }
                    
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        Console.WriteLine("Generated summary:");
                        Console.WriteLine("─" + new string('─', 50));
                        Console.WriteLine(summary);
                        Console.WriteLine("─" + new string('─', 50) + "\n");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to generate summary: {ex.Message}");
                    Console.WriteLine("Continuing without summary...\n");
                }
            }
            else
            {
                Console.WriteLine("💡 TIP: Configure Azure AI to enable automatic summarization");
                Console.WriteLine("   dotnet user-secrets set \"AzureAI:EndpointUrl\" \"<your-azure-ai-endpoint>\"");
                Console.WriteLine("   dotnet user-secrets set \"AzureAI:ApiKey\" \"<your-azure-ai-key>\"\n");
            }
            
            bool notionSuccess = false;
            try
            {
                var notionService = new NotionService(configuration);
                await notionService.CreatePageAsync(transcription, summary, aiTitle);
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
                
                var fileContent = new StringBuilder();
                fileContent.AppendLine($"=== Audio Transcription - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                fileContent.AppendLine();
                
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    fileContent.AppendLine("📝 SUMMARY:");
                    fileContent.AppendLine(new string('─', 50));
                    fileContent.AppendLine(summary);
                    fileContent.AppendLine();
                    fileContent.AppendLine(new string('═', 50));
                    fileContent.AppendLine();
                }
                
                fileContent.AppendLine("📄 FULL TRANSCRIPT:");
                fileContent.AppendLine(new string('─', 50));
                fileContent.AppendLine(transcription);
                
                await File.WriteAllTextAsync(fileName, fileContent.ToString());
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
                
                // Show environment variable tips based on actual selections and OS
                ShowEnvironmentVariableSuggestions(settings);
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
        
        // AzureAI settings are optional for summarization
        var optionalSettings = new[]
        {
            "AzureAI:EndpointUrl",
            "AzureAI:ApiKey"
        };
        
        var missingSettings = requiredSettings
            .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
            .ToList();
        
        if (missingSettings.Any())
        {
            throw new InvalidOperationException($"Missing required settings: {string.Join(", ", missingSettings)}");
        }
    }
    
    static void ShowEnvironmentVariableSuggestions(RecordingSettings settings)
    {
        // Detect OS
        var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        var isMac = Environment.OSVersion.Platform == PlatformID.Unix && 
                    System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
        var isLinux = Environment.OSVersion.Platform == PlatformID.Unix && 
                      System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        
        string osName = isWindows ? "Windows" : (isMac ? "macOS" : "Linux/Unix");
        
        // Convert settings to environment variable values
        string modeValue = settings.Mode switch
        {
            RecordingMode.MicrophoneOnly => "mic",
            RecordingMode.SystemOnly => "system",
            RecordingMode.Both => "both",
            _ => "both"
        };
        
        string languageValue = settings.Language ?? "auto";
        string micValue = settings.MicrophoneDeviceNumber?.ToString() ?? "0";
        
        Console.WriteLine($"\n💡 TIP: Set default values to skip prompts next time ({osName}):");
        Console.WriteLine("\n📌 To SET defaults based on your current selections:");
        
        if (isWindows)
        {
            // Windows commands (both CMD and PowerShell)
            Console.WriteLine("\n   For Command Prompt (CMD):");
            Console.WriteLine($"   setx RECORDING_MODE \"{modeValue}\"");
            Console.WriteLine($"   setx RECORDING_LANGUAGE \"{languageValue}\"");
            if (settings.Mode != RecordingMode.SystemOnly && settings.MicrophoneDeviceNumber.HasValue)
            {
                Console.WriteLine($"   setx RECORDING_MICROPHONE \"{micValue}\"");
            }
            
            Console.WriteLine("\n   For PowerShell:");
            Console.WriteLine($"   [Environment]::SetEnvironmentVariable('RECORDING_MODE', '{modeValue}', 'User')");
            Console.WriteLine($"   [Environment]::SetEnvironmentVariable('RECORDING_LANGUAGE', '{languageValue}', 'User')");
            if (settings.Mode != RecordingMode.SystemOnly && settings.MicrophoneDeviceNumber.HasValue)
            {
                Console.WriteLine($"   [Environment]::SetEnvironmentVariable('RECORDING_MICROPHONE', '{micValue}', 'User')");
            }
        }
        else
        {
            // Linux/Mac commands
            Console.WriteLine("\n   For current session only:");
            Console.WriteLine($"   export RECORDING_MODE=\"{modeValue}\"");
            Console.WriteLine($"   export RECORDING_LANGUAGE=\"{languageValue}\"");
            if (settings.Mode != RecordingMode.SystemOnly && settings.MicrophoneDeviceNumber.HasValue)
            {
                Console.WriteLine($"   export RECORDING_MICROPHONE=\"{micValue}\"");
            }
            
            Console.WriteLine("\n   To make permanent (add to ~/.bashrc or ~/.zshrc):");
            Console.WriteLine($"   echo 'export RECORDING_MODE=\"{modeValue}\"' >> ~/.bashrc");
            Console.WriteLine($"   echo 'export RECORDING_LANGUAGE=\"{languageValue}\"' >> ~/.bashrc");
            if (settings.Mode != RecordingMode.SystemOnly && settings.MicrophoneDeviceNumber.HasValue)
            {
                Console.WriteLine($"   echo 'export RECORDING_MICROPHONE=\"{micValue}\"' >> ~/.bashrc");
            }
        }
        
        Console.WriteLine("\n🗑️  To REMOVE default settings:");
        
        if (isWindows)
        {
            Console.WriteLine("\n   For Command Prompt (CMD):");
            Console.WriteLine("   setx RECORDING_MODE \"\"");
            Console.WriteLine("   setx RECORDING_LANGUAGE \"\"");
            Console.WriteLine("   setx RECORDING_MICROPHONE \"\"");
            
            Console.WriteLine("\n   For PowerShell:");
            Console.WriteLine("   [Environment]::SetEnvironmentVariable('RECORDING_MODE', $null, 'User')");
            Console.WriteLine("   [Environment]::SetEnvironmentVariable('RECORDING_LANGUAGE', $null, 'User')");
            Console.WriteLine("   [Environment]::SetEnvironmentVariable('RECORDING_MICROPHONE', $null, 'User')");
        }
        else
        {
            Console.WriteLine("\n   For current session:");
            Console.WriteLine("   unset RECORDING_MODE");
            Console.WriteLine("   unset RECORDING_LANGUAGE");
            Console.WriteLine("   unset RECORDING_MICROPHONE");
            
            Console.WriteLine("\n   To remove from ~/.bashrc or ~/.zshrc:");
            Console.WriteLine("   sed -i '/RECORDING_MODE/d' ~/.bashrc");
            Console.WriteLine("   sed -i '/RECORDING_LANGUAGE/d' ~/.bashrc");
            Console.WriteLine("   sed -i '/RECORDING_MICROPHONE/d' ~/.bashrc");
        }
        
        Console.WriteLine("\n📝 Note: Environment changes may require restarting your terminal/console.");
    }
}