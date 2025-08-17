using Microsoft.Extensions.Configuration;
using AIConsoleAppRecording;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.MediaFoundation;
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
        
        // Choose operation mode
        Console.WriteLine("Select operation mode:");
        Console.WriteLine("1. Record new audio");
        Console.WriteLine("2. Process existing audio file(s)");
        Console.Write("Enter choice (1-2): ");
        
        var modeChoice = Console.ReadLine()?.Trim();
        if (modeChoice == "2")
        {
            await ProcessExistingFilesMode(configuration);
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
            
            // Use the same ProcessAudioFile method to handle file size checking and splitting
            await ProcessAudioFile(audioFilePath, settings.Language, configuration);
            
            // Show environment variable tips based on actual selections and OS
            ShowEnvironmentVariableSuggestions(settings);
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
            // Only delete if it's a temporary file (MP3), preserve WAV files
            if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
            {
                try
                {
                    var extension = Path.GetExtension(audioFilePath).ToLower();
                    if (extension == ".mp3")
                    {
                        File.Delete(audioFilePath);
                        Console.WriteLine("\nTemporary MP3 file cleaned up.");
                    }
                    else
                    {
                        Console.WriteLine($"\nWAV file preserved at: {audioFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nWarning: Could not clean up temporary file: {ex.Message}");
                }
            }
        }
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
    
    static async Task ProcessExistingFilesMode(IConfiguration configuration)
    {
        Console.WriteLine("\nSelect file processing mode:");
        Console.WriteLine("1. Single audio file (WAV, MP3, M4A, MP4)");
        Console.WriteLine("2. Two separate files (microphone + system audio WAV files)");
        Console.Write("Enter choice (1-2): ");
        
        var fileChoice = Console.ReadLine()?.Trim();
        
        string? audioFilePath = null;
        
        try
        {
            if (fileChoice == "2")
            {
                // Process two separate files
                Console.WriteLine("\nProcessing separate microphone and system audio files:");
                
                Console.Write("Enter microphone WAV file path: ");
                var micPath = Console.ReadLine()?.Trim().Trim('"');
                
                Console.Write("Enter system audio WAV file path: ");
                var systemPath = Console.ReadLine()?.Trim().Trim('"');
                
                if (string.IsNullOrEmpty(micPath) || string.IsNullOrEmpty(systemPath))
                {
                    Console.WriteLine("Error: Both file paths are required.");
                    return;
                }
                
                if (!File.Exists(micPath))
                {
                    Console.WriteLine($"Error: Microphone file not found: {micPath}");
                    return;
                }
                
                if (!File.Exists(systemPath))
                {
                    Console.WriteLine($"Error: System audio file not found: {systemPath}");
                    return;
                }
                
                // Mix the two files
                audioFilePath = await MixSeparateAudioFiles(micPath, systemPath);
                if (string.IsNullOrEmpty(audioFilePath))
                {
                    Console.WriteLine("Error: Failed to mix audio files.");
                    return;
                }
            }
            else
            {
                // Process single file
                Console.Write("\nEnter audio file path: ");
                var filePath = Console.ReadLine()?.Trim().Trim('"');
                
                if (string.IsNullOrEmpty(filePath))
                {
                    Console.WriteLine("Error: File path is required.");
                    return;
                }
                
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Error: File not found: {filePath}");
                    return;
                }
                
                var extension = Path.GetExtension(filePath).ToLower();
                if (extension != ".wav" && extension != ".mp3" && extension != ".m4a" && extension != ".mp4")
                {
                    Console.WriteLine($"Error: Unsupported file format: {extension}");
                    Console.WriteLine("Supported formats: .wav, .mp3, .m4a, .mp4");
                    return;
                }
                
                audioFilePath = filePath;
            }
            
            // Select language
            Console.WriteLine("\nSelect transcription language:");
            Console.WriteLine("1. Auto-detect");
            Console.WriteLine("2. English (en)");
            Console.WriteLine("3. Thai (th)");
            Console.Write("Enter choice (1-3): ");
            
            var langChoice = Console.ReadLine()?.Trim();
            var language = langChoice switch
            {
                "1" => "auto",
                "2" => "en",
                "3" => "th",
                _ => "auto"
            };
            
            Console.WriteLine($"\nProcessing audio file: {audioFilePath}");
            Console.WriteLine($"Language: {language}");
            
            // Process the file
            await ProcessAudioFile(audioFilePath, language, configuration);
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
            // Clean up temporary mixed file if created
            if (!string.IsNullOrEmpty(audioFilePath) && fileChoice == "2" && File.Exists(audioFilePath))
            {
                try
                {
                    File.Delete(audioFilePath);
                    Console.WriteLine("\nTemporary mixed file cleaned up.");
                }
                catch { }
            }
        }
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
    
    static Task<string> MixSeparateAudioFiles(string micPath, string systemPath)
    {
        try
        {
            Console.WriteLine("Mixing separate audio files...");
            
            var outputPath = Path.Combine(Path.GetTempPath(), $"mixed_{Guid.NewGuid()}.wav");
            
            using var micReader = new AudioFileReader(micPath);
            using var systemReader = new AudioFileReader(systemPath);
            
            Console.WriteLine($"Microphone: {micReader.WaveFormat.SampleRate}Hz, {micReader.WaveFormat.Channels} channels");
            Console.WriteLine($"System: {systemReader.WaveFormat.SampleRate}Hz, {systemReader.WaveFormat.Channels} channels");
            
            // Determine target format (use higher sample rate, stereo)
            int targetSampleRate = Math.Max(micReader.WaveFormat.SampleRate, systemReader.WaveFormat.SampleRate);
            int targetChannels = 2; // Always use stereo for mixed output
            
            Console.WriteLine($"Target format: {targetSampleRate}Hz, {targetChannels} channels");
            
            // Process microphone audio
            ISampleProvider micProvider = micReader;
            
            // Convert mic to stereo if mono
            if (micReader.WaveFormat.Channels == 1)
            {
                Console.WriteLine("Converting microphone from mono to stereo...");
                micProvider = new MonoToStereoSampleProvider(micProvider);
            }
            
            // Resample mic if needed
            if (micReader.WaveFormat.SampleRate != targetSampleRate)
            {
                Console.WriteLine($"Resampling microphone from {micReader.WaveFormat.SampleRate}Hz to {targetSampleRate}Hz...");
                micProvider = new WdlResamplingSampleProvider(micProvider, targetSampleRate);
            }
            
            // Process system audio
            ISampleProvider systemProvider = systemReader;
            
            // Convert system to stereo if mono
            if (systemReader.WaveFormat.Channels == 1)
            {
                Console.WriteLine("Converting system audio from mono to stereo...");
                systemProvider = new MonoToStereoSampleProvider(systemProvider);
            }
            
            // Resample system if needed
            if (systemReader.WaveFormat.SampleRate != targetSampleRate)
            {
                Console.WriteLine($"Resampling system audio from {systemReader.WaveFormat.SampleRate}Hz to {targetSampleRate}Hz...");
                systemProvider = new WdlResamplingSampleProvider(systemProvider, targetSampleRate);
            }
            
            // Verify formats match
            Console.WriteLine($"Final mic format: {micProvider.WaveFormat.SampleRate}Hz, {micProvider.WaveFormat.Channels} channels");
            Console.WriteLine($"Final system format: {systemProvider.WaveFormat.SampleRate}Hz, {systemProvider.WaveFormat.Channels} channels");
            
            // Mix the two sources
            var mixer = new MixingSampleProvider(new[] { micProvider, systemProvider });
            
            Console.WriteLine("Writing mixed audio file...");
            
            // Write mixed output
            WaveFileWriter.CreateWaveFile16(outputPath, mixer);
            
            var originalMicSize = new FileInfo(micPath).Length;
            var originalSystemSize = new FileInfo(systemPath).Length;
            var mixedSize = new FileInfo(outputPath).Length;
            
            Console.WriteLine($"✅ Mixed file created: {mixedSize / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine($"  Original mic: {originalMicSize / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine($"  Original system: {originalSystemSize / (1024.0 * 1024.0):F2} MB");
            
            return Task.FromResult(outputPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to mix audio files: {ex.Message}");
            Console.WriteLine($"Error details: {ex}");
            return Task.FromResult(string.Empty);
        }
    }
    
    static async Task ProcessAudioFile(string audioFilePath, string language, IConfiguration configuration)
    {
        string finalAudioPath = audioFilePath;
        List<string> tempFiles = new List<string>();
        
        // Convert WAV files to MP3 for compression
        if (Path.GetExtension(audioFilePath).ToLower() == ".wav")
        {
            var fileInfo = new FileInfo(audioFilePath);
            var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
            
            Console.WriteLine($"Original WAV file size: {fileSizeMB:F2} MB");
            
            // Always convert WAV to MP3 for better upload performance
            var mp3Path = await ConvertWavToMp3(audioFilePath);
            if (!string.IsNullOrEmpty(mp3Path))
            {
                finalAudioPath = mp3Path;
                tempFiles.Add(mp3Path);
            }
            else
            {
                Console.WriteLine("MP3 conversion failed, using original WAV file.");
            }
        }
        
        try
        {
            // Check if file needs to be split
            var finalFileInfo = new FileInfo(finalAudioPath);
            var finalFileSizeMB = finalFileInfo.Length / (1024.0 * 1024.0);
            
            string transcription;
            
            if (finalFileSizeMB > 25)
            {
                Console.WriteLine($"File size ({finalFileSizeMB:F2} MB) exceeds Azure limit. Splitting into 20-minute segments...");
                
                // Split the audio file into segments
                var segments = await SplitAudioFile(finalAudioPath, 20); // 20 minutes each
                tempFiles.AddRange(segments);
                
                if (segments.Count == 0)
                {
                    throw new InvalidOperationException("Failed to split audio file into segments.");
                }
                
                // Transcribe each segment
                transcription = await TranscribeMultipleSegments(segments, language, configuration);
            }
            else
            {
                // Process single file
                var azureService = new AzureWhisperService(configuration);
                Console.WriteLine("Starting transcription...");
                transcription = await azureService.TranscribeAsync(finalAudioPath, language);
                Console.WriteLine("Transcription successful.\n");
            }
            
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
                    (aiTitle, summary) = await summaryService.SummarizeAsync(transcription, language);
                    
                    if (!string.IsNullOrWhiteSpace(aiTitle))
                    {
                        Console.WriteLine("Generated title:");
                        Console.WriteLine("─" + new string('─', 50));
                        Console.WriteLine(aiTitle);
                        Console.WriteLine("─" + new string('─', 50));
                    }
                    
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        Console.WriteLine("Generated detailed content:");
                        Console.WriteLine("─" + new string('─', 50));
                        Console.WriteLine(summary.Length > 500 ? summary.Substring(0, 500) + "..." : summary);
                        Console.WriteLine("─" + new string('─', 50) + "\n");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to generate summary: {ex.Message}");
                    Console.WriteLine("Continuing without summary...\n");
                }
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
                fileContent.AppendLine($"Source file: {audioFilePath}");
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
            }
            else
            {
                Console.WriteLine("\nOperation failed - transcript could not be saved.");
            }
        }
        finally
        {
            // Clean up all temporary files
            foreach (var tempFile in tempFiles)
            {
                if (File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch { }
                }
            }
            
            if (tempFiles.Count > 0)
            {
                Console.WriteLine("Temporary files cleaned up.");
            }
        }
    }
    
    static async Task<List<string>> SplitAudioFile(string audioFilePath, int segmentMinutes)
    {
        var segments = new List<string>();
        
        try
        {
            Console.WriteLine($"Splitting audio into {segmentMinutes}-minute segments...");
            
            using var reader = new AudioFileReader(audioFilePath);
            var segmentDuration = TimeSpan.FromMinutes(segmentMinutes);
            var totalDuration = reader.TotalTime;
            
            Console.WriteLine($"Total duration: {totalDuration:hh\\:mm\\:ss} ({totalDuration.TotalMinutes:F1} minutes)");
            Console.WriteLine($"Segment duration: {segmentDuration:hh\\:mm\\:ss} ({segmentMinutes} minutes)");
            
            int segmentCount = (int)Math.Ceiling(totalDuration.TotalMinutes / segmentMinutes);
            Console.WriteLine($"Will create {segmentCount} segments");
            
            for (int i = 0; i < segmentCount; i++)
            {
                var startTime = TimeSpan.FromMinutes(i * segmentMinutes);
                var endTime = TimeSpan.FromMinutes(Math.Min((i + 1) * segmentMinutes, totalDuration.TotalMinutes));
                
                // Ensure endTime doesn't exceed total duration
                if (endTime > totalDuration)
                {
                    endTime = totalDuration;
                }
                
                var segmentPath = Path.Combine(Path.GetTempPath(), 
                    $"segment_{i + 1:D2}_{Path.GetFileNameWithoutExtension(audioFilePath)}.wav");
                
                Console.WriteLine($"Creating segment {i + 1}/{segmentCount}: {startTime:hh\\:mm\\:ss} - {endTime:hh\\:mm\\:ss}");
                
                // Create segment
                await CreateAudioSegment(audioFilePath, segmentPath, startTime, endTime);
                
                // Check if segment was created successfully
                if (File.Exists(segmentPath))
                {
                    var segmentInfo = new FileInfo(segmentPath);
                    Console.WriteLine($"  WAV segment created: {segmentInfo.Length / (1024.0 * 1024.0):F2} MB");
                    
                    // Convert each segment to MP3 for compression
                    Console.WriteLine($"  Converting segment {i + 1} to MP3...");
                    var mp3SegmentPath = await ConvertSegmentToMp3(segmentPath);
                    
                    if (!string.IsNullOrEmpty(mp3SegmentPath) && File.Exists(mp3SegmentPath))
                    {
                        var mp3Info = new FileInfo(mp3SegmentPath);
                        Console.WriteLine($"  ✅ MP3 segment created: {mp3Info.Length / (1024.0 * 1024.0):F2} MB");
                        
                        // Check if MP3 is still too large
                        if (mp3Info.Length / (1024.0 * 1024.0) > 25)
                        {
                            Console.WriteLine($"  ⚠️  WARNING: MP3 segment still exceeds 25MB limit");
                        }
                        
                        // Delete WAV segment to save space
                        try { File.Delete(segmentPath); } catch { }
                        
                        segments.Add(mp3SegmentPath);
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠️  MP3 conversion failed, using WAV segment");
                        segments.Add(segmentPath);
                    }
                }
                else
                {
                    Console.WriteLine($"  ❌ Failed to create segment {i + 1}");
                }
            }
            
            Console.WriteLine($"Successfully created {segments.Count} segments\n");
            return segments;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to split audio file: {ex.Message}");
            
            // Clean up any partial segments
            foreach (var segment in segments)
            {
                try { File.Delete(segment); } catch { }
            }
            
            return new List<string>();
        }
    }
    
    static async Task CreateAudioSegment(string inputPath, string outputPath, TimeSpan startTime, TimeSpan endTime)
    {
        try
        {
            using var reader = new AudioFileReader(inputPath);
            
            // Validate time ranges
            if (startTime >= reader.TotalTime)
            {
                Console.WriteLine($"    Warning: Start time {startTime} exceeds audio duration {reader.TotalTime}");
                return;
            }
            
            if (endTime > reader.TotalTime)
            {
                endTime = reader.TotalTime;
            }
            
            var duration = endTime - startTime;
            if (duration <= TimeSpan.Zero)
            {
                Console.WriteLine($"    Warning: Invalid duration {duration}");
                return;
            }
            
            Console.WriteLine($"    Extracting {duration:hh\\:mm\\:ss} from {startTime:hh\\:mm\\:ss} to {endTime:hh\\:mm\\:ss}");
            
            // Create segment using OffsetSampleProvider
            var segmentProvider = new OffsetSampleProvider(reader)
            {
                SkipOver = startTime,
                Take = duration
            };
            
            // Write the segment
            await Task.Run(() =>
            {
                WaveFileWriter.CreateWaveFile16(outputPath, segmentProvider);
            });
            
            // Verify the file was created
            if (File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Length > 0)
                {
                    Console.WriteLine($"    ✅ Segment file created successfully");
                }
                else
                {
                    Console.WriteLine($"    ❌ Segment file is empty");
                    try { File.Delete(outputPath); } catch { }
                }
            }
            else
            {
                Console.WriteLine($"    ❌ Segment file was not created");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ❌ Error creating segment: {ex.Message}");
        }
    }
    
    static async Task<string> TranscribeMultipleSegments(List<string> segments, string language, IConfiguration configuration)
    {
        var allTranscripts = new List<string>();
        var azureService = new AzureWhisperService(configuration);
        
        Console.WriteLine($"Transcribing {segments.Count} segments...\n");
        
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var segmentNumber = i + 1;
            
            try
            {
                Console.WriteLine($"Processing segment {segmentNumber}/{segments.Count}...");
                
                var segmentTranscript = await azureService.TranscribeAsync(segment, language);
                
                if (!string.IsNullOrWhiteSpace(segmentTranscript))
                {
                    // Add segment marker and transcript
                    allTranscripts.Add($"[Segment {segmentNumber}]");
                    allTranscripts.Add(segmentTranscript.Trim());
                    allTranscripts.Add(""); // Empty line between segments
                    
                    Console.WriteLine($"Segment {segmentNumber} completed ({segmentTranscript.Length} characters)");
                }
                else
                {
                    Console.WriteLine($"⚠️  Segment {segmentNumber} produced empty transcript");
                    allTranscripts.Add($"[Segment {segmentNumber}] - No speech detected");
                    allTranscripts.Add("");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to transcribe segment {segmentNumber}: {ex.Message}");
                
                // Check for quota/rate limit errors
                if (ex.Message.Contains("429") || ex.Message.Contains("TooManyRequests") || 
                    ex.Message.Contains("rate limit") || ex.Message.Contains("quota"))
                {
                    Console.WriteLine($"🛑 QUOTA/RATE LIMIT EXCEEDED - Stopping processing to avoid further charges");
                    Console.WriteLine($"Successfully processed {segmentNumber - 1} out of {segments.Count} segments");
                    
                    allTranscripts.Add($"[Segment {segmentNumber}] - STOPPED: Rate limit exceeded");
                    allTranscripts.Add($"[Note: Processing stopped at segment {segmentNumber} due to quota limits]");
                    allTranscripts.Add("");
                    
                    // Return partial transcript
                    break;
                }
                
                allTranscripts.Add($"[Segment {segmentNumber}] - Transcription failed: {ex.Message}");
                allTranscripts.Add("");
            }
            
            // Longer delay between requests to respect rate limits
            if (i < segments.Count - 1)
            {
                Console.WriteLine($"Waiting 5 seconds before next segment to respect rate limits...");
                await Task.Delay(5000); // 5 second delay
            }
        }
        
        var combinedTranscript = string.Join("\n", allTranscripts).Trim();
        
        Console.WriteLine($"\n✅ All segments processed. Combined transcript: {combinedTranscript.Length} characters\n");
        Console.WriteLine("Combined transcript preview:");
        Console.WriteLine("─" + new string('─', 50));
        Console.WriteLine(combinedTranscript.Length > 500 
            ? combinedTranscript.Substring(0, 500) + "..." 
            : combinedTranscript);
        Console.WriteLine("─" + new string('─', 50) + "\n");
        
        return combinedTranscript;
    }
    
    static async Task<string> ConvertSegmentToMp3(string wavPath)
    {
        try
        {
            var mp3Path = Path.ChangeExtension(wavPath, ".mp3");
            
            using var reader = new AudioFileReader(wavPath);
            
            // Use 128 kbps for good quality/size balance
            await Task.Run(() =>
            {
                MediaFoundationEncoder.EncodeToMp3(reader, mp3Path, 128000);
            });
            
            var originalSize = new FileInfo(wavPath).Length;
            var compressedSize = new FileInfo(mp3Path).Length;
            var compressionRatio = (1 - (double)compressedSize / originalSize) * 100;
            
            Console.WriteLine($"    Compression: {originalSize / (1024.0 * 1024.0):F2} MB → {compressedSize / (1024.0 * 1024.0):F2} MB ({compressionRatio:F1}% reduction)");
            
            return mp3Path;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Failed to convert segment to MP3: {ex.Message}");
            return string.Empty;
        }
    }
    
    static async Task<string> ConvertWavToMp3(string wavPath)
    {
        try
        {
            var mp3Path = Path.ChangeExtension(wavPath, ".mp3");
            Console.WriteLine("Converting WAV to MP3 for compression...");
            
            using var reader = new AudioFileReader(wavPath);
            
            // Use 128 kbps for good quality/size balance
            await Task.Run(() =>
            {
                MediaFoundationEncoder.EncodeToMp3(reader, mp3Path, 128000);
            });
            
            var originalSize = new FileInfo(wavPath).Length;
            var compressedSize = new FileInfo(mp3Path).Length;
            var compressionRatio = (1 - (double)compressedSize / originalSize) * 100;
            
            Console.WriteLine($"MP3 conversion complete!");
            Console.WriteLine($"Original: {originalSize / (1024.0 * 1024.0):F2} MB → Compressed: {compressedSize / (1024.0 * 1024.0):F2} MB ({compressionRatio:F1}% reduction)");
            
            // Check if still too large for Azure API
            if (compressedSize > 25 * 1024 * 1024) // 25MB limit
            {
                Console.WriteLine($"⚠️  WARNING: File is still {compressedSize / (1024.0 * 1024.0):F1} MB, exceeding Azure's 25MB limit!");
                Console.WriteLine("Consider using a shorter audio segment.");
            }
            
            return mp3Path;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to convert WAV to MP3: {ex.Message}");
            return string.Empty;
        }
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