using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AIConsoleAppRecording;

public class AzureSummaryService
{
    private readonly HttpClient _httpClient;
    private readonly string _endpointUrl;
    private readonly string _apiKey;

    public AzureSummaryService(IConfiguration configuration)
    {
        _httpClient = new HttpClient();
        _endpointUrl = configuration["AzureAI:EndpointUrl"] ?? throw new ArgumentNullException("AzureAI:EndpointUrl not configured");
        _apiKey = configuration["AzureAI:ApiKey"] ?? throw new ArgumentNullException("AzureAI:ApiKey not configured");
    }

    public async Task<(string title, string summary)> SummarizeAsync(string text, string language = "auto")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be empty", nameof(text));
        }

        try
        {
            Console.WriteLine("Generating summary with Azure AI...");
            
            // Create the prompt based on language
            var systemPrompt = language.ToLower() switch
            {
                "th" => "คุณคือผู้ช่วยที่เชี่ยวชาญในการวิเคราะห์และสรุปข้อความภาษาไทย กรุณาตอบในรูปแบบ JSON ที่มี \"title\" และ \"summary\"",
                "en" => "You are an expert text analysis assistant. Analyze the text and respond in JSON format with \"title\" and \"summary\" fields.",
                _ => "You are an expert text analysis assistant. Analyze the text and respond in JSON format with \"title\" and \"summary\" fields. Detect the language and respond in the same language as the input."
            };

            var userPrompt = language.ToLower() switch
            {
                "th" => $"วิเคราะห์ข้อความนี้และสร้าง:\n1. หัวข้อที่สะท้อนเนื้อหาหลัก (สั้นและกระชับ)\n2. สรุปเนื้อหาสำคัญ\n\nตอบในรูปแบบ JSON:\n{{\n  \"title\": \"หัวข้อที่เหมาะสม\",\n  \"summary\": \"สรุปเนื้อหา\"\n}}\n\nข้อความ:\n{text}",
                "en" => $"Analyze this text and create:\n1. A concise title that reflects the main topic\n2. A clear summary of the key points\n\nRespond in JSON format:\n{{\n  \"title\": \"Appropriate Title\",\n  \"summary\": \"Content summary\"\n}}\n\nText:\n{text}",
                _ => $"Analyze this text and create:\n1. A concise title that reflects the main topic\n2. A clear summary of the key points\n\nRespond in JSON format using the same language as the input:\n{{\n  \"title\": \"Appropriate Title\",\n  \"summary\": \"Content summary\"\n}}\n\nText:\n{text}"
            };

            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            };

            var requestBody = new
            {
                messages = messages,
                max_tokens = 500,
                temperature = 0.3,
                top_p = 1.0
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

            var response = await _httpClient.PostAsync(_endpointUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Azure AI API request failed with status {response.StatusCode}: {errorContent}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();

            using var jsonDoc = JsonDocument.Parse(jsonResponse);
            
            if (jsonDoc.RootElement.TryGetProperty("choices", out var choicesElement) &&
                choicesElement.GetArrayLength() > 0 &&
                choicesElement[0].TryGetProperty("message", out var messageElement) &&
                messageElement.TryGetProperty("content", out var contentElement))
            {
                var responseContent = contentElement.GetString()?.Trim() ?? string.Empty;
                Console.WriteLine("AI analysis generated successfully.");
                
                try
                {
                    // Parse the JSON response from AI
                    using var resultDoc = JsonDocument.Parse(responseContent);
                    if (resultDoc.RootElement.TryGetProperty("title", out var titleElement) &&
                        resultDoc.RootElement.TryGetProperty("summary", out var summaryElement))
                    {
                        var title = titleElement.GetString()?.Trim() ?? "Untitled";
                        var summary = summaryElement.GetString()?.Trim() ?? string.Empty;
                        return (title, summary);
                    }
                }
                catch (JsonException)
                {
                    // If JSON parsing fails, try to extract title and summary from plain text
                    Console.WriteLine("Warning: Could not parse structured response, using fallback parsing");
                    var lines = responseContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var title = lines.Length > 0 ? lines[0].Trim() : "AI Generated Topic";
                    var summary = lines.Length > 1 ? string.Join(" ", lines.Skip(1)).Trim() : responseContent;
                    return (title, summary);
                }
                
                // Fallback if structure is unexpected
                return ("AI Generated Topic", responseContent);
            }

            throw new InvalidOperationException("Unexpected response format from Azure AI API");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to generate summary: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unexpected error during summarization: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}