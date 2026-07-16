namespace RakionServer.Admin;

public static class CurrencyAdjustmentPolicy
{
    public static void Validate(CurrencyAdjustmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AccountId) || request.AccountId.Length > 16)
            throw new ArgumentException("Conta inválida.");
        if (request.Currency == CurrencyKind.Gold && request.GameInfoId <= 0)
            throw new ArgumentException("Conta sem perfil de jogo.");
        if (request.TargetBalance is < 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(request.TargetBalance));
        string reason = request.Reason.Trim();
        if (reason.Length is < 4 or > 200)
            throw new ArgumentException("Informe um motivo entre 4 e 200 caracteres.");
        if (string.IsNullOrWhiteSpace(request.OperatorId) || request.OperatorId.Length > 64)
            throw new ArgumentException("Operador inválido.");
    }
}
