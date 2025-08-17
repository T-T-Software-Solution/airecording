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

            // Split summary into chunks if needed
            var summaryChunks = SplitTextIntoChunks(summary, 2000);
            foreach (var chunk in summaryChunks)
            {
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
                                text = new { content = chunk }
                            }
                        }
                    }
                });
            }

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

        // Split content into chunks of 2000 characters (Notion API limit)
        var contentChunks = SplitTextIntoChunks(content, 2000);
        foreach (var chunk in contentChunks)
        {
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
                            text = new { content = chunk }
                        }
                    }
                }
            });
        }

        return children.ToArray();
    }

    private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
    {
        var chunks = new List<string>();
        
        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }
        
        // Split by paragraphs first to avoid breaking mid-sentence
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.None);
        var currentChunk = new StringBuilder();
        
        foreach (var paragraph in paragraphs)
        {
            // If the paragraph itself is too long, split it
            if (paragraph.Length > maxChunkSize)
            {
                // Save current chunk if it has content
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }
                
                // Split long paragraph by sentences or words
                var sentences = paragraph.Split(new[] { ". ", "? ", "! ", "។ ", "๏ ", "。" }, StringSplitOptions.None);
                
                foreach (var sentence in sentences)
                {
                    if (sentence.Length > maxChunkSize)
                    {
                        // Split by words if sentence is still too long
                        var words = sentence.Split(' ');
                        foreach (var word in words)
                        {
                            if (currentChunk.Length + word.Length + 1 > maxChunkSize)
                            {
                                if (currentChunk.Length > 0)
                                {
                                    chunks.Add(currentChunk.ToString().Trim());
                                    currentChunk.Clear();
                                }
                            }
                            
                            if (currentChunk.Length > 0)
                                currentChunk.Append(" ");
                            currentChunk.Append(word);
                        }
                    }
                    else
                    {
                        var sentenceWithPunctuation = sentence + (sentence.EndsWith(".") || sentence.EndsWith("?") || sentence.EndsWith("!") ? "" : ". ");
                        
                        if (currentChunk.Length + sentenceWithPunctuation.Length > maxChunkSize)
                        {
                            if (currentChunk.Length > 0)
                            {
                                chunks.Add(currentChunk.ToString().Trim());
                                currentChunk.Clear();
                            }
                        }
                        
                        currentChunk.Append(sentenceWithPunctuation);
                    }
                }
            }
            else
            {
                // Check if adding this paragraph would exceed the limit
                var paragraphWithNewline = (currentChunk.Length > 0 ? "\n\n" : "") + paragraph;
                
                if (currentChunk.Length + paragraphWithNewline.Length > maxChunkSize)
                {
                    // Save current chunk and start new one
                    if (currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }
                    currentChunk.Append(paragraph);
                }
                else
                {
                    // Add to current chunk
                    if (currentChunk.Length > 0)
                        currentChunk.Append("\n\n");
                    currentChunk.Append(paragraph);
                }
            }
        }
        
        // Add remaining chunk if any
        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }
        
        // If no chunks were created but we have text, just split it brutally
        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            for (int i = 0; i < text.Length; i += maxChunkSize)
            {
                var length = Math.Min(maxChunkSize, text.Length - i);
                chunks.Add(text.Substring(i, length));
            }
        }
        
        Console.WriteLine($"Split text ({text.Length} chars) into {chunks.Count} chunks");
        
        return chunks;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}