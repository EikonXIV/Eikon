// Codegen unifies discovery's proximity tier and Events' scope into Eikon.Contracts.EventScopeEnum;
// this alias keeps the discovery tests reading as `Tier` (matches the plugin's global alias).
global using Tier = Eikon.Contracts.EventScopeEnum;
