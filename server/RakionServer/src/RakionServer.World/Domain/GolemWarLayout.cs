using System.Collections.Generic;

namespace RakionServer.World.Domain
{
    /// <summary>Ponto 3D no espaço do mundo (metros; X/Z horizontal, Y vertical/up — convenção SE1).
    /// CALIBRAÇÃO (FATO): o espaço do 0x30a == o espaço do .wld (IDENTIDADE, sem escala/offset) —
    /// validado byte-a-byte contra a captura de 2 jogadores (golem-war-map-geometry).</summary>
    public readonly record struct Vec3(float X, float Y, float Z);

    /// <summary>Tipo de waypoint da rota de objetivo do bot.</summary>
    public enum WaypointKind : byte
    {
        /// <summary>Ponto de passagem: só andar até e seguir (corredor pelas entradas, fugindo de parede).</summary>
        Nav = 0,
        /// <summary>Pad de teleporte (CTeleport touch-field): ao tocar, snapa o bot p/ <c>Target</c> (XZ).</summary>
        Teleport = 1,
        /// <summary>Golem a destruir: atacar até zerar. <c>Param</c> = alvo (0 = master time 0, 1 = master
        /// time 1, 2 = golem dourado neutro).</summary>
        Golem = 2,
    }

    /// <summary>Um waypoint da rota de objetivo: posição-alvo + tipo + destino/param do tipo.</summary>
    public readonly record struct Waypoint(Vec3 Pos, WaypointKind Kind, Vec3 Target = default, int Param = 0);

    /// <summary>Retângulo caminhável (AABB no plano XZ, espaço do .wld == 0x30a) da ARENA de um mapa. O bot é
    /// server-side (0x30a manda posição direta, sem física do cliente) — sem isto ele beelia por cima das
    /// paredes. Extraído da topologia real do <c>.wld</c> (gravity_topo.py): a arena de jogo é aberta dentro
    /// deste box; as paredes/pilares ficam FORA. Clampar a posição do bot ao box = colisão de parede fiel o
    /// bastante, sem navmesh. Objetivo mora em Z≈0, então o clamp não o atrapalha.</summary>
    public readonly record struct WalkBounds(float MinX, float MaxX, float MinZ, float MaxZ)
    {
        public float ClampX(float x) => x < MinX ? MinX : (x > MaxX ? MaxX : x);
        public float ClampZ(float z) => z < MinZ ? MinZ : (z > MaxZ ? MaxZ : z);
        public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

        /// <summary>Menor AABB que contém os dois boxes — usado p/ EXPANDIR o box estático do mapa com o
        /// chão empiricamente pisado pelos humanos (hull), sem nunca encolhê-lo.</summary>
        public WalkBounds Union(WalkBounds o) => new(
            o.MinX < MinX ? o.MinX : MinX, o.MaxX > MaxX ? o.MaxX : MaxX,
            o.MinZ < MinZ ? o.MinZ : MinZ, o.MaxZ > MaxZ ? o.MaxZ : MaxZ);
    }

    /// <summary>
    /// Geometria do objetivo Golem War de UM mapa: spawn por time + a rota ORDENADA de waypoints
    /// (corredor → golem dourado → golem inimigo). Coordenadas no espaço do mundo (.wld == 0x30a),
    /// extraídas da engine (golem-war-map-geometry). Domínio puro, sem I/O.
    /// </summary>
    public sealed record GolemWarLayout(
        Vec3 SpawnTeam0,
        Vec3 SpawnTeam1,
        IReadOnlyList<Waypoint> RouteTeam0,
        IReadOnlyList<Waypoint> RouteTeam1)
    {
        public IReadOnlyList<Waypoint> RouteFor(byte team) => team == 0 ? RouteTeam0 : RouteTeam1;
        public Vec3 SpawnFor(byte team) => team == 0 ? SpawnTeam0 : SpawnTeam1;
    }

    /// <summary>
    /// Registro de layouts de objetivo por mapa. GRAVITY (MapId 210) tem coords FATO do gravity.wld
    /// (golem-war-map-geometry): corredor leste-oeste simétrico, eixo Z≈0 LIVRE de paredes de X−30 a X+30;
    /// golem dourado central rebaixado; masters em ±40; spawns em ±50; SEM teleporte. A rota anda reto pelo
    /// corredor (passa pelas entradas, não pela parede) → golem dourado (centro) → golem INIMIGO (lado oposto).
    ///
    /// HIPÓTESE a validar in-game: time 0 = RED (+X), time 1 = BLUE (−X). Se invertido, espelhar.
    /// </summary>
    public static class GolemWarLayouts
    {
        private static readonly GolemWarLayout Gravity = new(
            SpawnTeam0: new Vec3(50f, 0f, 0f),     // RED no extremo +X
            SpawnTeam1: new Vec3(-50f, 0f, 0f),    // BLUE no extremo −X
            RouteTeam0: new[]   // RED: corredor Z≈0 → golem dourado (entrada) → golem BLUE inimigo (−40)
            {
                new Waypoint(new Vec3(40f, 0f, 0f), WaypointKind.Nav),
                new Waypoint(new Vec3(20f, 0f, 0f), WaypointKind.Nav),
                new Waypoint(new Vec3(2f, 0f, -1f), WaypointKind.Golem, Param: 2),   // golem dourado (zone id101)
                new Waypoint(new Vec3(-40f, 0f, 0f), WaypointKind.Golem, Param: 1),  // master BLUE (inimigo)
            },
            RouteTeam1: new[]   // BLUE: espelho → golem RED inimigo (+40)
            {
                new Waypoint(new Vec3(-40f, 0f, 0f), WaypointKind.Nav),
                new Waypoint(new Vec3(-20f, 0f, 0f), WaypointKind.Nav),
                new Waypoint(new Vec3(2f, 0f, -1f), WaypointKind.Golem, Param: 2),
                new Waypoint(new Vec3(40f, 0f, 0f), WaypointKind.Golem, Param: 0),   // master RED (inimigo)
            });

        private static readonly IReadOnlyDictionary<byte, GolemWarLayout> ByMapId =
            new Dictionary<byte, GolemWarLayout> { [210] = Gravity };

        /// <summary>AABB caminhável por mapa (só onde a topologia real foi extraída — gravity 210). GRAVITY: arena
        /// aberta em Z∈[-6,6] (marcadores de jogo), pilares/paredes a partir de Z=±12 → clamp Z∈[-9,9] (dentro
        /// das paredes, com folga); spawns em X=±50 → clamp X∈[-54,54]. Só clampa mapas CONHECIDOS: mapa
        /// desconhecido = sem clamp (não constranger geometria não-verificada).</summary>
        // Z apertado ao CHÃO jogável real (marcadores da topologia em Z∈[-6,6]; humano anda em Z[-4,1] na captura).
        // Frouxo (±9) deixava o bot ir a Z=-8.9 → fora do piso → flutuava/atravessava parede (e provável "unknown error"
        // de geometria no cliente). X mantém a largura dos spawns (±50) com folga curta.
        private static readonly WalkBounds GravityWalk = new WalkBounds(-52f, 52f, -6.5f, 6.5f);
        private static readonly IReadOnlyDictionary<byte, WalkBounds> WalkByMapId =
            new Dictionary<byte, WalkBounds> { [210] = GravityWalk };

        /// <summary>Layout do objetivo p/ um mapa. Default = gravity (mapa de teste atual); segmentar quando a
        /// codificação real do <paramref name="mapId"/> no 0x3b for confirmada pelo log de decisão (sonda).</summary>
        public static GolemWarLayout For(byte mapId) =>
            ByMapId.TryGetValue(mapId, out var l) ? l : Gravity;

        /// <summary>AABB caminhável do mapa. Default = gravity (igual ao <see cref="For"/>): enquanto só gravity
        /// tem geometria, todo mapa cai nela, então o clamp também — senão o bot atravessa parede no mapa cujo
        /// MapId não é exatamente 210. Segmentar junto com <see cref="For"/> quando outros mapas entrarem.</summary>
        public static WalkBounds? WalkFor(byte mapId) =>
            WalkByMapId.TryGetValue(mapId, out var w) ? w : GravityWalk;
    }
}
