using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Integrations.OrderIntake.Ai;
using Volo.Abp.DependencyInjection;

namespace OpenTms.TestDoubles;

/// <summary>
/// The language model of the tests (design 72 §5, AD-21's vendor abstraction): no test process ever calls Anthropic. A test
/// scripts the answers in order; an unscripted call fails the test loudly ("AI tokens burned where none should be").
/// Every request is recorded so a test can assert what was (not) sent.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ILanguageModelClient), typeof(ScriptedLanguageModelClient))]
public class ScriptedLanguageModelClient : ILanguageModelClient, ISingletonDependency
{
    private readonly ConcurrentQueue<Func<LanguageModelRequest, LanguageModelResponse>> _answers = new();

    public List<LanguageModelRequest> Requests { get; } = new();

    public void Enqueue(Func<LanguageModelRequest, LanguageModelResponse> answer) => _answers.Enqueue(answer);

    /// <summary>A structured answer with the given documents (usage 1000 in / 200 out).</summary>
    public void Answer(params LearnedDocument[] documents)
        => Enqueue(_ => Response(JsonSerializer.Serialize(new IntakeLearningResult { Documents = documents.ToList() })));

    public void Refuse() => Enqueue(_ => Response("", LanguageModelStopReason.Refusal));

    public void AnswerOutsideTheSchema() => Enqueue(_ => Response("this is not json"));

    public void Fail(bool transient) => Enqueue(_ => throw new LanguageModelException("scripted failure", transient));

    public static LanguageModelResponse Response(string json, LanguageModelStopReason stop = LanguageModelStopReason.EndTurn) => new()
    {
        Json = json,
        StopReason = stop,
        ModelId = "claude-opus-5-5",
        Usage = new LanguageModelUsage { InputTokens = 1000, OutputTokens = 200, CacheReadTokens = 0, CacheWriteTokens = 0 }
    };

    public Task<LanguageModelResponse> CompleteAsync(LanguageModelRequest request, CancellationToken cancellationToken = default)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        if (!_answers.TryDequeue(out var answer))
        {
            throw new InvalidOperationException("Unscripted language-model call: no answer was scripted for this test.");
        }

        return Task.FromResult(answer(request));
    }
}
