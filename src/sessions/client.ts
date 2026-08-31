import type { SessionCoordinatorNamespace } from "./coordinator";
import type {
  ContinueSessionInput,
  DiscardSessionInput,
  DiscardSessionOutcome,
  GetSessionInput,
  GetWorkingDiffInput,
  PublishSessionInput,
  PublishSessionOutcome,
  SessionDetailsOutcome,
  SessionDiffOutcome,
  SessionOutcome,
  SessionWorkingDiffOutcome,
  StartSessionInput,
} from "./internal/contract";
import { trustedEventRequest } from "./internal/event-request";

export type SessionOperations = {
  start(input: StartSessionInput): Promise<SessionOutcome>;
  continue(input: ContinueSessionInput): Promise<SessionOutcome>;
  get(input: GetSessionInput): Promise<SessionDetailsOutcome>;
  diff(input: GetSessionInput): Promise<SessionDiffOutcome>;
  workingDiff(input: GetWorkingDiffInput): Promise<SessionWorkingDiffOutcome>;
  discard(input: DiscardSessionInput): Promise<DiscardSessionOutcome>;
  publish(input: PublishSessionInput): Promise<PublishSessionOutcome>;
  connectEvents(
    input: GetSessionInput,
    request: Request,
  ): Promise<Response>;
};

export function bindSessions(
  namespace: SessionCoordinatorNamespace,
): SessionOperations {
  return {
    start: (input) => namespace.getByName(input.sessionId).start(input),
    continue: (input) => namespace.getByName(input.sessionId).continue(input),
    get: (input) => namespace.getByName(input.sessionId).get(input),
    diff: (input) => namespace.getByName(input.sessionId).diff(input),
    workingDiff: (input) =>
      namespace.getByName(input.sessionId).workingDiff(input),
    discard: (input) => namespace.getByName(input.sessionId).discard(input),
    publish: (input) => namespace.getByName(input.sessionId).publish(input),
    connectEvents: (input, request) =>
      namespace.getByName(input.sessionId).fetch(
        trustedEventRequest(request, input),
      ),
  };
}
