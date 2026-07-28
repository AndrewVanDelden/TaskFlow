// Shared manual mock for @microsoft/signalr. Any test can opt in with a bare
// `vi.mock('@microsoft/signalr')`. It prevents tests from opening a real hub connection
// (no /hubs/agents negotiate) and records handlers so a test can simulate a server push
// via emit().
type Handler = (...args: unknown[]) => void

class FakeHubConnection {
  state = 'Disconnected'
  private handlers: Record<string, Handler[]> = {}

  on(event: string, cb: Handler) {
    this.handlers[event] = this.handlers[event] ?? []
    this.handlers[event].push(cb)
  }
  off(event: string, cb: Handler) {
    this.handlers[event] = (this.handlers[event] ?? []).filter((h) => h !== cb)
  }
  onreconnected() {}
  onclose() {}
  start() {
    return Promise.resolve()
  }
  stop() {
    return Promise.resolve()
  }
  // Test helper: invoke every handler registered for an event.
  emit(event: string, ...args: unknown[]) {
    const list = this.handlers[event] ?? []
    list.forEach((h) => h(...args))
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
