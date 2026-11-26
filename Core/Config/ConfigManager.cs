using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Games.Palworld;

namespace GameServerKeeper.Core.Config;

/// <summary>
/// config.json (실행 파일 옆) 저장/불러오기.
/// GameSettings 는 GameRegistry 에 등록된 게임 목록으로 다형성 직렬화된다 ("gameId" 판별자).
/// </summary>
public static class ConfigManager
{
    private const string Src = "Config";

    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            // GameId 는 판별자("gameId")로 이미 기록되므로 파생 클래스 속성으로는 저장하지 않음
            // (abstract 속성의 [JsonIgnore] 는 override 에 상속되지 않음)
            if (typeof(GameSettings).IsAssignableFrom(typeInfo.Type) && typeInfo.Kind == JsonTypeInfoKind.Object)
            {
                for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
                    if (typeInfo.Properties[i].Name == nameof(GameSettings.GameId)) typeInfo.Properties.RemoveAt(i);
            }

            if (typeInfo.Type != typeof(GameSettings)) return;
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "gameId",
                UnknownDerivedTypeHandling = System.Text.Json.Serialization.JsonUnknownDerivedTypeHandling.FailSerialization
            };
            foreach (var game in GameRegistry.All)
                typeInfo.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(game.SettingsType, game.GameId));
        });

        return new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 한글 그대로 저장
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            TypeInfoResolver = resolver
        };
    }

    public static T DeepClone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    public static AppConfig Load(IAppLog log)
    {
        if (!File.Exists(ConfigPath))
        {
            var def = CreateDefault();
            Save(def);
            log.Warn(Src, $"config.json 이 없어 기본값(팰월드 서버 1개)으로 생성했습니다: {ConfigPath}");
            return def;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            // v1(팰월드 전용 단일 서버) 형식이면 백업 후 새 형식으로 교체
            if (node?["Servers"] == null)
            {
                var bak = ConfigPath + ".v1.bak";
                File.Copy(ConfigPath, bak, overwrite: true);
                var def = CreateDefault();
                Save(def);
                log.Warn(Src, $"이전 형식의 config.json 을 {Path.GetFileName(bak)} 로 백업하고 새 형식으로 생성했습니다. 설정을 다시 확인하세요.");
                return def;
            }

            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? CreateDefault();
            cfg.Servers.RemoveAll(s => s.Game == null);
            log.Info(Src, $"설정 불러옴: 서버 {cfg.Servers.Count}개");
            Save(cfg); // 새 버전에서 추가된 항목이 파일에 기록되도록
            return cfg;
        }
        catch (Exception ex)
        {
            log.Error(Src, "config.json 을 읽지 못했습니다. 파일은 그대로 두고 기본값으로 실행합니다. (저장하면 덮어씁니다)", ex);
            return CreateDefault();
        }
    }

    public static void Save(AppConfig cfg)
    {
        cfg.Version = AppConfig.CurrentVersion;
        var json = JsonSerializer.Serialize(cfg, JsonOptions);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, ConfigPath, overwrite: true); // 저장 도중 꺼져도 파일이 깨지지 않도록
    }

    private static AppConfig CreateDefault() => new()
    {
        Servers = { PalworldGame.Definition.CreateDefaultProfile() }
    };
}
