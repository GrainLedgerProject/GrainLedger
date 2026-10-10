namespace GrainLedger.Modules.Identity;

public sealed record IssuedTokens(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, SessionUserDto User);
