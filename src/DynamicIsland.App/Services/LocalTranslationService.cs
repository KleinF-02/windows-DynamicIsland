using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DynamicIsland.App.Services;

public sealed record TranslationRequest(
    string Text,
    string TargetLanguage,
    string Endpoint,
    string Model);

public sealed record TranslationResult(string Text, string Provider, string Model);

public sealed record LocalChatImage(string MimeType, string Base64);

public sealed record LocalChatMessage(
    string Role,
    string Content,
    IReadOnlyList<LocalChatImage>? Images = null);

public sealed class LocalTranslationException : Exception
{
    public LocalTranslationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Calls a locally hosted model. Ollama's native /api/chat is tried first on
/// the default 11434 port; other endpoints use the OpenAI-compatible
/// /v1/chat/completions shape (LM Studio, llama.cpp, etc.).
/// </summary>
public sealed class LocalTranslationService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private enum Provider
    {
        Ollama,
        OpenAiCompatible
    }

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new LocalTranslationException("请先输入要翻译的文字。");
        }

        var target = MapLanguage(request.TargetLanguage);
        var systemPrompt =
            $"You are a precise translation engine. Translate the user's text into {target}. "
            + "Preserve line breaks, markdown, code, URLs and proper names. "
            + "Return only the translated text, without explanations or quotes.";

        return await CompleteAsync(
            [
                new LocalChatMessage("system", systemPrompt),
                new LocalChatMessage("user", request.Text)
            ],
            request.Endpoint,
            request.Model,
            cancellationToken);
    }

    /// <summary>
    /// Sends a complete role/content conversation to the local model. Used by
    /// translation and by the multi-turn AI agent workspace.
    /// </summary>
    public async Task<TranslationResult> CompleteAsync(
        IReadOnlyList<LocalChatMessage> messages,
        string endpointValue,
        string modelValue,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            throw new LocalTranslationException("没有可发送的消息。");
        }

        var endpoint = string.IsNullOrWhiteSpace(endpointValue)
            ? "http://127.0.0.1:11434"
            : endpointValue.Trim();

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new LocalTranslationException("本地模型地址无效，请填写 http://127.0.0.1:11434 这类地址。");
        }

        var model = modelValue.Trim();
        var attempts = BuildAttempts(baseUri);
        var failures = new List<string>();

        foreach (var provider in attempts)
        {
            try
            {
                var resolvedModel = string.IsNullOrWhiteSpace(model)
                    ? await ResolveModelAsync(provider, baseUri, cancellationToken)
                    : model;

                var content = await SendChatAsync(
                    provider,
                    baseUri,
                    resolvedModel,
                    messages,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(content))
                {
                    return new TranslationResult(
                        content.Trim(),
                        provider == Provider.Ollama ? "Ollama" : "OpenAI 兼容",
                        resolvedModel);
                }

                failures.Add("模型返回了空内容");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException
                or TaskCanceledException
                or JsonException
                or LocalTranslationException
                or InvalidOperationException)
            {
                failures.Add(ShortMessage(ex));
            }
        }

        AppLogger.Error($"Local model call failed: {string.Join(" | ", failures)}");
        throw new LocalTranslationException(
            string.IsNullOrWhiteSpace(model)
                ? "无法连接本地模型。请确认 Ollama / LM Studio 已启动并加载了模型，或在设置中填写模型名。"
                : "本地模型没有返回内容。请确认服务已启动，且模型名称填写正确。");
    }

    private static IReadOnlyList<Provider> BuildAttempts(Uri endpoint)
    {
        var path = endpoint.AbsolutePath.TrimEnd('/');

        if (path.Contains("/api/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return [Provider.Ollama];
        }

        if (path.Contains("/v1", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return [Provider.OpenAiCompatible];
        }

        return endpoint.Port == 11434
            ? [Provider.Ollama, Provider.OpenAiCompatible]
            : [Provider.OpenAiCompatible, Provider.Ollama];
    }

    private static async Task<string> ResolveModelAsync(
        Provider provider,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        var modelsUri = BuildModelsUri(provider, endpoint);
        using var response = await Http.GetAsync(modelsUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LocalTranslationException(
                $"模型列表请求失败（HTTP {(int)response.StatusCode}）。");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (provider == Provider.Ollama
            && document.RootElement.TryGetProperty("models", out var models)
            && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in models.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name)
                    && !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    return name.GetString()!;
                }

                if (item.TryGetProperty("model", out var model)
                    && !string.IsNullOrWhiteSpace(model.GetString()))
                {
                    return model.GetString()!;
                }
            }
        }

        if (provider == Provider.OpenAiCompatible
            && document.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id)
                    && !string.IsNullOrWhiteSpace(id.GetString()))
                {
                    return id.GetString()!;
                }
            }
        }

        throw new LocalTranslationException("本地服务没有返回可用模型，请先在 Ollama / LM Studio 中加载模型。");
    }

    private static async Task<string> SendChatAsync(
        Provider provider,
        Uri endpoint,
        string model,
        IReadOnlyList<LocalChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var payloadMessages = messages
            .Select(message =>
            {
                var images = message.Images;
                if (provider == Provider.Ollama)
                {
                    var item = new Dictionary<string, object?>
                    {
                        ["role"] = message.Role,
                        ["content"] = message.Content
                    };
                    if (images is { Count: > 0 })
                    {
                        item["images"] = images.Select(image => image.Base64).ToArray();
                    }

                    return item;
                }

                object content = message.Content;
                if (images is { Count: > 0 })
                {
                    var parts = new List<object>
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "text",
                            ["text"] = message.Content
                        }
                    };

                    foreach (var image in images)
                    {
                        parts.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new Dictionary<string, object?>
                            {
                                ["url"] = $"data:{image.MimeType};base64,{image.Base64}"
                            }
                        });
                    }

                    content = parts;
                }

                return new Dictionary<string, object?>
                {
                    ["role"] = message.Role,
                    ["content"] = content
                };
            })
            .ToArray();

        object payload = provider == Provider.Ollama
            ? new
            {
                model,
                stream = false,
                messages = payloadMessages,
                options = new { temperature = 0.3 }
            }
            : new
            {
                model,
                stream = false,
                temperature = 0.3,
                messages = payloadMessages
            };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(
            BuildChatUri(provider, endpoint),
            content,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LocalTranslationException(
                $"模型请求失败（HTTP {(int)response.StatusCode}）：{Truncate(body, 160)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (provider == Provider.Ollama
            && root.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var messageContent))
        {
            return messageContent.GetString() ?? string.Empty;
        }

        if (provider == Provider.Ollama
            && root.TryGetProperty("response", out var responseContent))
        {
            return responseContent.GetString() ?? string.Empty;
        }

        if (provider == Provider.OpenAiCompatible
            && root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var choiceMessage)
                    && choiceMessage.TryGetProperty("content", out var choiceContent))
                {
                    return choiceContent.GetString() ?? string.Empty;
                }
            }
        }

        throw new LocalTranslationException("模型返回格式无法识别。");
    }

    private static Uri BuildModelsUri(Provider provider, Uri endpoint)
    {
        var path = endpoint.AbsolutePath.TrimEnd('/');
        if (provider == Provider.Ollama)
        {
            path = path.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase)
                ? path[..^"/chat".Length]
                : path;
            path = path.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
                ? path + "/tags"
                : path + "/api/tags";
        }
        else
        {
            path = path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? path[..^"/chat/completions".Length]
                : path;
            path = path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? path + "/models"
                : path + "/v1/models";
        }

        var builder = new UriBuilder(endpoint) { Path = path };
        return builder.Uri;
    }

    private static Uri BuildChatUri(Provider provider, Uri endpoint)
    {
        var path = endpoint.AbsolutePath.TrimEnd('/');
        if (provider == Provider.Ollama)
        {
            if (path.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            {
                // Already an explicit chat endpoint.
            }
            else if (path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                path += "/chat";
            }
            else
            {
                path += "/api/chat";
            }
        }
        else
        {
            if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                // Already an explicit chat endpoint.
            }
            else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                path += "/chat/completions";
            }
            else
            {
                path += "/v1/chat/completions";
            }
        }

        var builder = new UriBuilder(endpoint) { Path = path };
        return builder.Uri;
    }

    private static string MapLanguage(string language) => language switch
    {
        "中文" => "Simplified Chinese",
        "日本語" => "Japanese",
        "한국어" => "Korean",
        "Français" => "French",
        "Deutsch" => "German",
        _ => "English"
    };

    private static string ShortMessage(Exception exception)
    {
        var message = exception.Message.ReplaceLineEndings(" ").Trim();
        return message.Length > 180 ? message[..180] + "…" : message;
    }

    private static string Truncate(string value, int max)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length > max ? value[..max] + "…" : value;
    }
}
