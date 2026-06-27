using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Muselly.Core.Models;
using Muselly.Server.Api;
using Muselly.Server.Auth;
using Muselly.Server.Protocol;

namespace Muselly.WebHost;

/// <summary>
/// Maps the JSON/binary HTTP API onto <see cref="MusellyApiService"/>. The shape mirrors the framed protocol
/// (hello / login / library / media / stream / admin) so the browser client is just a different transport to
/// the same engine. Auth is a Bearer token (also accepted as a <c>?token=</c> query for media/stream, which
/// the browser's media elements can't add headers to). Admin routes additionally require the Admin role.
/// </summary>
public static class WebApi
{
    public static void Map(WebApplication app, MusellyApiService api)
    {
        app.MapGet("/api/hello", () => Results.Json(api.Hello()));

        app.MapPost("/api/login", (LoginRequest req) =>
            Results.Json(api.Login(req.Username, req.Password, req.Guest)));

        app.MapPost("/api/share-login", (ShareLoginRequest req) =>
            Results.Json(api.ShareLogin(req.Token)));

        app.MapGet("/api/library", (HttpContext ctx) =>
            RequireAuth(ctx, api, session => Results.Json(api.GetLibrary(session, ctx.Request.Query["etag"]))));

        app.MapGet("/api/album-art", (HttpContext ctx) =>
            RequireAuth(ctx, api, session => FileOr404(api.AlbumArtPath(session, ctx.Request.Query["key"]!), "image/jpeg")));

        app.MapGet("/api/artist-image", (HttpContext ctx) =>
            RequireAuth(ctx, api, session => FileOr404(api.ArtistImagePath(session, ctx.Request.Query["key"]!), "image/jpeg")));

        // These return Task<IResult>; cast to Delegate so minimal APIs write the result instead of
        // treating the lambda as a RequestDelegate and discarding it (ASP0016).
        app.MapGet("/api/artist-bio", (Delegate)((HttpContext ctx) =>
            RequireAuthAsync(ctx, api, async session =>
                Results.Json(new TextResponse { Text = await api.GetArtistBioAsync(session, ctx.Request.Query["key"]!, ctx.RequestAborted) }))));

        app.MapGet("/api/lyrics", (Delegate)((HttpContext ctx) =>
            RequireAuthAsync(ctx, api, async session =>
                Results.Json(new TextResponse { Text = await api.GetLyricsAsync(session, ctx.Request.Query["id"]!, ctx.RequestAborted) }))));

        app.MapGet("/api/stream", (Delegate)((HttpContext ctx) =>
            RequireAuthAsync(ctx, api, async session =>
            {
                int.TryParse(ctx.Request.Query["bitrate"], out var bitrate);
                var path = await api.GetStreamPathAsync(session, ctx.Request.Query["id"]!, bitrate, ctx.RequestAborted);
                return path is null
                    ? Results.NotFound()
                    : Results.File(path, "audio/ogg", enableRangeProcessing: true);
            })));

        // --- Users / profiles / per-user data --------------------------------------------------------

        app.MapGet("/api/users", (HttpContext ctx) =>
            RequireAuth(ctx, api, session => Results.Json(api.ListUserProfiles(session))));

        app.MapGet("/api/users/profile", (HttpContext ctx) =>
            RequireAuth(ctx, api, session =>
            {
                var profile = api.GetUserProfile(session, ctx.Request.Query["username"]!);
                return profile is null ? Results.NotFound() : Results.Json(profile);
            }));

        // A signed-in (non-guest) user edits their own profile (admins may edit anyone — enforced in the API).
        app.MapPost("/api/users/profile", (HttpContext ctx, UpdateProfileRequest req) =>
            RequireUser(ctx, api, session =>
                api.UpdateUserProfile(session, req) ? Results.Ok() : Results.Forbid()));

        app.MapGet("/api/users/favorites", (HttpContext ctx) =>
            RequireAuth(ctx, api, session => Results.Json(api.GetFavorites(session, ctx.Request.Query["username"]!))));

        app.MapPost("/api/users/favorites", (HttpContext ctx, ToggleFavoriteRequest req) =>
            RequireUser(ctx, api, session => Results.Json(api.ToggleFavorite(session, req))));

        app.MapGet("/api/users/history", (HttpContext ctx) =>
            RequireAuth(ctx, api, session => Results.Json(api.GetHistory(session, ctx.Request.Query["username"]!))));

        // --- Admin -----------------------------------------------------------------------------------

        app.MapGet("/api/admin/settings", (HttpContext ctx) =>
            RequireAdmin(ctx, api, _ => Results.Json(api.GetSettings())));

        app.MapPost("/api/admin/settings", (HttpContext ctx, ServerSettingsDto dto) =>
            RequireAdmin(ctx, api, _ => { api.UpdateSettings(dto); return Results.Ok(); }));

        app.MapPost("/api/admin/folders", (HttpContext ctx, FolderRequest req) =>
            RequireAdmin(ctx, api, _ => { api.AddFolder(req.Path); return Results.Ok(); }));

        app.MapMethods("/api/admin/folders", new[] { "DELETE" }, (HttpContext ctx) =>
            RequireAdmin(ctx, api, _ => { api.RemoveFolder(ctx.Request.Query["path"]!); return Results.Ok(); }));

        app.MapPost("/api/admin/rescan", (HttpContext ctx, FolderRequest? req) =>
            RequireAdmin(ctx, api, _ => { api.RescanAsync(req?.Path); return Results.Ok(); }));

        app.MapGet("/api/admin/users", (HttpContext ctx) =>
            RequireAdmin(ctx, api, _ => Results.Json(api.ListUsers())));

        app.MapPost("/api/admin/users", (HttpContext ctx, AddUserRequest req) =>
            RequireAdmin(ctx, api, _ => { api.AddUser(req.Username, req.Password, req.Role); return Results.Ok(); }));

        app.MapPost("/api/admin/users/role", (HttpContext ctx, AddUserRequest req) =>
            RequireAdmin(ctx, api, _ => { api.SetUserRole(req.Username, req.Role); return Results.Ok(); }));

        app.MapMethods("/api/admin/users", new[] { "DELETE" }, (HttpContext ctx) =>
            RequireAdmin(ctx, api, _ => { api.RemoveUser(ctx.Request.Query["username"]!); return Results.Ok(); }));

        // Any signed-in User or Admin can create a share link (guests/share-link guests cannot).
        app.MapPost("/api/shares", (HttpContext ctx, CreateShareRequest req) =>
            RequireUser(ctx, api, _ => Results.Json(api.CreateShare(req))));

        app.MapGet("/api/admin/shares", (HttpContext ctx) =>
            RequireAdmin(ctx, api, _ => Results.Json(api.ListShares())));

        app.MapPost("/api/admin/shares", (HttpContext ctx, CreateShareRequest req) =>
            RequireAdmin(ctx, api, _ => Results.Json(api.CreateShare(req))));

        app.MapMethods("/api/admin/shares", new[] { "DELETE" }, (HttpContext ctx) =>
            RequireAdmin(ctx, api, _ => { api.RevokeShare(ctx.Request.Query["id"]!); return Results.Ok(); }));
    }

    // --- Auth helpers --------------------------------------------------------------------------------

    private static Session? Resolve(HttpContext ctx, MusellyApiService api)
    {
        string? token = null;
        var header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            token = ctx.Request.Query["token"];
        return api.Authenticate(token);
    }

    private static IResult RequireAuth(HttpContext ctx, MusellyApiService api, Func<Session, IResult> handler)
    {
        var session = Resolve(ctx, api);
        return session is null ? Results.Unauthorized() : handler(session);
    }

    private static async Task<IResult> RequireAuthAsync(HttpContext ctx, MusellyApiService api,
        Func<Session, Task<IResult>> handler)
    {
        var session = Resolve(ctx, api);
        return session is null ? Results.Unauthorized() : await handler(session);
    }

    private static IResult RequireAdmin(HttpContext ctx, MusellyApiService api, Func<Session, IResult> handler)
    {
        var session = Resolve(ctx, api);
        if (session is null) return Results.Unauthorized();
        if (session.Role != UserRole.Admin) return Results.Forbid();
        return handler(session);
    }

    /// <summary>Requires a signed-in User or Admin (excludes guests and share-link guests).</summary>
    private static IResult RequireUser(HttpContext ctx, MusellyApiService api, Func<Session, IResult> handler)
    {
        var session = Resolve(ctx, api);
        if (session is null) return Results.Unauthorized();
        if (session.Role == UserRole.Guest) return Results.Forbid();
        return handler(session);
    }

    private static IResult FileOr404(string? path, string contentType) =>
        path is null ? Results.NotFound() : Results.File(path, contentType);
}
