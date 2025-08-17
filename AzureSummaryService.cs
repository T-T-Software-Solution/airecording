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
            Console.WriteLine("Generating detailed content analysis with Azure AI...");
            
            // Create the prompt based on language for detailed formatted content
            var systemPrompt = language.ToLower() switch
            {
                "th" => "คุณคือผู้ช่วยที่เชี่ยวชาญในการวิเคราะห์และจัดระเบียบข้อความภาษาไทย กรุณาตอบในรูปแบบ JSON ที่มี \"title\" (string) และ \"detailed_content\" (string ในรูปแบบ Markdown)",
                "en" => "You are an expert content analysis and organization assistant. Analyze the text and respond in JSON format with \"title\" (string) and \"detailed_content\" (markdown-formatted string) fields.",
                _ => "You are an expert content analysis and organization assistant. Analyze the text and respond in JSON format with \"title\" (string) and \"detailed_content\" (markdown-formatted string) fields. Detect the language and respond in the same language as the input."
            };

            var userPrompt = language.ToLower() switch
            {
                "th" => $"วิเคราะห์ข้อความนี้และสร้าง:\n1. หัวข้อที่สะท้อนเนื้อหาหลัก (เป็น string เท่านั้น)\n2. เนื้อหาที่มีรายละเอียดและจัดระเบียบแล้วในรูปแบบ Markdown ประกอบด้วย:\n   - ประเด็นหลักและประเด็นย่อย\n   - จัดหมวดหมู่ตามหัวข้อด้วย ## หรือ ###\n   - ใช้ Markdown formatting เช่น bullet points (-), หมายเลข (1.)\n   - ข้อมูลสำคัญที่ควรจำ\n   - การเชื่อมโยงระหว่างหัวข้อต่างๆ\n\nตอบในรูปแบบ JSON (title และ detailed_content ต้องเป็น string เท่านั้น ไม่ใช่ object):\n{{\n  \"title\": \"หัวข้อที่เหมาะสม\",\n  \"detailed_content\": \"## หัวข้อหลัก\\n\\n- ประเด็นที่ 1\\n- ประเด็นที่ 2\\n\\n### หัวข้อย่อย\\n\\n1. รายละเอียดแรก\\n2. รายละเอียดที่สอง\"\n}}\n\nข้อความ:\n{text}",
                "en" => $"Analyze this text and create:\n1. A descriptive title that reflects the main topic (as a simple string)\n2. Detailed, well-formatted content in Markdown format including:\n   - Main points and sub-points\n   - Organized by topic/theme with ## or ### headers\n   - Use Markdown formatting like bullet points (-), numbers (1.)\n   - Key information and important details\n   - Connections between different topics\n   - Clear structure and flow\n\nRespond in JSON format (both title and detailed_content must be simple strings, not objects):\n{{\n  \"title\": \"Descriptive Title\",\n  \"detailed_content\": \"## Main Topic\\n\\n- Key point 1\\n- Key point 2\\n\\n### Subtopic\\n\\n1. Detail one\\n2. Detail two\"\n}}\n\nText:\n{text}",
                _ => $"Analyze this text and create:\n1. A descriptive title that reflects the main topic (as a simple string)\n2. Detailed, well-formatted content in Markdown format including:\n   - Main points and sub-points organized by topic\n   - Use Markdown formatting like headers (##), bullet points (-), numbers (1.)\n   - Key information and important details\n   - Clear structure and logical flow\n   - Connections between different topics\n\nRespond in JSON format using the same language as the input (both title and detailed_content must be simple strings, not objects):\n{{\n  \"title\": \"Descriptive Title\",\n  \"detailed_content\": \"## Main Topic\\n\\n- Key point 1\\n- Key point 2\\n\\n### Subtopic\\n\\n1. Detail one\\n2. Detail two\"\n}}\n\nText:\n{text}"
            };

            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            };

            var requestBody = new
            {
                messages = messages,
                max_tokens = 1500, // Increased for detailed content
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
                string responseContent;
                
                // Handle both string and object content types
                if (contentElement.ValueKind == JsonValueKind.String)
                {
                    responseContent = contentElement.GetString()?.Trim() ?? string.Empty;
                }
                else if (contentElement.ValueKind == JsonValueKind.Object)
                {
                    // Content is an object, convert to string
                    responseContent = contentElement.GetRawText()?.Trim() ?? string.Empty;
                }
                else
                {
                    Console.WriteLine($"Warning: Unexpected content type: {contentElement.ValueKind}");
                    responseContent = contentElement.ToString()?.Trim() ?? string.Empty;
                }
                
                Console.WriteLine("AI detailed content analysis generated successfully.");
                
                try
                {
                    // Clean up the response content - remove markdown formatting and extra characters
                    var cleanedContent = CleanJsonResponse(responseContent);
                    
                    // Parse the JSON response from AI
                    using var resultDoc = JsonDocument.Parse(cleanedContent);
                    
                    if (resultDoc.RootElement.TryGetProperty("title", out var titleElement) &&
                        resultDoc.RootElement.TryGetProperty("detailed_content", out var detailedContentElement))
                    {
                        string title = "Untitled";
                        string detailedContent = string.Empty;
                        
                        // Extract title (should be a simple string now)
                        try
                        {
                            title = titleElement.GetString()?.Trim() ?? "Untitled";
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Failed to extract title: {ex.Message}");
                            // Fallback to raw text extraction
                            title = titleElement.GetRawText().Trim('"') ?? "Untitled";
                        }
                        
                        // Extract detailed content (should be a markdown-formatted string now)
                        try
                        {
                            detailedContent = detailedContentElement.GetString()?.Trim() ?? string.Empty;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: AI returned non-string content, attempting fallback conversion: {ex.Message}");
                            // Fallback: if AI still returns object despite our prompt improvements
                            if (detailedContentElement.ValueKind == JsonValueKind.Object)
                            {
                                detailedContent = ConvertJsonObjectToReadableText(detailedContentElement);
                            }
                            else
                            {
                                detailedContent = detailedContentElement.GetRawText().Trim('"') ?? string.Empty;
                            }
                        }
                        
                        // Clean up any remaining artifacts
                        detailedContent = CleanSummaryText(detailedContent);
                        
                        return (title, detailedContent);
                    }
                    // Fallback: try "summary" field for backward compatibility
                    else if (resultDoc.RootElement.TryGetProperty("title", out var titleElement2) &&
                             resultDoc.RootElement.TryGetProperty("summary", out var summaryElement))
                    {
                        string title = "Untitled";
                        string summary = string.Empty;
                        
                        try
                        {
                            title = titleElement2.GetString()?.Trim() ?? "Untitled";
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Failed to extract title (fallback): {ex.Message}");
                            title = titleElement2.GetRawText().Trim('"') ?? "Untitled";
                        }
                        
                        try
                        {
                            summary = summaryElement.GetString()?.Trim() ?? string.Empty;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Failed to extract summary (fallback): {ex.Message}");
                            // Fallback for legacy object responses
                            if (summaryElement.ValueKind == JsonValueKind.Object)
                            {
                                summary = ConvertJsonObjectToReadableText(summaryElement);
                            }
                            else
                            {
                                summary = summaryElement.GetRawText().Trim('"') ?? string.Empty;
                            }
                        }
                        
                        summary = CleanSummaryText(summary);
                        return (title, summary);
                    }
                    else
                    {
                        Console.WriteLine("No recognized JSON structure found");
                        Console.WriteLine($"Available properties: {string.Join(", ", resultDoc.RootElement.EnumerateObject().Select(p => p.Name))}");
                    }
                }
                catch (JsonException ex)
                {
                    // If JSON parsing fails, try to extract title and content from plain text
                    Console.WriteLine($"Warning: Could not parse structured response ({ex.Message}), using fallback parsing");
                    
                    var cleanedText = CleanSummaryText(responseContent);
                    var lines = cleanedText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var title = lines.Length > 0 ? lines[0].Trim() : "AI Generated Topic";
                    var detailedContent = lines.Length > 1 ? string.Join("\n", lines.Skip(1)).Trim() : cleanedText;
                    
                    return (title, detailedContent);
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

    private string CleanJsonResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        // Remove markdown code blocks
        response = response.Replace("```json", "").Replace("```", "").Trim();
        
        // Find the JSON part - look for the first { and last }
        var firstBrace = response.IndexOf('{');
        var lastBrace = response.LastIndexOf('}');
        
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            response = response.Substring(firstBrace, lastBrace - firstBrace + 1);
        }
        
        // Additional cleanup
        response = response.Trim();
        
        return response;
    }

    private string ConvertJsonObjectToReadableText(JsonElement jsonElement)
    {
        var result = new StringBuilder();
        
        try
        {
            ConvertJsonElementToText(jsonElement, result, 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to convert JSON object to text: {ex.Message}");
            return jsonElement.GetRawText();
        }
        
        return result.ToString().Trim();
    }
    
    private void ConvertJsonElementToText(JsonElement element, StringBuilder result, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);
        
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    result.AppendLine($"{indent}**{property.Name}:**");
                    ConvertJsonElementToText(property.Value, result, indentLevel + 1);
                    result.AppendLine();
                }
                break;
                
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ConvertJsonElementToText(item, result, indentLevel);
                }
                break;
                
            case JsonValueKind.String:
                var text = element.GetString() ?? "";
                if (text.StartsWith("- "))
                {
                    result.AppendLine($"{indent}{text}");
                }
                else
                {
                    result.AppendLine($"{indent}- {text}");
                }
                break;
                
            default:
                result.AppendLine($"{indent}- {element.GetRawText()}");
                break;
        }
    }

    private string CleanSummaryText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove common JSON artifacts and formatting issues
        text = text
            .Replace("```json", "")
            .Replace("```", "")
            .Replace("\" }", "")
            .Replace("{ \"", "")
            .Replace("\":", ":")
            .Replace("\",", ",")
            .Replace("\\\"", "\"")
            .Trim();

        // Remove any remaining JSON structure patterns
        if (text.StartsWith("\"") && text.EndsWith("\""))
        {
            text = text.Substring(1, text.Length - 2);
        }

        // Clean up common JSON escape sequences
        text = text
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t");

        return text.Trim();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}