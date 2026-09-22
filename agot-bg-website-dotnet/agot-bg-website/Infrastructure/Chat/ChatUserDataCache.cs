using Microsoft.Extensions.Caching.Memory;

namespace agot_bg_website.Infrastructure.Chat;

internal static class ChatUserDataCache
{
    internal static string GetKey(Guid userId) => $"chat:user-data:{userId}";

    internal static void Invalidate(IMemoryCache memoryCache, Guid userId) =>
        memoryCache.Remove(GetKey(userId));
}
