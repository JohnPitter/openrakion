using System;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Common;

namespace RakionServer.Ranking;

public sealed class RankingJob(RankingRepository repository, int activeMonths)
{
    private readonly RankingRepository _repository = repository;
    private readonly int _activeMonths = activeMonths > 0
        ? activeMonths
        : throw new ArgumentOutOfRangeException(nameof(activeMonths));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        MySqlConnection source = await _repository.AcquireSourceAsync(cancellationToken);
        try
        {
            Log.Info("ranking", "carregando jogadores ativos dos últimos {0} meses", _activeMonths);
            CharacterRankSource[] characters = await RankingRepository.LoadCharactersAsync(
                source, _activeMonths, cancellationToken);
            ClanMemberRankSource[] members = await RankingRepository.LoadClanMembersAsync(source, cancellationToken);
            ClanRankSource[] clans = await RankingRepository.LoadClansAsync(source, cancellationToken);

            var projection = new RankingProjection(
                RankingRules.RankCharacters(characters),
                RankingRules.RankClanMembers(members),
                RankingRules.RankClans(clans));
            Log.Info("ranking", "projeção calculada: {0} personagens, {1} membros e {2} clãs",
                projection.Characters.Length, projection.ClanMembers.Length, projection.Clans.Length);

            await using MySqlConnection target = await _repository.OpenProjectionAsync(cancellationToken);
            await RankingRepository.PrepareStagingAsync(target, cancellationToken);
            await RankingRepository.WriteStagingAsync(target, projection, cancellationToken);
            await RankingRepository.UpdateCanonicalAsync(source, projection, cancellationToken);
            await RankingRepository.PublishAsync(target, cancellationToken);
            Log.Ok("ranking", "snapshot publicado e campos canônicos atualizados");
            try
            {
                await RankingRepository.CleanupPreviousAsync(target, cancellationToken);
            }
            catch (Exception exception)
            {
                Log.Warn("ranking", "snapshot publicado, mas a limpeza do anterior falhou: {0}", exception.Message);
            }
        }
        catch (Exception exception)
        {
            Log.Error("ranking", "atualização falhou; snapshot anterior preservado: {0}", exception);
            throw;
        }
        finally
        {
            await RankingRepository.ReleaseSourceAsync(source);
        }
    }
}
