# Service ↔ agent protocol

The service listens on the named pipe **`\\.\pipe\AutoSettings.Service`**. Each agent connects as a client and both
sides speak **JSON-RPC 2.0** (StreamJsonRpc, header-delimited messages, System.Text.Json) in both directions over the
same connection.

The contracts are in `src/AutoSettings.Core/Ipc/Contracts.cs`.

## Agent → service (`IServiceApi`)

| Method | Purpose |
|---|---|
| `RegisterAgentAsync(AgentHello)` → `ServiceHello` | Registers the agent for its session. The service answers with its version, the process monitor in use and the session's user. Pending logon events are delivered right after. |
| `GetMachineActivityAsync()` | The machine activity log, shown in the agent's Activity tab. |
| `AgentExitingAsync()` | The user closed the agent: do not restart it until the next sign-in. |

## Service → agent (`IAgentApi`)

| Method | Purpose |
|---|---|
| `OnSystemEventAsync(SystemEvent)` | Logon (held until the agent connects, max 5 min), logoff, lock, unlock, app started/closed for the agent's session. |
| `ExecuteActionAsync(RemoteAction)` → `bool` | Run a user action on behalf of a machine automation. |
| `CaptureActionAsync(RemoteAction)` → `string?` | Capture the current value for a revertible action (machine profiles). |
| `RestoreActionAsync(RemoteAction, string?)` | Restore a captured value. |

`RemoteAction` carries the action type, its parameters as a JSON object (`PlainJson`), the triggering event and the
automation's id and name.

## Connection lifecycle

- The agent connects at start, and reconnects with exponential backoff (2 s to 30 s) whenever the connection drops.
- Until the first connection succeeds (or after it drops), the agent detects app starts/exits itself by polling.
- The service identifies the agent's session from the **pipe client's process** (`GetNamedPipeClientProcessId` +
  `ProcessIdToSessionId`), never from what the agent says.
- The agent only accepts a pipe server that runs in **session 0**, so another user cannot impersonate the service.

See also the [security model](security.md).
