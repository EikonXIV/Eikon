global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Numerics;
global using System.Threading.Tasks;
global using Dalamud.Bindings.ImGui;
global using OtterGui.Raii;
global using OtterGui.Widgets;

// Discovery's proximity tiers and Events' scope are the same world/dc/region enum, so codegen unifies
// them into Eikon.Contracts.EventScopeEnum. This alias keeps the discovery code reading as `Tier`.
global using Tier = Eikon.Contracts.EventScopeEnum;

// Album and event visibility are the same private/public enum, so codegen unifies them into
// Eikon.Contracts.Visibility. This alias keeps the album code reading as `AlbumVisibilityEnum`.
global using AlbumVisibilityEnum = Eikon.Contracts.Visibility;
