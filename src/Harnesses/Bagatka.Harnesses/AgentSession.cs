using System;
using System.Collections.Generic;

namespace Bagatka.Harnesses;

/// <summary>
/// One agent's conversation over the Agent Client Protocol (ACP), as plain state its caller keeps with
/// whatever it saves, so another process can continue it after a restart: the session prompts go to,
/// the earlier session the next agent continues when it can load it, whether the agent takes messages
/// into a running turn, and whether it is replaying an earlier session's history. It answers the
/// harness's requests, starts or loads a session, and matches every answer to its prompt; its caller
/// writes the lines it gives to the harness and acts on what happened. It does no I/O.
/// </summary>
/// <param name="SessionId">The session prompts go to, once the agent started or loaded it.</param>
/// <param name="Earlier">The session the next agent continues when it can load it.</param>
/// <param name="CanSteer">Whether the agent takes messages into a running turn (<see cref="Steer"/>).</param>
/// <param name="Loading">Whether the agent replays the earlier session's history, which its caller has already.</param>
public sealed record AgentSession(string? SessionId = null, string? Earlier = null, bool CanSteer = false, bool Loading = false)
{
    /// <summary>
    /// A new agent process's first line, which names this client: some agents pass the name on to their
    /// model's vendor, such as <c>aisloth</c>. The agent answers, and <see cref="Read"/> continues the
    /// earlier session when it can, or starts a new one.
    /// </summary>
    public static string Start(string clientName, string clientTitle, string clientVersion)
    {
        return Acp.Initialize(clientName, clientTitle, clientVersion);
    }

    /// <summary>The session once its agent stopped or was lost: the next agent continues it when it can.</summary>
    public AgentSession Ended()
    {
        return new AgentSession(SessionId: null, Earlier: SessionId ?? Earlier);
    }

    /// <summary>
    /// What a line the harness wrote means (<see cref="AgentStep.Event"/>, null for noise, history the
    /// caller has, or what needs no caller), and the lines to answer it with, working in
    /// <paramref name="workingDirectory"/>, an absolute path.
    /// </summary>
    public AgentStep Read(string line, string workingDirectory)
    {
        AcpEvent? read = Acp.Read(line);
        if (read is not AcpEvent known)
        {
            return Step(this);
        }

        return known switch
        {
            AcpUpdate when Loading => Step(this),
            AcpUpdate update => Step(this, new AgentEvent(new AgentReported(update.Update, update.Model))),
            AcpRequest request => Step(this, line: request.IsPermissionRequest ? Acp.Allow(request) : Acp.MethodNotFound(request)),
            AcpInitialized initialized => Initialized(initialized, workingDirectory),
            AcpLoadFailed => Step(this with { Loading = false }, line: Acp.NewSession(workingDirectory)),
            AcpSessionLoaded loaded => Step(new AgentSession(Earlier, Earlier: null, CanSteer), new AgentEvent(new SessionReady(loaded.Model, Remembers: true))),
            AcpSessionCreated created => Step(new AgentSession(created.SessionId, Earlier: null, CanSteer), new AgentEvent(new SessionReady(created.Model, Remembers: Earlier is null ? null : false))),
            AcpStartFailed failed => Step(this, new AgentEvent(new StartFailed(failed.Error))),
            AcpPromptEnded ended => Step(this, new AgentEvent(new PromptAnswered(ended.Prompt, ended.StopReason, Error: null))),
            AcpPromptFailed failed => Step(this, new AgentEvent(new PromptAnswered(failed.Prompt, "failed", failed.Error))),
            AcpSteerAnswered answered => Step(this, new AgentEvent(new SteerAnswered(answered.Message, answered.Injected))),
        };
    }

    /// <summary>
    /// Sends a message, starting a turn: the agent's answer is a <see cref="PromptAnswered"/> with
    /// <paramref name="key"/>, even when it is read from saved output after a restart.
    /// </summary>
    public string Prompt(Guid key, string text)
    {
        return Acp.Prompt(key, Ready(), text);
    }

    /// <summary>
    /// Offers a message to the running turn, when the agent can take it (<see cref="CanSteer"/>): the
    /// answer is a <see cref="SteerAnswered"/> with <paramref name="key"/>, which says whether it joined
    /// the turn, or that no turn runs and it needs a prompt.
    /// </summary>
    public string Steer(Guid key, string text)
    {
        if (!CanSteer)
        {
            throw new InvalidOperationException("The agent takes no messages into a running turn.");
        }

        return Acp.Steer(key, Ready(), text);
    }

    /// <summary>Asks the agent to end the running turn; its prompt then answers <c>cancelled</c>.</summary>
    public string Cancel()
    {
        return Acp.Cancel(Ready());
    }

    // The agent says how it works: it continues the earlier session when it can load one.
    private AgentStep Initialized(AcpInitialized initialized, string workingDirectory)
    {
        bool loads = initialized.SupportsLoading && Earlier is not null;
        AgentSession next = this with { CanSteer = initialized.SupportsSteering, Loading = loads };
        return Step(next, line: loads ? Acp.LoadSession(Earlier!, workingDirectory) : Acp.NewSession(workingDirectory));
    }

    private string Ready()
    {
        if (SessionId is null)
        {
            throw new InvalidOperationException("The agent has no session yet.");
        }

        return SessionId;
    }

    private static AgentStep Step(AgentSession next, AgentEvent? happened = null, string? line = null)
    {
        IReadOnlyList<string> lines = line is null ? [] : [line];
        return new AgentStep(next, lines, happened);
    }
}
