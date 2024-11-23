﻿using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace KernelMemory.Evaluation.Evaluators;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

internal class AnswerCorrectnessEvaluator : EvaluationEngine
{
    private readonly Kernel kernel;

    private KernelFunction ExtractStatements => this.kernel.CreateFunctionFromPrompt(GetSKPrompt("Extraction", "Statements"), new OpenAIPromptExecutionSettings
    {
        Temperature = 1e-8f,
        TopP = 1e-8f,
        Seed = 0,
        ResponseFormat = "json_object"        
    }, functionName: nameof(ExtractStatements));

    private KernelFunction EvaluateCorrectness => this.kernel.CreateFunctionFromPrompt(GetSKPrompt("Evaluation", "Correctness"), new OpenAIPromptExecutionSettings
    {
        Temperature = 1e-8f,
        TopP = 1e-8f,
        Seed = 0,
        ResponseFormat = "json_object",
    }, functionName: nameof(EvaluateCorrectness));

    public AnswerCorrectnessEvaluator(Kernel kernel)
    {
        this.kernel = kernel.Clone();
    }

    internal async Task<float> Evaluate(TestSet.TestSet testSet, MemoryAnswer answer, Dictionary<string, object?> metadata)
    {
        var statements = await Try(3, async (remainingTry) =>
        {
            var extraction = await ExtractStatements.InvokeAsync(this.kernel, new KernelArguments
                {
                    { "question", answer.Question },
                    { "answer", answer.Result }
                }).ConfigureAwait(false);

            return JsonSerializer.Deserialize<StatementExtraction>(extraction.GetValue<string>()!);
        }).ConfigureAwait(false);

        if (statements is null)
        {
            return 0;
        }

        var evaluation = await Try(3, async (remainingTry) =>
        {
            var extraction = await EvaluateCorrectness.InvokeAsync(this.kernel, new KernelArguments
                {
                    { "question", answer.Question },
                    { "answer", JsonSerializer.Serialize(statements) },
                    { "ground_truth", JsonSerializer.Serialize(testSet.Context) }
                }).ConfigureAwait(false);

            return JsonSerializer.Deserialize<CorrectnessEvaluation>(extraction.GetValue<string>()!);
        }).ConfigureAwait(false);

        if (evaluation is null)
        {
            return 0;
        }

        metadata.Add($"{nameof(AnswerCorrectnessEvaluator)}-Evaluation", evaluation);

        return (float)evaluation.TP.Count() / 
            (float)(evaluation.TP.Count() + .5 * (evaluation.FP.Count() + evaluation.FN.Count()));
    }
}

internal class CorrectnessEvaluation
{
    public class StatementEvaluation
    {
        public string Statement { get; set; } = null!;

        public string Reason { get; set; } = null!;
    }

    public IEnumerable<StatementEvaluation> FP { get; set; } = null!;

    public IEnumerable<StatementEvaluation> FN { get; set; } = null!;

    public IEnumerable<StatementEvaluation> TP { get; set; } = null!;
}

public class KeyphraseExtraction
{
    [JsonPropertyName("keyphrases")]
    public List<string> Keyphrases { get; set; } = new List<string>();
}

internal class StatementExtraction
{
    [JsonPropertyName("statements")]
    public List<string> Statements { get; set; } = new List<string>();
}

#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Restore this diagnostic to proceed.
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Restore this diagnostic to proceed.