﻿// Copyright (c) IdeaTech. All rights reserved.

namespace IdeaTech.SemanticKernel.Connectors.Hunyuan;

/// <summary>
/// Hunyuan Execution Settings.
/// </summary>
public class HunyuanPromptExecutionSettings : PromptExecutionSettings
{
    /// <summary>
    /// Gets the specialization for the Hunyuan execution settings.
    /// </summary>
    /// <param name="executionSettings">Generic prompt execution settings.</param>
    /// <returns>Specialized Hunyuan execution settings.</returns>
    public static HunyuanPromptExecutionSettings FromExecutionSettings(PromptExecutionSettings? executionSettings)
    {
        switch (executionSettings)
        {
            case null:
                return new HunyuanPromptExecutionSettings();
            case HunyuanPromptExecutionSettings settings:
                return settings;
        }

        var json = JsonSerializer.Serialize(executionSettings);

        HunyuanPromptExecutionSettings? hunyuanPromptExecutionSettings = JsonSerializer.Deserialize<HunyuanPromptExecutionSettings>(json, JsonOptionsCache.ReadPermissive);

        return hunyuanPromptExecutionSettings!;
    }

    /// <summary>
    /// 说明：
    /// 1. 较高的数值会使输出更加随机，而较低的数值会使其更加集中和确定。
    /// 2. 默认 1.0，取值区间为 [0.0, 2.0]。
    /// 3. 非必要不建议使用，不合理的取值会影响效果。
    /// </summary>
    public float Temperature
    {
        get => this._temperature;
        set
        {
            this.ThrowIfFrozen();
            this._temperature = value;
        }
    }

    /// <summary>
    /// 说明：
    /// 1. 影响输出文本的多样性，取值越大，生成文本的多样性越强。
    /// 2. 默认 1.0，取值区间为 [0.0, 1.0]。
    /// 3. 非必要不建议使用，不合理的取值会影响效果。
    /// </summary>
    public float TopP
    {
        get => this._topP;
        set
        {
            this.ThrowIfFrozen();
            this._topP = value;
        }
    }

    /// <summary>
    /// 功能增强（如搜索）开关。
    /// 说明：
    /// 1. hunyuan-lite 无功能增强（如搜索）能力，该参数对 hunyuan-lite 版本不生效。
    /// 2. 未传值时默认打开开关。
    /// 3. 关闭时将直接由主模型生成回复内容，可以降低响应时延（对于流式输出时的首字时延尤为明显）。但在少数场景里，回复效果可能会下降。
    /// 4. 安全审核能力不属于功能增强范围，不受此字段影响。
    /// </summary>
    public bool? EnableEnhancement
    {
        get => this._enableEnhancement;
        set
        {
            this.ThrowIfFrozen();
            this._enableEnhancement = value;
        }
    }

    /// <summary>
    /// 流式输出审核开关。
    /// 说明：
    /// 1. 当使用流式输出（Stream 字段值为 true）时，该字段生效。
    /// 2. 输出审核有流式和同步两种模式，**流式模式首包响应更快**。未传值时默认为流式模式（true）。
    /// 3. 如果值为 true，将对输出内容进行分段审核，审核通过的内容流式输出返回。如果出现审核不过，响应中的 FinishReason 值为 sensitive。
    /// 4. 如果值为 false，则不使用流式输出审核，需要审核完所有输出内容后再返回结果。
    /// 注意：
    /// 当选择流式输出审核时，可能会出现部分内容已输出，但中间某一段响应中的 FinishReason 值为 sensitive，此时说明安全审核未通过。如果业务场景有实时文字上屏的需求，需要自行撤回已上屏的内容，并建议自定义替换为一条提示语，如 “这个问题我不方便回答，不如我们换个话题试试”，以保障终端体验。
    /// </summary>
    public bool? StreamModeration
    {
        get => this._streamModeration;
        set
        {
            this.ThrowIfFrozen();
            this._streamModeration = value;
        }
    }

    /// <summary>
    /// The system prompt to use when generating text using a chat model.
    /// Defaults to "Assistant is a large language model."
    /// Only works for text generation service.
    /// </summary>
    /// <remarks>
    /// Please note that the current system prompt for the Hunyuan model is not very stable!
    /// </remarks>
    public string? ChatSystemPrompt
    {
        get => this._chatSystemPrompt;
        set
        {
            this.ThrowIfFrozen();
            this._chatSystemPrompt = value;
        }
    }

    /// <inheritdoc />
    public override PromptExecutionSettings Clone()
    {
        return new HunyuanPromptExecutionSettings()
        {
            ModelId = this.ModelId,
            Temperature = this.Temperature,
            TopP = this.TopP,
            EnableEnhancement = this.EnableEnhancement,
            StreamModeration = this.StreamModeration,
            ExtensionData = this.ExtensionData is not null ? new Dictionary<string, object>(this.ExtensionData) : null
        };
    }

    private float _temperature;

    private float _topP;

    private bool? _enableEnhancement;

    private bool? _streamModeration;

    private string? _chatSystemPrompt = "Assistant is a large language model.";
}
