using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AIConsoleAppRecording;

public class NotionService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private readonly string _databaseId;
    private const string NotionApiUrl = "https://api.notion.com/v1/pages";
    private const string NotionVersion = "2022-06-28";

    public NotionService(IConfiguration configuration)
    {
        _httpClient = new HttpClient();
        _apiToken = configuration["Notion:ApiToken"] ?? throw new ArgumentNullException("Notion:ApiToken not configured");
        _databaseId = configuration["Notion:DatabaseId"] ?? throw new ArgumentNullException("Notion:DatabaseId not configured");
    }

    public async Task CreatePageAsync(string content, string? summary = null, string? customTitle = null)
    {
        try
        {
            var currentTime = DateTime.Now;
            var title = !string.IsNullOrWhiteSpace(customTitle) 
                ? customTitle 
                : $"Transcription - {currentTime:yyyy-MM-dd HH:mm}";
            var isoDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            
            var requestBody = new
            {
                parent = new { database_id = _databaseId },
                properties = new
                {
                    Name = new
                    {
                        title = new[]
                        {
                            new
                            {
                                text = new { content = title }
                            }
                        }
                    },
                    Date = new
                    {
                        date = new
                        {
                            start = isoDate
                        }
                    }
                },
                children = CreatePageChildren(content, summary, title)
            };
            
            var json = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiToken}");
            _httpClient.DefaultRequestHeaders.Add("Notion-Version", NotionVersion);
            
            Console.WriteLine("Creating page in Notion...");
            var response = await _httpClient.PostAsync(NotionApiUrl, httpContent);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Notion API request failed with status {response.StatusCode}: {errorContent}");
            }
            
            Console.WriteLine($"Page created successfully in Notion: {title}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create Notion page: {ex.Message}", ex);
        }
    }

    private object[] CreatePageChildren(string content, string? summary, string? title = null)
    {
        var children = new List<object>();

        // Add title section if available
        if (!string.IsNullOrWhiteSpace(title))
        {
            children.Add(new
            {
                @object = "block",
                type = "heading_1",
                heading_1 = new
                {
                    rich_text = new[]
                    {
                        new
                        {
                            type = "text",
                            text = new { content = title }
                        }
                    }
                }
            });

            children.Add(new
            {
                @object = "block",
                type = "divider",
                divider = new { }
            });
        }

        // Add summary section if available
        if (!string.IsNullOrWhiteSpace(summary))
        {
            children.Add(new
            {
                @object = "block",
                type = "heading_2",
                heading_2 = new
                {
                    rich_text = new[]
                    {
                        new
                        {
                            type = "text",
                            text = new { content = "📝 Summary" }
                        }
                    }
                }
            });

            children.Add(new
            {
                @object = "block",
                type = "paragraph",
                paragraph = new
                {
                    rich_text = new[]
                    {
                        new
                        {
                            type = "text",
                            text = new { content = summary }
                        }
                    }
                }
            });

            // Add divider
            children.Add(new
            {
                @object = "block",
                type = "divider",
                divider = new { }
            });
        }

        // Add full transcript section
        children.Add(new
        {
            @object = "block",
            type = "heading_2",
            heading_2 = new
            {
                rich_text = new[]
                {
                    new
                    {
                        type = "text",
                        text = new { content = "📄 Full Transcript" }
                    }
                }
            }
        });

        children.Add(new
        {
            @object = "block",
            type = "paragraph",
            paragraph = new
            {
                rich_text = new[]
                {
                    new
                    {
                        type = "text",
                        text = new { content = content }
                    }
                }
            }
        });

        return children.ToArray();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}