﻿namespace IdeaTech.SemanticKernel.Connectors.Hunyuan;

/// <summary>
/// This class is responsible for making HTTP requests to the Hunyuan.
/// </summary>
internal sealed class HunyuanClientCore
{
    private const string ModelProvider = "Hunyuan";

    private readonly string _model;
    private readonly ILogger _logger;
    private readonly HunyuanClient _client;

    internal HunyuanClientCore(string model, Credential credential, string region, ClientProfile clientProfile, ILogger logger)
    {
        this._model = model;
        this._client = new HunyuanClient(credential, region, clientProfile);
        this._logger = logger;
    }

    internal async Task<IReadOnlyList<ChatMessageContent>> CompleteChatMessageAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings)
    {
        string model = executionSettings?.ModelId ?? this._model;

        HunyuanPromptExecutionSettings hunyuanPromptExecutionSettings = HunyuanPromptExecutionSettings.FromExecutionSettings(executionSettings);
        hunyuanPromptExecutionSettings.ModelId ??= this._model;
        hunyuanPromptExecutionSettings.Stream = false;

        using var activity = ModelDiagnostics.StartCompletionActivity(new Uri(this._client.Endpoint), model, ModelProvider, chatHistory, hunyuanPromptExecutionSettings);

        ChatCompletionsRequest request = this.CreateChatRequest(chatHistory, hunyuanPromptExecutionSettings);

        ChatCompletionsResponse response;

        try
        {
            response = await this._client.ChatCompletions(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (activity is not null)
        {
            activity.SetError(ex);
            throw;
        }

        List<ChatMessageContent> chatContents = GetChatMessageContentFromResponse(response, model);

        activity?.SetCompletionResponse(chatContents, (int?)response.Usage?.PromptTokens, (int?)response.Usage?.CompletionTokens);

        this.LogChatCompletionUsage(hunyuanPromptExecutionSettings, response);

        return chatContents;
    }

    internal async IAsyncEnumerable<StreamingChatMessageContent> StreamingCompleteChatMessageAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings)
    {
        string model = executionSettings?.ModelId ?? this._model;

        HunyuanPromptExecutionSettings hunyuanPromptExecutionSettings = HunyuanPromptExecutionSettings.FromExecutionSettings(executionSettings);
        hunyuanPromptExecutionSettings.ModelId ??= this._model;
        hunyuanPromptExecutionSettings.Stream = true;

        using var activity = ModelDiagnostics.StartCompletionActivity(new Uri(this._client.Endpoint), model, ModelProvider, chatHistory, hunyuanPromptExecutionSettings);

        ChatCompletionsRequest request = this.CreateChatRequest(chatHistory, hunyuanPromptExecutionSettings);

        ChatCompletionsResponse response;

        try
        {
            response = await this._client.ChatCompletions(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (activity is not null)
        {
            activity.SetError(ex);
            throw;
        }

        IEnumerator<StreamingChatMessageContent> responseEnumerator = ProcessChatResponseStream(response, model);

        List<StreamingChatMessageContent>? streamedContents = activity is not null ? [] : null;

        try
        {
            while (true)
            {
                try
                {
                    if (!responseEnumerator.MoveNext())
                    {
                        break;
                    }
                }
                catch (Exception ex) when (activity is not null)
                {
                    activity.SetError(ex);
                    throw;
                }
            }

            streamedContents?.Add(responseEnumerator.Current);

            yield return responseEnumerator.Current;
        }
        finally
        {
            activity?.Dispose();
            responseEnumerator.Dispose();
        }
    }

    private static List<ChatMessageContent> GetChatMessageContentFromResponse(ChatCompletionsResponse response, string modelId)
    {
        List<ChatMessageContent> chatMessageContents = [];

        foreach (var choice in response.Choices)
        {
            HunyuanChatCompletionMetadata metadata = new()
            {
                Id = response.Id,
                Created = response.Created,
                Usage = response.Usage,
                Note = response.Note,
                RequestId = response.RequestId,
                ErrorMsg = response.ErrorMsg,
                FinishReason = choice.FinishReason,
                Delta = choice.Delta
            };

            chatMessageContents.Add(new ChatMessageContent(
                role: new AuthorRole(choice.Message.Role ?? AuthorRole.Assistant.ToString()),
                content: choice.Message.Content,
                modelId: modelId,
                innerContent: response,
                encoding: Encoding.UTF8,
                metadata: metadata));
        }
        return chatMessageContents;
    }

    private ChatCompletionsRequest CreateChatRequest(ChatHistory chatHistory, HunyuanPromptExecutionSettings hunyuanFaceExecutionSettings)
    {
        if (this._logger.IsEnabled(LogLevel.Trace))
        {
            this._logger.LogTrace("ChatHistory: {ChatHistory}, Settings: {Settings}",
                JsonSerializer.Serialize(chatHistory),
                JsonSerializer.Serialize(hunyuanFaceExecutionSettings));
        }

        ChatCompletionsRequest request = FromChatHistoryAndExecutionSettings(chatHistory, hunyuanFaceExecutionSettings);

        return request;
    }

    private static ChatCompletionsRequest FromChatHistoryAndExecutionSettings(ChatHistory chatHistory, HunyuanPromptExecutionSettings executionSettings)
    {
        return new ChatCompletionsRequest
        {
            Messages = chatHistory.Select(message => new Message
            {
                Content = message.Content,
                Role = message.Role.ToString(),
            }).ToArray(),
            Temperature = executionSettings.Temperature,
            Model = executionSettings.ModelId,
            TopP = executionSettings.TopP,
            EnableEnhancement = executionSettings.EnableEnhancement,
            StreamModeration = executionSettings.StreamModeration,
            Stream = executionSettings.Stream
        };
    }

    private static IEnumerator<StreamingChatMessageContent> ProcessChatResponseStream(ChatCompletionsResponse response, string modelId)
    {
        IEnumerator<AbstractSSEModel.SSE> responseEnumerator = response.GetEnumerator();

        while (true)
        {
            if (!responseEnumerator.MoveNext())
            {
                break;
            }
            else
            {
                ChatCompletionsResponse? sseResponse = JsonSerializer.Deserialize<ChatCompletionsResponse>(responseEnumerator.Current.Data);
                if (sseResponse is not null)
                {
                    yield return GetStreamingChatMessageContentFromStreamResponse(sseResponse, modelId);
                }
            }

        }
    }

    private static StreamingChatMessageContent GetStreamingChatMessageContentFromStreamResponse(ChatCompletionsResponse response, string modelId)
    {
        Choice? choice = response.Choices?.FirstOrDefault();
        if (choice is not null)
        {
            HunyuanChatCompletionMetadata metadata = new()
            {
                Id = response.Id,
                Created = response.Created,
                Usage = response.Usage,
                Note = response.Note,
                RequestId = response.RequestId,
                ErrorMsg = response.ErrorMsg,
                FinishReason = choice.FinishReason,
                Delta = choice.Delta
            };

            var streamChat = new StreamingChatMessageContent(
                choice.Delta?.Role is not null ? new AuthorRole(choice.Delta.Role) : null,
                choice.Delta?.Content,
                response,
                0,
                modelId,
                Encoding.UTF8,
                metadata);

            return streamChat;
        }

        throw new KernelException("Unexpected response from model")
        {
            Data = { { "ResponseData", response } },
        };
    }

    #region Logging and Meter

    private void LogChatCompletionUsage(HunyuanPromptExecutionSettings executionSettings, ChatCompletionsResponse chatCompletionResponse)
    {
        if (chatCompletionResponse.Usage is null)
        {
            this._logger.LogDebug("Token usage information unavailable.");
            return;
        }

        if (this._logger.IsEnabled(LogLevel.Information))
        {
            this._logger.LogInformation(
                "Prompt tokens: {PromptTokens}. Completion tokens: {CompletionTokens}. Total tokens: {TotalTokens}. ModelId: {ModelId}.",
                chatCompletionResponse.Usage.PromptTokens,
                chatCompletionResponse.Usage.CompletionTokens,
                chatCompletionResponse.Usage.TotalTokens,
                executionSettings.ModelId);
        }

        promptTokensCounter.Add(chatCompletionResponse.Usage.PromptTokens ?? 0);
        completionTokensCounter.Add(chatCompletionResponse.Usage.CompletionTokens ?? 0);
        totalTokensCounter.Add(chatCompletionResponse.Usage.TotalTokens ?? 0);
    }


    private static readonly string @namespace = typeof(HunyuanChatCompletionService).Namespace!;

    /// <summary>
    /// Instance of <see cref="Meter"/> for metrics.
    /// </summary>
    private static readonly Meter meter = new(@namespace);

    /// <summary>
    /// Instance of <see cref="Counter{T}"/> to keep track of the number of prompt tokens used.
    /// </summary>
    private static readonly Counter<long> promptTokensCounter =
        meter.CreateCounter<long>(
            name: $"{@namespace}.tokens.prompt",
            unit: "{token}",
            description: "Number of prompt tokens used");

    /// <summary>
    /// Instance of <see cref="Counter{T}"/> to keep track of the number of completion tokens used.
    /// </summary>
    private static readonly Counter<long> completionTokensCounter =
        meter.CreateCounter<long>(
            name: $"{@namespace}.tokens.completion",
            unit: "{token}",
            description: "Number of completion tokens used");

    /// <summary>
    /// Instance of <see cref="Counter{T}"/> to keep track of the total number of tokens used.
    /// </summary>
    private static readonly Counter<long> totalTokensCounter =
        meter.CreateCounter<long>(
            name: $"{@namespace}.tokens.total",
            unit: "{token}",
            description: "Number of total tokens used");

    #endregion

}