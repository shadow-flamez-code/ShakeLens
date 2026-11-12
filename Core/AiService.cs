// ============================================================================
//  ShakeLens - "ask an AI about the selection"
//
//  A tiny client for the Gemini, OpenAI and Claude vision endpoints.
// ============================================================================

using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ShakeLens.Core;

internal sealed record AiReply(bool Ok, string Text, string Error);

internal static class AiService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static async Task<AiReply> AskAsync(byte[] png, string prompt, Settings settings)
    {
        if (settings.AiProvider is "none" or "" or null)
            return new AiReply(false, "", "No AI provider is configured. Add a key in Settings.");
        if (string.IsNullOrWhiteSpace(settings.AiKey))
            return new AiReply(false, "", "No API key is configured. Add one in Settings.");
        if (png.Length == 0)
            return new AiReply(false, "", "There is no image data.");

        string base64 = Convert.ToBase64String(png);
        string question = string.IsNullOrWhiteSpace(prompt) ? settings.AiPrompt : prompt;
        string model = string.IsNullOrWhiteSpace(settings.AiModel)
            ? settings.AiProvider switch
            {
                "openai" => "gpt-4o-mini",
                "claude" => "claude-sonnet-4-5",
                _ => "gemini-2.5-flash"
            }
            : settings.AiModel;

        try
        {
            string url;
            string body;
            string? apiKeyHeader = null;

            switch (settings.AiProvider)
            {
                case "gemini":
                    url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
                    apiKeyHeader = settings.AiKey;
                    body = JsonSerializer.Serialize(new
                    {
                        contents = new[]
                        {
                            new
                            {
                                parts = new object[]
                                {
                                    new { text = question },
                                    new { inline_data = new { mime_type = "image/png", data = base64 } }
                                }
                            }
                        }
                    });
                    break;

                case "openai":
                    url = "https://api.openai.com/v1/chat/completions";
                    apiKeyHeader = settings.AiKey;
                    body = JsonSerializer.Serialize(new
                    {
                        model,
                        messages = new[]
                        {
                            new
                            {
                                role = "user",
                                content = new object[]
                                {
                                    new { type = "text", text = question },
                                    new { type = "image_url", image_url = new { url = "data:image/png;base64," + base64 } }
                                }
                            }
                        }
                    });
                    break;

                case "claude":
                    url = "https://api.anthropic.com/v1/messages";
                    apiKeyHeader = settings.AiKey;
                    body = JsonSerializer.Serialize(new
                    {
                        model,
                        max_tokens = 2048,
                        messages = new[]
                        {
                            new
                            {
                                role = "user",
                                content = new object[]
                                {
                                    new { type = "image", source = new { type = "base64", media_type = "image/png", data = base64 } },
                                    new { type = "text", text = question }
                                }
                            }
                        }
                    });
                    break;

                default:
                    return new AiReply(false, "", "Unknown AI provider.");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (apiKeyHeader is not null)
            {
                switch (settings.AiProvider)
                {
                    case "gemini": request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKeyHeader); break;
                    case "openai": request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKeyHeader); break;
                    case "claude":
                        request.Headers.TryAddWithoutValidation("x-api-key", apiKeyHeader);
                        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                        break;
                }
            }

            using HttpResponseMessage response = await Http.SendAsync(request);
            string payload = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string message = ExtractError(payload);
                return new AiReply(false, "", string.IsNullOrEmpty(message)
                    ? $"The AI service returned {(int)response.StatusCode}."
                    : message);
            }

            string answer = ExtractAnswer(payload, settings.AiProvider);
            if (string.IsNullOrWhiteSpace(answer))
                return new AiReply(false, "", "The AI service returned an empty answer.");

            return new AiReply(true, answer, string.Empty);
        }
        catch (TaskCanceledException)
        {
            return new AiReply(false, "", "The AI request timed out.");
        }
        catch (Exception ex)
        {
            return new AiReply(false, "", ex.Message);
        }
    }

    private static string ExtractError(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? "";
                if (error.TryGetProperty("message", out JsonElement message))
                    return message.GetString() ?? "";
            }
        }
        catch { /* fall through */ }
        return payload.Length > 300 ? payload[..300] : payload;
    }

    private static string ExtractAnswer(string payload, string provider)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            switch (provider)
            {
                case "gemini":
                    if (root.TryGetProperty("candidates", out JsonElement candidates) &&
                        candidates.GetArrayLength() > 0)
                    {
                        JsonElement first = candidates[0];
                        if (first.TryGetProperty("content", out JsonElement content) &&
                            content.TryGetProperty("parts", out JsonElement parts) &&
                            parts.GetArrayLength() > 0 &&
                            parts[0].TryGetProperty("text", out JsonElement text))
                        {
                            return text.GetString() ?? "";
                        }
                    }
                    break;

                case "openai":
                    if (root.TryGetProperty("choices", out JsonElement choices) &&
                        choices.GetArrayLength() > 0 &&
                        choices[0].TryGetProperty("message", out JsonElement openAiMessage) &&
                        openAiMessage.TryGetProperty("content", out JsonElement openAiContent))
                    {
                        return openAiContent.GetString() ?? "";
                    }
                    break;

                case "claude":
                    if (root.TryGetProperty("content", out JsonElement blocks) &&
                        blocks.GetArrayLength() > 0 &&
                        blocks[0].TryGetProperty("text", out JsonElement claudeText))
                    {
                        return claudeText.GetString() ?? "";
                    }
                    break;
            }
        }
        catch { /* ignore */ }
        return string.Empty;
    }
}
