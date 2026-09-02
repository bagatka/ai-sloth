interface AiSloth {
    createSession(sourceCode: SourceCode): Promise<Session>;
    createEmptySession(): Promise<Session>;

    getSession(sessionId: SessionId): Promise<Session>;
    deleteSession(sessionId: SessionId): Promise<void>;
}

interface BaseSession {
    readonly id: SessionId;

    sendMessage(
        prompt: string,
        config: LlmModelConfiguration
    ): Promise<void>;

    stop(): Promise<void>;
}

interface RegularSession extends BaseSession {
    steeringSupported: false;
}

interface SteerableSession extends BaseSession {
    steeringSupported: true;

    steerMessage(
        prompt: string,
        config: LlmModelConfiguration
    ): Promise<void>;
}

type Session = RegularSession | SteerableSession;


interface SourceCode {
    getLoader(): SourceCodeLoader;
}

interface SourceCodeLoader {
    getLoadCommand(): Promise<SandboxCommand>;
}

interface SandboxCommand {
    command: string;
    env?: Record<string, string>;
}

interface SourceCodeProvider {
    getSourceCode(): SourceCode;
}


interface LlmModelConfiguration {
    modelProvider: ModelProvider;
    modelId: ModelId;
    thinkingLevel: ThinkingLevel;
}

type SessionId = string;
type ModelId = string;
type ThinkingLevel = string;

enum ModelProvider {
    OpenRouterApiKey = 'openrouter',
    OpenAIApiKey = 'openai',
    ChatGPTSubscription = 'openai-codex',
}


// Example

const sourceCodeProvider: SourceCodeProvider = {} as SourceCodeProvider;
const aiSloth: AiSloth = {} as AiSloth;

const sourceCode: SourceCode = sourceCodeProvider.getSourceCode();

const sourceCodeLoader: SourceCodeLoader = sourceCode.getLoader();

const loadCommand: SandboxCommand = await sourceCodeLoader.getLoadCommand();

// Sandbox implementation decides WHERE this command is executed.
// The source-code loader only describes HOW to obtain the code.
console.log(loadCommand);

const session: Session = await aiSloth.createSession(sourceCode);

const llmModelConfiguration: LlmModelConfiguration = {
    modelId: 'openai/gpt-6-astra',
    modelProvider: ModelProvider.ChatGPTSubscription,
    thinkingLevel: 'high',
};

await session.sendMessage(
    'Implement Feature A',
    llmModelConfiguration
);

if (session.steeringSupported) {
    await session.steerMessage(
        'Actually, implement Feature B instead',
        llmModelConfiguration
    );
}


// Restore the same session later

const existingSession: Session = await aiSloth.getSession(session.id);

await existingSession.sendMessage(
    'Now add tests',
    llmModelConfiguration
);

await existingSession.stop();

await aiSloth.deleteSession(existingSession.id);
