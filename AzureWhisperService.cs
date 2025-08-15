using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AIConsoleAppRecording;

public class AzureWhisperService
{
    private readonly HttpClient _httpClient;
    private readonly string _endpointUrl;
    private readonly string _apiKey;

    public AzureWhisperService(IConfiguration configuration)
    {
        _httpClient = new HttpClient();
        _endpointUrl = configuration["Azure:EndpointUrl"] ?? throw new ArgumentNullException("Azure:EndpointUrl not configured");
        _apiKey = configuration["Azure:ApiKey"] ?? throw new ArgumentNullException("Azure:ApiKey not configured");
    }

    public async Task<string> TranscribeAsync(string audioFilePath, string language = "auto")
    {
        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException($"Audio file not found: {audioFilePath}");
        }

        try
        {
            using var form = new MultipartFormDataContent();
            
            var fileBytes = await File.ReadAllBytesAsync(audioFilePath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            
            form.Add(fileContent, "file", Path.GetFileName(audioFilePath));
            
            // Add language parameter if specified (not auto)
            if (!string.IsNullOrEmpty(language) && language.ToLower() != "auto")
            {
                var langCode = language.ToLower();
                form.Add(new StringContent(langCode), "language");
                Console.WriteLine($"Forcing language to: {langCode}");
                
                // Also try adding prompt parameter for better Thai recognition
                if (langCode == "th")
                {
                    form.Add(new StringContent("นี่คือการบันทึกเสียงภาษาไทย"), "prompt");
                    Console.WriteLine("Added Thai prompt hint");
                }
            }
            else
            {
                Console.WriteLine("Using auto-detect for language");
            }
            
            // Add response format to get more details
            form.Add(new StringContent("verbose_json"), "response_format");
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);
            
            Console.WriteLine("Sending audio to Azure for transcription...");
            var response = await _httpClient.PostAsync(_endpointUrl, form);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Azure API request failed with status {response.StatusCode}: {errorContent}");
            }
            
            var jsonResponse = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Raw Azure response: {jsonResponse.Substring(0, Math.Min(200, jsonResponse.Length))}...");
            
            using var jsonDoc = JsonDocument.Parse(jsonResponse);
            
            // Log detected language if available
            if (jsonDoc.RootElement.TryGetProperty("language", out var langElement))
            {
                var detectedLanguage = langElement.GetString();
                Console.WriteLine($"Azure detected language: {detectedLanguage}");
                
                if (!string.IsNullOrEmpty(language) && language.ToLower() != "auto" && 
                    detectedLanguage != language.ToLower())
                {
                    Console.WriteLine($"⚠️  WARNING: Requested {language} but Azure detected {detectedLanguage}");
                }
            }
            
            // Log segments if available (for debugging)
            if (jsonDoc.RootElement.TryGetProperty("segments", out var segmentsElement) && 
                segmentsElement.GetArrayLength() > 0)
            {
                Console.WriteLine($"Number of segments: {segmentsElement.GetArrayLength()}");
            }
            
            if (jsonDoc.RootElement.TryGetProperty("text", out var textElement))
            {
                var transcribedText = textElement.GetString() ?? string.Empty;
                Console.WriteLine($"Transcription length: {transcribedText.Length} characters");
                return transcribedText;
            }
            
            throw new InvalidOperationException("Unexpected response format from Azure API");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to transcribe audio: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unexpected error during transcription: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}