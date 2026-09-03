/**
 * AI Sloth's complete high-level design.
 *
 * An AiSloth instance is already authenticated and scoped to the current
 * person or team. HTTP, storage, Git, Pi, and Cloudflare are implementation
 * details and do not appear here.
 */

export interface AiSloth {
    createSession(sourceCode: SourceCode): Promise<Session>;
    createEmptySession(): Promise<Session>;

    getSessions(): Promise<readonly Session[]>;
    getSession(sessionId: SessionId): Promise<Session>;
    deleteSession(sessionId: SessionId): Promise<void>;
}


// Sessions

export interface BaseSession {
    readonly id: SessionId;

    /** Current state of the remote session. */
    getStatus(): Promise<SessionStatus>;

    /**
     * Accepts a prompt and starts the agent in the background.
     * Progress and completion are observed through getEvents().
     */
    sendMessage(
        prompt: string,
        config: LlmModelConfiguration,
    ): Promise<void>;

    /** Stops the active agent operation. The session and its files remain. */
    stop(): Promise<void>;

    /** Replays retained events, then follows new events until cancelled. */
    getEvents(after?: SessionEventId): AsyncIterable<SessionEvent>;

    /** Cumulative changes from the source initially loaded into this session. */
    getChanges(): Promise<SourceCodeChanges>;

    /** Downloads current source files without agent or platform state. */
    downloadSourceCode(): Promise<SourceCodeDownload>;

    /**
     * Stops agent processes, then runs an explicit trusted publication.
     * Publication credentials are never available to the agent process.
     */
    publishSourceCode(
        publisher: SourceCodePublisher,
    ): Promise<SourceCodePublication>;
}

export interface RegularSession extends BaseSession {
    readonly steeringSupported: false;
}

export interface SteerableSession extends BaseSession {
    readonly steeringSupported: true;

    /** Adds guidance to the active operation without starting another one. */
    steerMessage(
        prompt: string,
        config: LlmModelConfiguration,
    ): Promise<void>;
}

export type Session = RegularSession | SteerableSession;

export type SessionStatus = "idle" | "running" | "stopping";

export type SessionEvent =
    | Readonly<{
        id: SessionEventId;
        type: "message";
        role: "user" | "agent";
        text: string;
    }>
    | Readonly<{
        id: SessionEventId;
        type: "activity";
        name: string;
        status: "started" | "running" | "completed" | "failed";
        text?: string;
    }>
    | Readonly<{
        id: SessionEventId;
        type: "status";
        status: SessionStatus;
    }>;

export interface SourceCodeChanges {
    readonly unifiedDiff: string;
    readonly truncated: boolean;
}

export interface SourceCodeDownload {
    readonly fileName: string;
    readonly size: number;
    readonly content: ReadableStream<Uint8Array>;
}


// Source code in

/**
 * A trusted server-side capability, not data accepted directly from a client.
 * It may represent GitHub, an uploaded archive, object storage, or generated
 * source code.
 */
export interface SourceCode {
    getLoader(): SourceCodeLoader;
}

/** Describes how an empty sandbox obtains a specific source tree. */
export interface SourceCodeLoader {
    getLoadCommand(): Promise<SandboxCommand>;
}

export interface SourceCodeProvider {
    getSourceCode(): SourceCode;
}


// Source code out

/** Describes one explicit destination for the session's current source code. */
export interface SourceCodePublisher {
    getPublishCommand(): Promise<SandboxCommand>;

    /** Converts successful command output into a provider-owned receipt. */
    getPublication(
        result: SandboxCommandResult,
    ): Promise<SourceCodePublication>;
}

export interface SourceCodePublisherProvider {
    getSourceCodePublisher(): SourceCodePublisher;
}

export interface SourceCodePublication {
    readonly reference: string;
    readonly url?: string;
}


// Sandboxes

/** Decides where disposable sandbox processes execute. */
export interface SandboxHost {
    createSandbox(): Promise<Sandbox>;
}

export interface Sandbox {
    /** Starts one isolated process in the sandbox workspace. */
    start(command: SandboxCommand): Promise<SandboxProcess>;

    /** Stops remaining processes and permanently destroys this sandbox. */
    destroy(): Promise<void>;
}

/**
 * Optional directory backup capability. It does not snapshot process state or
 * replace a sandbox's complete disk. Cache and restore policy lives elsewhere.
 */
export interface SandboxBackups {
    create(
        sandbox: Sandbox,
        directory: string,
    ): Promise<SandboxBackup>;

    /** The caller must stop processes that can access the directory first. */
    restore(
        sandbox: Sandbox,
        backup: SandboxBackup,
    ): Promise<void>;
}

/** Opaque handle understood by the SandboxBackups implementation that made it. */
export interface SandboxBackup {
    readonly id: string;
}

export interface SandboxProcess {
    readonly output: AsyncIterable<SandboxOutput>;

    /** Used by agents that support steering or other interactive input. */
    write(input: string): Promise<void>;

    stop(): Promise<void>;
    wait(): Promise<SandboxCommandResult>;
}

/**
 * Environment variables belong only to this process. A sandbox must not carry
 * loader or publisher credentials into a later agent process.
 */
export interface SandboxCommand {
    readonly command: string;
    readonly env?: Readonly<Record<string, string>>;
}

export interface SandboxOutput {
    readonly channel: "stdout" | "stderr";
    readonly text: string;
}

export interface SandboxCommandResult {
    readonly exitCode: number;
    readonly stdout: string;
    readonly stderr: string;
}


// Models

/** Model credentials are resolved by the authenticated AI Sloth instance. */
export interface LlmModelConfiguration {
    readonly modelProvider: ModelProvider;
    readonly modelId: ModelId;
    readonly thinkingLevel: ThinkingLevel;
}

export enum ModelProvider {
    OpenRouterApiKey = "openrouter",
    OpenAIApiKey = "openai",
    ChatGPTSubscription = "openai-codex",
}

export type SessionId = string;
export type SessionEventId = number;
export type ModelId = string;
export type ThinkingLevel = string;


// Complete usage

async function example(
    aiSloth: AiSloth,
    githubRepository: SourceCodeProvider & SourceCodePublisherProvider,
): Promise<void> {
    const sourceCode = githubRepository.getSourceCode();

    // A source driver describes HOW to load code. The sandbox host decides WHERE.
    const loadCommand = await sourceCode.getLoader().getLoadCommand();
    console.log(loadCommand);

    const session = await aiSloth.createSession(sourceCode);

    const model: LlmModelConfiguration = {
        modelId: "openai/gpt-6-astra",
        modelProvider: ModelProvider.ChatGPTSubscription,
        thinkingLevel: "high",
    };

    await session.sendMessage("Implement Feature A", model);

    if (session.steeringSupported) {
        await session.steerMessage("Actually, implement Feature B instead", model);
    }

    for await (const event of session.getEvents()) {
        console.log(event);

        if (event.type === "status" && event.status === "idle") {
            break;
        }
    }

    const changes = await session.getChanges();
    console.log(changes.unifiedDiff);

    const download = await session.downloadSourceCode();
    console.log(download.fileName, download.size);

    const publisher = githubRepository.getSourceCodePublisher();
    const publication = await session.publishSourceCode(publisher);
    console.log(publication.url);

    // Restore the same files and agent conversation later.
    const existingSession = await aiSloth.getSession(session.id);
    await existingSession.sendMessage("Now add tests", model);
    await existingSession.stop();

    await aiSloth.deleteSession(existingSession.id);
}

void example;

type Email = string;
type Password = string;
type TokenPair = {
    accessToken: AccessToken;
    refreshToken: RefreshToken;
}
type AccessToken = string;
type RefreshToken = string;
type TeamName = string;
type TeamId = string;
type TeamInvitationCode = string;
type UserId = string;
type GitHubAccountId = string;
type SourceCodeId = string;
enum Agent {
    Pi = 'pi',
    GitHubCopilot = 'github-copilot',
    ClaudeCode = 'claude-code',
    Codex = 'codex',
    OpenCode = 'opencode'
}

interface AiSlothHttpServer {
    createAccount(email: Email, password: Password): Promise<boolean>;

    signIn(email: Email, password: Password): Promise<TokenPair>;
    refreshToken(refreshToken: RefreshToken): Promise<TokenPair>;
    signOut(refreshToken: RefreshToken): Promise<boolean>;

    createTeam(name: TeamName, accessToken: string): Promise<TeamId>;
    deleteTeam(teamId: TeamId, accessToken: AccessToken): Promise<boolean>;
    joinTeam(invitationCode: TeamInvitationCode, accessToken: AccessToken): Promise<TeamId>;
    leaveTeam(teamId: TeamId, accessToken: AccessToken): Promise<boolean>;
    inviteToTeam(teamId: TeamId, invitationTargetEmail: Email): Promise<TeamInvitationCode>;
    kickFromTeam(teamId: TeamId, targetUserId: UserId, accessToken: AccessToken): Promise<boolean>;
    renameTeam(teamId: TeamId, newName: TeamName, accessToken: AccessToken): Promise<boolean>;

    connectGitHubAccount(accessToken: AccessToken): Promise<void>;
    disconnectGitHubAccount(GitHubAccoundId: GitHubAccountId, accessToken: AccessToken): Promise<boolean>;
    // Other GitHub connection related endpoints

    // Endpoints to upload repo as archive and use it as source code for sessions

    createSession(sourceCodeId: SourceCodeId, agent: Agent, accessToken: AccessToken): Promise<SessionId>;

}
