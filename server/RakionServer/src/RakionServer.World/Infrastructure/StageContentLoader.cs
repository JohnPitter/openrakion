using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using RakionServer.World.Domain;

namespace RakionServer.World.Infrastructure;

public static class StageContentLoader
{
    private const string ResourceName = "RakionServer.World.Data.stage-content-v258.json";

    public static IReadOnlyList<StageContentDefinition> LoadEmbedded()
    {
        Assembly assembly = typeof(StageContentLoader).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName) ??
            throw new InvalidOperationException($"Recurso {ResourceName} ausente.");
        return JsonSerializer.Deserialize<List<StageContentDefinition>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
            throw new InvalidDataException("Catálogo de conteúdo de stages está vazio.");
    }
}
