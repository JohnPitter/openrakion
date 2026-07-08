using System;
using System.Collections.Generic;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Grid de OCUPAÇÃO do chão andável de um mapa (célula 1×1 m; espaço do 0x30a == metros do .wld,
    /// identidade provada). É a colisão de parede do bot no modelo "input de cliente": o DONO da entidade
    /// simula o movimento (no bot, o servidor) e a posição publicada no 0x30a nunca entra em célula
    /// bloqueada. Fontes do chão:
    ///  1. SEED da rota verificada do mapa (corredores entre waypoints — <see cref="GolemWarLayouts"/>);
    ///  2. APRENDIZADO vivo: cada 0x30a de HUMANO marca a célula pisada (parede interna = célula que
    ///     nenhum humano jamais pisa = bloqueada). Só humano ensina (bot não valida o próprio chão).
    /// Enquanto o grid de um mapa tem poucas células (mapa sem rota conhecida, começo de aprendizado),
    /// ele fica INATIVO (tudo aberto) — nunca prende o bot por falta de dado.
    /// </summary>
    public sealed class WalkGrid
    {
        /// <summary>Lado da célula (m). 1 m ≈ largura de passo; paredes têm ≥1 célula de espessura visual.</summary>
        public const float Cell = 1.0f;

        /// <summary>Células abertas mínimas p/ o grid virar colisão ativa (abaixo disso, tudo aberto).</summary>
        public const int ActiveThreshold = 200;

        private readonly HashSet<(short Cx, short Cz)> _open = new();
        private readonly object _lock = new();

        private static (short, short) Key(float x, float z) =>
            ((short)MathF.Floor(x / Cell), (short)MathF.Floor(z / Cell));

        /// <summary>Marca a célula pisada + vizinhança 3×3 (largura do corpo do personagem).</summary>
        public void MarkWalked(float x, float z)
        {
            var (cx, cz) = Key(x, z);
            lock (_lock)
                for (int ix = -1; ix <= 1; ix++)
                    for (int iz = -1; iz <= 1; iz++)
                        _open.Add(((short)(cx + ix), (short)(cz + iz)));
        }

        /// <summary>A posição é chão andável? (sempre true enquanto o grid está inativo).</summary>
        public bool IsOpen(float x, float z)
        {
            lock (_lock)
            {
                if (_open.Count < ActiveThreshold) return true;
                return _open.Contains(Key(x, z));
            }
        }

        /// <summary>Semeia um corredor retangular entre dois pontos (meia-largura em m) — a rota
        /// verificada do mapa vira chão conhecido antes de qualquer humano pisar.</summary>
        public void SeedPath(Vec3 a, Vec3 b, float halfWidth)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)(len / (Cell * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float px = a.X + dx * t, pz = a.Z + dz * t;
                for (float ox = -halfWidth; ox <= halfWidth; ox += Cell * 0.5f)
                    for (float oz = -halfWidth; oz <= halfWidth; oz += Cell * 0.5f)
                        MarkWalked(px + ox, pz + oz);
            }
        }

        // ---- registry por mapa (o chão não muda; vive o processo inteiro) ----
        private static readonly Dictionary<byte, WalkGrid> _byMap = new();
        private static readonly object _mapsLock = new();

        /// <summary>Grid do mapa (cria + semeia da rota conhecida na 1ª vez).</summary>
        public static WalkGrid For(byte mapId)
        {
            lock (_mapsLock)
            {
                if (_byMap.TryGetValue(mapId, out var g)) return g;
                g = new WalkGrid();
                Seed(g, mapId);
                _byMap[mapId] = g;
                return g;
            }
        }

        /// <summary>Semeia o grid com a rota verificada do mapa (waypoints + spawns dos dois times —
        /// <see cref="GolemWarLayouts.For"/> tem default gravity, como o clamp).</summary>
        private static void Seed(WalkGrid g, byte mapId)
        {
            var layout = GolemWarLayouts.For(mapId);
            const float corridor = 3.0f;                     // meia-largura do corredor da rota (m)
            for (byte team = 0; team <= 1; team++)
            {
                Vec3 prev = layout.SpawnFor(team);
                foreach (var wp in layout.RouteFor(team))
                {
                    g.SeedPath(prev, wp.Pos, corridor);
                    prev = wp.Pos;
                }
            }
        }
    }
}
