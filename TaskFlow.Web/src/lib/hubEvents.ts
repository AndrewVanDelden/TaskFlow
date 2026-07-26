// Mirror of the backend's TaskFlow.Api/Hubs/HubEvents.cs. These strings are a cross-language
// contract with the C# hub: a typo on either side silently stops the live feed. There is no
// shared type across the language boundary, so each side keeps its own copy.
export const HubEvents = {
  AgentAction: 'AgentAction',
  AgentCycle: 'AgentCycle',
} as const
