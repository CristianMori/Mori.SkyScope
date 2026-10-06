// Mori.SkyScope — Maps a WebSocket endpoint that streams the broadcaster's frames.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mori.SkyScope.Streaming;

/// <summary>Endpoint-routing extensions that expose a <see cref="FrameBroadcaster"/> over WebSocket.</summary>
public static class StreamEndpoints
{
    /// <summary>Maps a WebSocket endpoint that streams the broadcaster's frames. Call <c>app.UseWebSockets()</c> first.</summary>
    public static IEndpointConventionBuilder MapSkyScopeStream(this IEndpointRouteBuilder app, string pattern, FrameBroadcaster broadcaster)
        => app.Map(pattern, async (HttpContext http) =>
        {
            if (!http.WebSockets.IsWebSocketRequest) { http.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            await broadcaster.ServeAsync(socket, http.RequestAborted);
        });
}
