// Shared manual mock for @microsoft/signalr. Any test can opt in with a bare
// `vi.mock('@microsoft/signalr')`. It prevents tests from opening a real hub connection
// (no /hubs/agents negotiate), so mounting useAgentFeed / the Dashboard stays offline.
class FakeHubConnection {
  state = 'Disconnected'
  on() {}
  onreconnected() {}
  onclose() {}
  start() {
    return Promise.resolve()
  }
  stop() {
    return Promise.resolve()
  }
}

export class HubConnectionBuilder {
  withUrl() {
    return this
  }
  withAutomaticReconnect() {
    return this
  }
  build() {
    return new FakeHubConnection()
  }
}

export const HubConnectionState = { Disconnected: 'Disconnected' }
