using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xgame.Controllers;

public static class ConfigJson
{
    public static JsonNode json = ReadFile("appsettings.json") ?? new JsonObject();
    // ConcurrentDictionary 保证文件监控回调与读取操作的线程安全
    public static readonly ConcurrentDictionary<string, JsonNode> dictConfig = new ConcurrentDictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

    public static JsonNode? ReadFile(string jsonFile)
    {
        try
        {
            if (jsonFile.Length > 2 && jsonFile[1] != ':') jsonFile = Path.Combine(AppContext.BaseDirectory, jsonFile);

            if (!File.Exists(jsonFile)) return null;

            string jsonString = File.ReadAllText(jsonFile);
            var docOptions = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            return JsonNode.Parse(jsonString, nodeOptions: null, documentOptions: docOptions);
        }
        catch
        {
            return null;
        }
    }

    public static JsonNode? Get(string configFileName)
    {
        try
        {
            if (dictConfig.TryGetValue(configFileName, out var cached)) return cached;

            JsonNode? jToken = ReadFile("config/" + configFileName);
            if (jToken != null) dictConfig.TryAdd(configFileName, jToken);
            return jToken;
        }
        catch
        {
            return null;
        }
    }

    static string configPath = Path.Combine(AppContext.BaseDirectory, "config").Replace("\\", "/");

    //监控配置被修改
    public static WatcherFolder? watcherFolder = Directory.Exists(configPath) ? new WatcherFolder(configPath, (fullPath, changeType, fileSize, lastWriteTime) =>
    {
        if (System.IO.Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            dictConfig.TryRemove(System.IO.Path.GetFileName(fullPath), out _);
            Logger.d($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - 文件被修改: {fullPath}");
        }
    }) : null;
}

